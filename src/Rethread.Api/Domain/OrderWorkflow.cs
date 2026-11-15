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
    IPaymentGateway gateway,
    TimeProvider time)
{
    public const string ProductUnavailable = "PRODUCT_UNAVAILABLE";
    public const string OrderStatusChanged = "ORDER_STATUS_CHANGED";
    public const string OrderNotFound = "ORDER_NOT_FOUND";

    public sealed record Line(string ProductId, int Quantity);

    public sealed record QuoteLine(string ProductId, Product? Product, int Quantity, bool Available)
    {
        public long LineTotalCents => Available ? Product!.PriceCents * Quantity : 0;
    }

    public sealed record Quote(
        IReadOnlyList<QuoteLine> Lines,
        long SubtotalCents,
        long? ShippingFeeCents,
        long? TransportChargeCents,
        long TotalCents,
        int EstimatedDeliveryDays,
        IReadOnlyList<string> UnavailableItemIds);

    public sealed record Placement(
        Customer Customer,
        IReadOnlyList<Line> Lines,
        FulfilmentType FulfilmentType,
        Address? Address,
        Vendor? Vendor,
        string? ContactName,
        string? ContactMobile,
        long QuoteTotalCents);

    public static readonly TimeSpan PaymentHold = TimeSpan.FromMinutes(30);

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
    /// Prices a cart. A line without enough left is unavailable and left out of every total; the charges
    /// are null when nothing is available. Every amount the app shows comes from here.
    /// </summary>
    public async Task<Quote> QuoteAsync(IReadOnlyList<Line> lines, FulfilmentType fulfilment, CancellationToken ct)
    {
        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var quoted = lines.Select(l =>
        {
            var product = products.GetValueOrDefault(l.ProductId);
            return new QuoteLine(l.ProductId, product, l.Quantity,
                product is { Published: true } && product.Stock >= l.Quantity && l.Quantity > 0);
        }).ToList();
        var subtotal = quoted.Sum(l => l.LineTotalCents);
        var anything = quoted.Any(l => l.Available);
        var rate = await rates.CurrentAsync(ct);
        long? shipping = anything && fulfilment == FulfilmentType.HomeDelivery ? rate.HomeDeliveryChargeCents : null;
        long? transport = anything && fulfilment == FulfilmentType.ClickAndCollect ? rate.ClickAndCollectChargeCents : null;
        return new Quote(quoted, subtotal, shipping, transport, subtotal + (shipping ?? 0) + (transport ?? 0),
            fulfilment == FulfilmentType.HomeDelivery ? 3 : 2,
            quoted.Where(l => !l.Available).Select(l => l.ProductId).ToList());
    }

    /// <summary>
    /// Places a card order and holds its pieces while payment is taken. Stock is taken with a conditional
    /// update, so two shoppers can't both buy the last one. The total must be the one the customer agreed
    /// to. An order left unpaid is released after <see cref="PaymentHold"/>.
    /// </summary>
    public async Task<Order> PlaceAsync(Placement placement, CancellationToken ct)
    {
        var quote = await QuoteAsync(placement.Lines, placement.FulfilmentType, ct);
        if (quote.UnavailableItemIds.Count > 0)
            throw OutOfStock(quote.UnavailableItemIds);
        if (quote.TotalCents != placement.QuoteTotalCents)
            throw ApiException.Conflict("PRICE_CHANGED", "The total has changed.", new { totalCents = quote.TotalCents });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.UtcNow();
        var id = $"ORD-{await db.NextAsync(Sequences.OrderNumber, ct)}";
        foreach (var line in quote.Lines)
        {
            var taken = await db.Products
                .Where(p => p.Id == line.ProductId && p.Stock >= line.Quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - line.Quantity), ct);
            if (taken == 0) throw OutOfStock([line.ProductId]);
        }

        var order = new Order
        {
            Id = id,
            CustomerId = placement.Customer.Id,
            FulfilmentType = placement.FulfilmentType,
            Destination = placement.Vendor?.Address ?? placement.Address!.FullLine,
            AddressId = placement.Address?.Id,
            VendorId = placement.Vendor?.Id,
            PaymentMethod = PaymentMethod.Card,
            PaymentStatus = PaymentStatus.Unpaid,
            ContactName = placement.ContactName?.Trim(),
            ContactMobile = placement.ContactMobile?.Trim(),
            SubtotalCents = quote.SubtotalCents,
            TransportChargeCents = quote.ShippingFeeCents ?? quote.TransportChargeCents ?? 0,
            TotalCents = quote.TotalCents,
            CreatedAt = now,
            EstimatedAt = now.AddDays(quote.EstimatedDeliveryDays),
            Items = quote.Lines.Select(l => new OrderItem
            {
                OrderId = id,
                ProductId = l.ProductId,
                Name = l.Product!.Name,
                Size = l.Product.DefaultSize,
                Quantity = l.Quantity,
                UnitPriceCents = l.Product.PriceCents,
            }).ToList(),
            Events = [new OrderEvent { OrderId = id, Status = OrderStatus.Placed, OccurredAt = now }],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return order;
    }

    /// <summary>
    /// The app reports what the gateway said. Only the gateway's own record makes an order paid; then the
    /// call centre hears about it. A failed or cancelled payment can be retried on the same order.
    /// </summary>
    public async Task ConfirmPaymentAsync(Order order, string status, string? reference, CancellationToken ct)
    {
        if (order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Refunded || order.Status != OrderStatus.Placed)
            return;

        switch (status)
        {
            case "succeeded":
                if (string.IsNullOrEmpty(reference) || !await gateway.HasPaymentAsync(reference, order.TotalCents, ct))
                    throw new ApiException(StatusCodes.Status402PaymentRequired, "PAYMENT_FAILED", "The payment didn't go through.");
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaymentReference = reference;
                var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == order.CustomerId, ct);
                var (title, body) = NotificationTexts.OrderPlaced(order, customer.FirstName);
                await notifications.ToStaffAsync(NotificationService.StaffTypes.OrderPlaced, order.Id, title, body, ct);
                break;
            case "failed" or "cancelled":
                order.PaymentStatus = PaymentStatus.Failed;
                break;
            case "pending":
                order.PaymentReference = reference;
                break;
            default:
                throw ApiException.Validation("status", "Use succeeded, failed, cancelled or pending.");
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Cancels card orders still unpaid after <see cref="PaymentHold"/> and puts their pieces back.</summary>
    public async Task ReleaseUnpaidAsync(CancellationToken ct)
    {
        var cutoff = time.UtcNow() - PaymentHold;
        var stale = await db.Orders.Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Placed && o.PaymentMethod == PaymentMethod.Card &&
                        (o.PaymentStatus == PaymentStatus.Unpaid || o.PaymentStatus == PaymentStatus.Failed) &&
                        o.CreatedAt <= cutoff)
            .ToListAsync(ct);
        foreach (var order in stale) await ChangeStatusAsync(order, OrderStatus.Cancelled, null, ct);
    }

    /// <summary>
    /// Moves an order on. Confirming issues the waybill; cancelling restocks the items and refunds a wallet
    /// payment. Anything the table doesn't allow (someone else may have moved it) is <c>ORDER_STATUS_CHANGED</c>.
    /// </summary>
    public async Task ChangeStatusAsync(Order order, OrderStatus status, string? actor, CancellationToken ct)
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

    private static ApiException OutOfStock(IReadOnlyList<string> productIds) =>
        ApiException.Conflict("OUT_OF_STOCK", "Something in your bag has sold out.", new { productIds });
}
