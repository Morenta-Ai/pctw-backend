using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Orders;

public sealed record CartLine(string ProductId, int Quantity);

public sealed record QuoteRequest(IReadOnlyList<CartLine> Items, FulfilmentType DeliveryMethod, string? AddressId,
    string? VendorId);

public sealed record QuoteDto(
    IReadOnlyList<QuoteDto.Line> Items,
    long SubtotalCents,
    long? ShippingFeeCents,
    long? TransportChargeCents,
    long? DiscountCents,
    long TotalCents,
    string Currency,
    int EstimatedDeliveryDays,
    IReadOnlyList<string> UnavailableItemIds)
{
    public sealed record Line(string ProductId, string Name, string Size, int Quantity, long UnitPriceCents,
        long LineTotalCents, int Stock, bool Available, string? ImageUrl);
}

public sealed record PlaceOrderRequest(IReadOnlyList<CartLine> Items, FulfilmentType DeliveryMethod, string? AddressId,
    string? VendorId, string? ContactName, string? ContactMobile, long QuoteTotalCents);

public sealed record PlacedOrder(string OrderId, string OrderNumber, long TotalCents, bool PaymentRequired);

public sealed record PaymentConfirmation(string Status, string? PaymentReference);

/// <summary>The user app's names for an order's progress, which differ by delivery method.</summary>
public enum CustomerOrderStatus
{
    Placed, Confirmed, Dispatched, OutForDelivery, Delivered, InTransitToVendor, ReadyForCollection, Collected, Cancelled,
}

public sealed record CustomerOrderDto(
    string Id,
    string OrderNumber,
    CustomerOrderStatus Status,
    FulfilmentType DeliveryMethod,
    IReadOnlyList<CustomerOrderDto.Line> Items,
    long SubtotalCents,
    long? ShippingFeeCents,
    long? TransportChargeCents,
    long? DiscountCents,
    long TotalCents,
    string Currency,
    string? ContactName,
    string? ContactMobile,
    string PaymentStatus,
    IReadOnlyList<CustomerOrderDto.Step> StatusHistory,
    CustomerOrderDto.AddressRef? Address,
    CustomerOrderDto.VendorRef? Vendor,
    DateOnly? EstimatedDeliveryDate,
    DateTime CreatedAt)
{
    public sealed record Line(string ProductId, string Name, string Size, int Quantity, long UnitPriceCents, string? ImageUrl);

    public sealed record Step(CustomerOrderStatus Status, DateTime OccurredAt);

    public sealed record AddressRef(string? Id, string Line);

    public sealed record VendorRef(string Id, string Name, string Address, string OpeningHours, string? Phone);

    public static CustomerOrderStatus StatusFor(OrderStatus status, FulfilmentType method) => status switch
    {
        OrderStatus.Placed => CustomerOrderStatus.Placed,
        OrderStatus.Confirmed => CustomerOrderStatus.Confirmed,
        OrderStatus.OutForDelivery => CustomerOrderStatus.OutForDelivery,
        OrderStatus.ReadyAtVendor => CustomerOrderStatus.ReadyForCollection,
        OrderStatus.Completed => method == FulfilmentType.HomeDelivery ? CustomerOrderStatus.Delivered : CustomerOrderStatus.Collected,
        _ => CustomerOrderStatus.Cancelled,
    };

    public static CustomerOrderDto From(Order o, IReadOnlyDictionary<string, string?> images)
    {
        var home = o.FulfilmentType == FulfilmentType.HomeDelivery;
        return new CustomerOrderDto(o.Id, o.Id, StatusFor(o.Status, o.FulfilmentType), o.FulfilmentType,
            o.Items.OrderBy(i => i.Id).Select(i => new Line(i.ProductId, i.Name, i.Size, i.Quantity, i.UnitPriceCents,
                images.GetValueOrDefault(i.ProductId))).ToList(),
            o.SubtotalCents, home ? o.TransportChargeCents : null, home ? null : o.TransportChargeCents, null, o.TotalCents,
            "LKR", o.ContactName, o.ContactMobile,
            o.PaymentStatus switch
            {
                Data.Entities.PaymentStatus.Paid or Data.Entities.PaymentStatus.Refunded => "paid",
                Data.Entities.PaymentStatus.Failed => "failed",
                _ => "pending",
            },
            o.Events.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).Select(e => new Step(StatusFor(e.Status, o.FulfilmentType), e.OccurredAt)).ToList(),
            home ? new AddressRef(o.AddressId, o.Destination) : null,
            o.Vendor is null ? null : new VendorRef(o.Vendor.Id, o.Vendor.Name, o.Vendor.Address, o.Vendor.OpeningHours, o.Vendor.Phone),
            o.Status == OrderStatus.Cancelled || o.EstimatedAt is null ? null : Colombo.DateOf(o.EstimatedAt.Value),
            o.CreatedAt);
    }
}

public sealed record OrderPage(IReadOnlyList<CustomerOrderDto> Items, int Page, int PageSize, bool HasMore);

/// <summary>The cart lives on the phone; the server prices it, takes the order and confirms the card payment.</summary>
public static class CustomerOrderEndpoints
{
    public static void MapCustomerOrders(this RouteGroupBuilder api)
    {
        var orders = api.MapGroup("/orders");
        orders.MapPost("/quote", Quote);
        orders.MapPost("", Place);
        orders.MapPost("/{id}/payment-confirmation", ConfirmPayment);
        orders.MapGet("", List);
        orders.MapGet("/{id}", Get);
    }

    private static List<OrderWorkflow.Line> Lines(IReadOnlyList<CartLine>? items) =>
        (items ?? []).GroupBy(i => i.ProductId).Select(g => new OrderWorkflow.Line(g.Key, g.Sum(i => i.Quantity))).ToList();

