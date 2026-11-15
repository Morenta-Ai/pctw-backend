using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>Marketplace orders: placing them (stock and wallet) and moving them through fulfilment.</summary>
public sealed class OrderWorkflow(
    AppDbContext db,
    RateBook rates,
    WalletLedger wallet,
    NotificationService notifications,
    TimeProvider time)
{
    public const string ProductUnavailable = "PRODUCT_UNAVAILABLE";
    public const string OrderStatusChanged = "ORDER_STATUS_CHANGED";
    public const string OrderNotFound = "ORDER_NOT_FOUND";

    public sealed record Line(string ProductId, string Size, int Quantity);

    public sealed record Placement(
        Customer Customer,
        IReadOnlyList<Line> Lines,
        FulfilmentType FulfilmentType,
        Address? Address,
        Vendor? Vendor,
        PaymentMethod PaymentMethod);

    /// <summary>Who can move an order where. Delivery and collection each have their own middle step.</summary>
    public static IReadOnlyList<OrderStatus> NextStatuses(OrderStatus status, FulfilmentType fulfilment) => status switch
    {
        OrderStatus.Placed => [OrderStatus.Confirmed, OrderStatus.Cancelled],
        OrderStatus.Confirmed => fulfilment == FulfilmentType.HomeDelivery
            ? [OrderStatus.OutForDelivery, OrderStatus.Cancelled]
            : [OrderStatus.ReadyAtVendor, OrderStatus.Cancelled],
        OrderStatus.OutForDelivery or OrderStatus.ReadyAtVendor => [OrderStatus.Completed, OrderStatus.Cancelled],
        _ => [],
    };

    /// <summary>
    /// Places an order. Stock is taken with a conditional update, so two shoppers can't both buy the last
    /// one; wallet orders are paid under the same lock as redemptions. Card orders are recorded unpaid until
    /// a payment gateway is chosen.
    /// </summary>
    public async Task<Order> PlaceAsync(Placement placement, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.UtcNow();
        var id = $"ORD-{await db.NextAsync(Sequences.OrderNumber, ct)}";
        var items = new List<OrderItem>();
        foreach (var line in placement.Lines)
        {
            var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == line.ProductId, ct);
            if (product is null || !product.Published || !product.Sizes.Contains(line.Size))
                throw Unavailable(line.ProductId);
            var taken = await db.Products
                .Where(p => p.Id == line.ProductId && p.Stock >= line.Quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - line.Quantity), ct);
            if (taken == 0) throw Unavailable(line.ProductId);
            items.Add(new OrderItem
            {
                OrderId = id,
                ProductId = product.Id,
                Name = product.Name,
                Size = line.Size,
                Quantity = line.Quantity,
                UnitPriceCents = product.PriceCents,
            });
        }

        var rate = await rates.CurrentAsync(ct);
        var subtotal = items.Sum(i => i.UnitPriceCents * i.Quantity);
        var transport = placement.FulfilmentType == FulfilmentType.HomeDelivery
            ? rate.HomeDeliveryChargeCents
            : rate.ClickAndCollectChargeCents;
        var order = new Order
        {
            Id = id,
            CustomerId = placement.Customer.Id,
            FulfilmentType = placement.FulfilmentType,
            Destination = placement.Vendor?.Address ?? placement.Address!.FullLine,
            AddressId = placement.Address?.Id,
            VendorId = placement.Vendor?.Id,
            PaymentMethod = placement.PaymentMethod,
            PaymentStatus = placement.PaymentMethod == PaymentMethod.Wallet ? PaymentStatus.Paid : PaymentStatus.Unpaid,
            SubtotalCents = subtotal,
            TransportChargeCents = transport,
            TotalCents = subtotal + transport,
            CreatedAt = now,
            EstimatedAt = now.AddDays(placement.FulfilmentType == FulfilmentType.HomeDelivery ? 3 : 2),
            Items = items,
            Events = [new OrderEvent { OrderId = id, Status = OrderStatus.Placed, OccurredAt = now }],
        };

        if (placement.PaymentMethod == PaymentMethod.Wallet)
        {
            await wallet.LockAsync(placement.Customer.Id, ct);
            var balance = (await wallet.TotalsAsync(placement.Customer.Id, ct)).BalanceCents;
            if (balance < order.TotalCents)
            {
                throw ApiException.Conflict(Redemptions.InsufficientBalance, "Your wallet doesn't cover this order.",
                    new { balanceCents = balance });
            }

            wallet.Add(new WalletTransaction
            {
                Id = $"txn_{id}",
                CustomerId = order.CustomerId,
                Type = TransactionType.ShopOrder,
                Status = TransactionStatus.Completed,
                Title = $"Order {id}",
                AmountCents = -order.TotalCents,
                OccurredAt = now,
                OrderId = id,
            });
        }

        db.Orders.Add(order);
        var (title, body) = NotificationTexts.OrderPlaced(order, placement.Customer.FirstName);
        await notifications.ToStaffAsync(NotificationService.StaffTypes.OrderPlaced, order.Id, title, body, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return order;
    }

    /// <summary>
    /// Moves an order on. Confirming issues the waybill; cancelling restocks the items and refunds a wallet
    /// payment. Anything the table doesn't allow (someone else may have moved it) is <c>ORDER_STATUS_CHANGED</c>.
    /// </summary>
    public async Task ChangeStatusAsync(Order order, OrderStatus status, string actor, CancellationToken ct)
    {
        if (!NextStatuses(order.Status, order.FulfilmentType).Contains(status))
        {
            throw ApiException.Conflict(OrderStatusChanged,
                $"{order.Id} can't go from {CamelCaseEnumConverter<OrderStatus>.ToText(order.Status)} to {CamelCaseEnumConverter<OrderStatus>.ToText(status)}.",
                new { status = CamelCaseEnumConverter<OrderStatus>.ToText(order.Status) });
        }

        var now = time.UtcNow();
        if (status == OrderStatus.Confirmed)
        {
            var number = await db.NextAsync(Sequences.WaybillNumber, ct);
            order.WaybillNumber = $"WB-{Colombo.DateOf(now):yyMMdd}-{number % 10_000:0000}";
        }

        if (status == OrderStatus.Cancelled)
        {
            foreach (var item in order.Items)
            {
                await db.Products.Where(p => p.Id == item.ProductId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + item.Quantity), ct);
            }

            if (order.PaymentStatus == PaymentStatus.Paid)
            {
                if (order.PaymentMethod == PaymentMethod.Wallet)
                {
                    wallet.Add(new WalletTransaction
                    {
                        Id = $"txn_{order.Id}_refund",
                        CustomerId = order.CustomerId,
                        Type = TransactionType.ShopOrder,
                        Status = TransactionStatus.Completed,
                        Title = $"Refund for {order.Id}",
                        AmountCents = order.TotalCents,
                        OccurredAt = now,
                        OrderId = order.Id,
                    });
                }

                order.PaymentStatus = PaymentStatus.Refunded;
            }
        }

        order.Status = status;
        db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Status = status, OccurredAt = now, Actor = actor });
        var (title, body) = NotificationTexts.OrderUpdate(order);
        notifications.ToCustomer(order.CustomerId, NotificationService.CustomerTypes.OrderUpdate, title, body, "order",
            order.Id);
        await db.SaveChangesAsync(ct);
    }

    private static ApiException Unavailable(string productId) =>
        ApiException.Conflict(ProductUnavailable, "Something in your bag has sold out.", new { productId });
}
