using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Admin.Redemptions;
using Rethread.Api.Features.Customer.Orders;

namespace Rethread.Api.Features.Admin.Orders;

public sealed record OrderRow(
    string Id,
    OrderStatus Status,
    string CustomerName,
    int ItemCount,
    long TotalCents,
    FulfilmentType FulfilmentType,
    string? VendorName,
    string? WaybillNumber,
    DateTime CreatedAt);

public sealed record VendorRef(string Id, string Name, string Address);

public sealed record OrderEventDto(OrderStatus Status, DateTime OccurredAt, string? Actor);

public sealed record OrderDetail(
    string Id,
    OrderStatus Status,
    PersonRef Customer,
    IReadOnlyList<OrderLineDto> Items,
    long SubtotalCents,
    long TransportChargeCents,
    long TotalCents,
    FulfilmentType FulfilmentType,
    string Destination,
    VendorRef? Vendor,
    PaymentMethod PaymentMethod,
    string? WaybillNumber,
    DateTime CreatedAt,
    IReadOnlyList<OrderEventDto> Timeline)
{
    public static OrderDetail From(Order o) => new(o.Id, o.Status,
        new PersonRef(o.CustomerId, o.Customer!.FullName, o.Customer.Mobile),
        o.Items.OrderBy(i => i.Id).Select(OrderLineDto.From).ToList(), o.SubtotalCents, o.TransportChargeCents,
        o.TotalCents, o.FulfilmentType, o.Destination,
        o.Vendor is null ? null : new VendorRef(o.Vendor.Id, o.Vendor.Name, o.Vendor.Address),
        o.PaymentMethod, o.WaybillNumber, o.CreatedAt,
        o.Events.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Select(e => new OrderEventDto(e.Status, e.OccurredAt, e.Actor)).ToList());
}

public sealed record OrderStatusRequest(OrderStatus Status);

public static class AdminOrderEndpoints
{
    public static void MapAdminOrders(this RouteGroupBuilder admin)
    {
        var orders = admin.MapGroup("/orders");
        orders.MapGet("", List);
        orders.MapGet("/{id}", Get);
        orders.MapPost("/{id}/status", SetStatus);
    }

    private static async Task<Ok<PagedList<OrderRow>>> List(string? status, string? fulfilmentType, string? q,
        int? page, int? pageSize, AppDbContext db, CancellationToken ct)
    {
        var query = db.Orders.Include(o => o.Customer).Include(o => o.Vendor).Include(o => o.Items).AsNoTracking();
        if (status is not null)
        {
            var s = CamelCaseEnumConverter<OrderStatus>.TryParse(status)
                    ?? throw ApiException.Validation("status", "That isn't an order status.");
            query = query.Where(o => o.Status == s);
        }

        if (fulfilmentType is not null)
        {
            var f = CamelCaseEnumConverter<FulfilmentType>.TryParse(fulfilmentType)
                    ?? throw ApiException.Validation("fulfilmentType", "Use homeDelivery or clickAndCollect.");
            query = query.Where(o => o.FulfilmentType == f);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(o => EF.Functions.ILike(o.Id, term) ||
                                     EF.Functions.ILike(o.Customer!.FirstName + " " + o.Customer.LastName, term) ||
                                     EF.Functions.ILike(o.Customer.Mobile ?? "", term) ||
                                     EF.Functions.ILike(o.WaybillNumber ?? "", term));
        }

        query = query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id);
        return TypedResults.Ok(await query.AsSplitQuery().ToPageAsync(PageRequest.From(page, pageSize), o => new OrderRow(
            o.Id, o.Status, o.Customer!.FullName, o.Items.Sum(i => i.Quantity), o.TotalCents, o.FulfilmentType,
            o.Vendor?.Name, o.WaybillNumber, o.CreatedAt), ct));
    }

    private static async Task<Ok<OrderDetail>> Get(string id, AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(OrderDetail.From(await FindAsync(db, id, ct)));

    private static async Task<Ok<OrderDetail>> SetStatus(string id, OrderStatusRequest request, CurrentUser user,
        AppDbContext db, OrderWorkflow orders, CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        var order = await FindAsync(db, id, ct);
        await orders.ChangeStatusAsync(order, request.Status, staff.Name, ct);
        db.ChangeTracker.Clear();
        return TypedResults.Ok(OrderDetail.From(await FindAsync(db, id, ct)));
    }

    private static async Task<Order> FindAsync(AppDbContext db, string id, CancellationToken ct) =>
        await db.Orders.Include(o => o.Customer).Include(o => o.Vendor).Include(o => o.Items).Include(o => o.Events)
            .AsSplitQuery().SingleOrDefaultAsync(o => o.Id == id, ct)
        ?? throw ApiException.NotFound(OrderWorkflow.OrderNotFound, "Order not found.");
}