    private static async Task<Vendor?> VendorAsync(AppDbContext db, FulfilmentType method, string? vendorId, CancellationToken ct)
    {
        if (method != FulfilmentType.ClickAndCollect) return null;
        return await db.Vendors.SingleOrDefaultAsync(v => v.Id == vendorId && v.Active, ct)
               ?? throw ApiException.Conflict("VENDOR_UNAVAILABLE", "That collection point isn't taking collections.");
    }

    private static async Task<Ok<QuoteDto>> Quote(QuoteRequest request, CurrentUser user, AppDbContext db,
        OrderWorkflow orders, CancellationToken ct)
    {
        await user.CustomerAsync(ct);
        await VendorAsync(db, request.DeliveryMethod, request.VendorId, ct);
        var quote = await orders.QuoteAsync(Lines(request.Items), request.DeliveryMethod, ct);
        return TypedResults.Ok(new QuoteDto(quote.Lines.Select(l => new QuoteDto.Line(l.ProductId, l.Product?.Name ?? "",
                l.Product?.DefaultSize ?? "", l.Quantity, l.Product?.PriceCents ?? 0, l.LineTotalCents,
                Math.Max(0, l.Product?.Stock ?? 0), l.Available, l.Product?.ImageUrls.FirstOrDefault())).ToList(),
            quote.SubtotalCents, quote.ShippingFeeCents, quote.TransportChargeCents, null, quote.TotalCents, "LKR",
            quote.EstimatedDeliveryDays, quote.UnavailableItemIds));
    }

    private static async Task<IResult> Place(PlaceOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CurrentUser user, AppDbContext db,
        OrderWorkflow orders, TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw ApiException.BadRequest(ErrorCodes.BadRequest, "Send an Idempotency-Key header.");
        var hash = Secrets.Hash(JsonSerializer.Serialize(request, Json.Options));
        if (await Idempotency.FindAsync(db, customer.Id, "order", idempotencyKey, hash, ct) is { } earlier)
        {
            var existing = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == earlier, ct);
            return TypedResults.Created($"/api/v1/orders/{existing.Id}", new PlacedOrder(existing.Id, existing.Id, existing.TotalCents, true));
        }

        Address? address = null;
        if (request.DeliveryMethod == FulfilmentType.HomeDelivery)
        {
            address = await db.Addresses.SingleOrDefaultAsync(a => a.Id == request.AddressId && a.CustomerId == customer.Id, ct)
                      ?? throw ApiException.Unprocessable("ADDRESS_REQUIRED", "Choose one of your addresses.");
        }

        var vendor = await VendorAsync(db, request.DeliveryMethod, request.VendorId, ct);
        var order = await orders.PlaceAsync(new OrderWorkflow.Placement(customer, Lines(request.Items), request.DeliveryMethod,
            address, vendor, request.ContactName ?? customer.FullName, request.ContactMobile ?? customer.Mobile,
            request.QuoteTotalCents), ct);
        await Idempotency.SaveAsync(db, customer.Id, "order", idempotencyKey, hash, order.Id, time, ct);
        return TypedResults.Created($"/api/v1/orders/{order.Id}", new PlacedOrder(order.Id, order.Id, order.TotalCents, true));
    }

    private static async Task<Ok<CustomerOrderDto>> ConfirmPayment(string id, PaymentConfirmation request, CurrentUser user,
        AppDbContext db, OrderWorkflow orders, CancellationToken ct)
    {
        var order = await FindAsync(db, user, id, ct);
        await orders.ConfirmPaymentAsync(order, request.Status, request.PaymentReference, ct);
        return TypedResults.Ok(await DtoAsync(db, user, id, ct));
    }

    private static async Task<Ok<OrderPage>> List(int? page, int? pageSize, CurrentUser user, AppDbContext db,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var paging = PageRequest.From(page, pageSize ?? 20);
        var rows = await WithDetails(db.Orders).AsNoTracking().Where(o => o.CustomerId == customer.Id)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip(paging.Skip).Take(paging.PageSize + 1).ToListAsync(ct);
        var images = await ImagesAsync(db, rows, ct);
        return TypedResults.Ok(new OrderPage(rows.Take(paging.PageSize).Select(o => CustomerOrderDto.From(o, images)).ToList(),
            paging.Page, paging.PageSize, rows.Count > paging.PageSize));
    }

    private static async Task<Ok<CustomerOrderDto>> Get(string id, CurrentUser user, AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await DtoAsync(db, user, id, ct));

    private static IQueryable<Order> WithDetails(IQueryable<Order> orders) =>
        orders.Include(o => o.Items).Include(o => o.Events).Include(o => o.Vendor).AsSplitQuery();

    private static async Task<Order> FindAsync(AppDbContext db, CurrentUser user, string id, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        return await WithDetails(db.Orders).SingleOrDefaultAsync(o => o.Id == id && o.CustomerId == customer.Id, ct)
               ?? throw ApiException.NotFound(OrderWorkflow.OrderNotFound, "Order not found.");
    }

    private static async Task<CustomerOrderDto> DtoAsync(AppDbContext db, CurrentUser user, string id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var order = await FindAsync(db, user, id, ct);
        return CustomerOrderDto.From(order, await ImagesAsync(db, [order], ct));
    }

    private static async Task<Dictionary<string, string?>> ImagesAsync(AppDbContext db, IEnumerable<Order> orders,
        CancellationToken ct)
    {
        var ids = orders.SelectMany(o => o.Items).Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        return products.ToDictionary(p => p.Id, p => p.ImageUrls.FirstOrDefault());
    }
}
