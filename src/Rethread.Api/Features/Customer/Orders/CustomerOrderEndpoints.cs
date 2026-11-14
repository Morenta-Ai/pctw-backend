using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Orders;

public sealed record OrderLineDto(string ProductId, string Name, string Size, int Quantity, long UnitPriceCents)
{
    public static OrderLineDto From(OrderItem i) => new(i.ProductId, i.Name, i.Size, i.Quantity, i.UnitPriceCents);
}

public sealed record CustomerOrderDto(
    string Id,
    OrderStatus Status,
    IReadOnlyList<OrderLineDto> Items,
    long SubtotalCents,
    long TransportChargeCents,
    long TotalCents,
    FulfilmentType FulfilmentType,
    string Destination,
    PaymentMethod PaymentMethod,
    DateTime CreatedAt,
    DateTime? EstimatedAt)
{
    public static CustomerOrderDto From(Order o) => new(o.Id, o.Status,
        o.Items.OrderBy(i => i.Id).Select(OrderLineDto.From).ToList(), o.SubtotalCents, o.TransportChargeCents,
        o.TotalCents, o.FulfilmentType, o.Destination, o.PaymentMethod, o.CreatedAt, o.EstimatedAt);
}

public sealed record PlaceOrderRequest(
    IReadOnlyList<PlaceOrderRequest.Line> Items,
    FulfilmentType FulfilmentType,
    string? AddressId,
    string? VendorId,
    PaymentMethod PaymentMethod)
{
    public sealed record Line(string ProductId, string Size, int Quantity);
}

public sealed class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Your bag is empty.");
        RuleForEach(x => x.Items).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Size).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 20);
        });
        RuleFor(x => x.AddressId).NotEmpty().When(x => x.FulfilmentType == FulfilmentType.HomeDelivery)
            .WithMessage("Choose a delivery address.");
        RuleFor(x => x.VendorId).NotEmpty().When(x => x.FulfilmentType == FulfilmentType.ClickAndCollect)
            .WithMessage("Choose a collection point.");
    }
}

public static class CustomerOrderEndpoints
{
    public static void MapCustomerOrders(this RouteGroupBuilder api)
    {
        var orders = api.MapGroup("/orders");
        orders.MapPost("", Place).Validate<PlaceOrderRequest>();
        orders.MapGet("", List);
        orders.MapGet("/{id}", Get);
    }

    private static async Task<Created<CustomerOrderDto>> Place(PlaceOrderRequest request, CurrentUser user,
        AppDbContext db, OrderWorkflow orders, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        Address? address = null;
        Vendor? vendor = null;
        if (request.FulfilmentType == FulfilmentType.HomeDelivery)
        {
            address = await db.Addresses.SingleOrDefaultAsync(a => a.Id == request.AddressId && a.CustomerId == customer.Id, ct)
                      ?? throw ApiException.Validation("addressId", "Choose one of your addresses.");
        }
        else
        {
            vendor = await db.Vendors.SingleOrDefaultAsync(v => v.Id == request.VendorId, ct)
                     ?? throw ApiException.Validation("vendorId", "Choose one of the collection points.");
        }

        var lines = request.Items.GroupBy(l => (l.ProductId, l.Size))
            .Select(g => new OrderWorkflow.Line(g.Key.ProductId, g.Key.Size, g.Sum(l => l.Quantity))).ToList();
        var order = await orders.PlaceAsync(new OrderWorkflow.Placement(customer, lines, request.FulfilmentType,
            address, vendor, request.PaymentMethod), ct);
        return TypedResults.Created($"/api/v1/orders/{order.Id}", CustomerOrderDto.From(order));
    }

    private static async Task<Ok<CursorPage<CustomerOrderDto>>> List(string? cursor, CurrentUser user, AppDbContext db,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        return TypedResults.Ok(await db.Orders.Include(o => o.Items).AsNoTracking()
            .Where(o => o.CustomerId == customer.Id)
            .ToCursorPageAsync(cursor, 20, CustomerOrderDto.From, ct));
    }

    private static async Task<Ok<CustomerOrderDto>> Get(string id, CurrentUser user, AppDbContext db,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var order = await db.Orders.Include(o => o.Items).AsNoTracking()
                        .SingleOrDefaultAsync(o => o.Id == id && o.CustomerId == customer.Id, ct)
                    ?? throw ApiException.NotFound(OrderWorkflow.OrderNotFound, "Order not found.");
        return TypedResults.Ok(CustomerOrderDto.From(order));
    }
}
