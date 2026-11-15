using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Addresses;

public sealed record AddressDto(
    string Id,
    AddressLabel Label,
    string? CustomLabel,
    string AddressLine,
    string? UnitNo,
    string City,
    string? Landmark,
    string? Notes,
    double Lat,
    double Lng,
    bool IsDefault)
{
    public static AddressDto From(Address a) => new(a.Id, a.Label, a.Label == AddressLabel.Other ? a.CustomLabel : null,
        a.Line, a.UnitNo, a.City, a.Landmark, a.Notes, a.Latitude, a.Longitude, a.IsDefault);
}

/// <summary>The create and replace body.</summary>
public sealed record AddressRequest(
    AddressLabel Label,
    string? CustomLabel,
    string AddressLine,
    string? UnitNo,
    string City,
    string? Landmark,
    string? Notes,
    double Lat,
    double Lng,
    bool IsDefault);

public sealed class AddressRequestValidator : AbstractValidator<AddressRequest>
{
    public AddressRequestValidator()
    {
        RuleFor(x => x.AddressLine).NotEmpty().WithMessage("Enter the street and number.").MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().WithMessage("Enter the city.").MaximumLength(80);
        RuleFor(x => x.CustomLabel).MaximumLength(40);
        RuleFor(x => x.UnitNo).MaximumLength(40);
        RuleFor(x => x.Landmark).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

/// <summary>
/// A customer's pickup addresses. Exactly one is the default once there are any; it only moves to another
/// address, never away to none.
/// </summary>
public static class AddressEndpoints
{
    private const string NotFoundCode = "ADDRESS_NOT_FOUND";

    public static void MapAddresses(this RouteGroupBuilder api)
    {
        var addresses = api.MapGroup("/addresses");
        addresses.MapGet("", List);
        addresses.MapPost("", Create).Validate<AddressRequest>();
        addresses.MapPut("/{id}", Replace).Validate<AddressRequest>();
        addresses.MapDelete("/{id}", Delete);
        addresses.MapPost("/{id}/default", MakeDefault);
    }

    /// <summary>Sri Lanka, by default. <c>ServiceArea:*</c> narrows it.</summary>
    private static void RequireServiceArea(AddressRequest request, IConfiguration config)
    {
        var section = config.GetSection("ServiceArea");
        var (minLat, maxLat) = (section.GetValue("MinLatitude", 5.85), section.GetValue("MaxLatitude", 9.9));
        var (minLng, maxLng) = (section.GetValue("MinLongitude", 79.5), section.GetValue("MaxLongitude", 81.95));
        if (request.Lat < minLat || request.Lat > maxLat || request.Lng < minLng || request.Lng > maxLng)
            throw ApiException.Unprocessable("OUTSIDE_SERVICE_AREA", "We don't collect from there yet.");
    }

    private static async Task<Ok<ItemList<AddressDto>>> List(CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var items = await db.Addresses.Where(a => a.CustomerId == customer.Id)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt).ToListAsync(ct);
        return TypedResults.Ok(new ItemList<AddressDto>(items.Select(AddressDto.From).ToList()));
    }

    private static async Task<Created<AddressDto>> Create(AddressRequest request, CurrentUser user, AppDbContext db,
        IConfiguration config, TimeProvider time, CancellationToken ct)
    {
        RequireServiceArea(request, config);
        var customer = await user.CustomerAsync(ct);
        var others = await db.Addresses.Where(a => a.CustomerId == customer.Id).ToListAsync(ct);
        var address = new Address
        {
            Id = Ids.New("adr"),
            CustomerId = customer.Id,
            Line = "",
            Area = "",
            CreatedAt = time.UtcNow(),
        };
        Apply(address, request);
        address.IsDefault = request.IsDefault || others.Count == 0;
        if (address.IsDefault) others.ForEach(a => a.IsDefault = false);
        // Clear the old default first: the database allows only one default per customer.
        await db.SaveChangesAsync(ct);
        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/addresses/{address.Id}", AddressDto.From(address));
    }

    /// <summary>Replaces the address. <c>isDefault: false</c> on the default is ignored.</summary>
    private static async Task<Ok<AddressDto>> Replace(string id, AddressRequest request, CurrentUser user,
        AppDbContext db, IConfiguration config, CancellationToken ct)
    {
        RequireServiceArea(request, config);
        var customer = await user.CustomerAsync(ct);
        var all = await db.Addresses.Where(a => a.CustomerId == customer.Id).ToListAsync(ct);
        var address = all.SingleOrDefault(a => a.Id == id) ?? throw NotFound();
        Apply(address, request);
        if (request.IsDefault && !address.IsDefault) await MoveDefaultAsync(db, all, address, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AddressDto.From(address));
    }

    private static async Task<Ok<AddressDto>> MakeDefault(string id, CurrentUser user, AppDbContext db,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var all = await db.Addresses.Where(a => a.CustomerId == customer.Id).ToListAsync(ct);
        var address = all.SingleOrDefault(a => a.Id == id) ?? throw NotFound();
        if (!address.IsDefault) await MoveDefaultAsync(db, all, address, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AddressDto.From(address));
    }

    /// <summary>Can't go while an open pickup is booked to it; the oldest remaining becomes the default.</summary>
    private static async Task<NoContent> Delete(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var all = await db.Addresses.Where(a => a.CustomerId == customer.Id).ToListAsync(ct);
        var address = all.SingleOrDefault(a => a.Id == id) ?? throw NotFound();
        if (await db.Pickups.AnyAsync(p => p.AddressId == id &&
                p.Status != PickupStatus.DeliveredToHub && p.Status != PickupStatus.Cancelled, ct))
            throw ApiException.Conflict("ADDRESS_IN_USE", "A pickup is booked to this address.");

        db.Addresses.Remove(address);
        await db.SaveChangesAsync(ct);
        if (address.IsDefault && all.Where(a => a.Id != id).MinBy(a => a.CreatedAt) is { } next)
        {
            next.IsDefault = true;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task MoveDefaultAsync(AppDbContext db, List<Address> all, Address address, CancellationToken ct)
    {
        all.ForEach(a => a.IsDefault = false);
        await db.SaveChangesAsync(ct);
        address.IsDefault = true;
    }

    private static void Apply(Address address, AddressRequest request)
    {
        address.Label = request.Label;
        address.CustomLabel = request.Label == AddressLabel.Other ? Clean(request.CustomLabel) : null;
        address.Line = request.AddressLine.Trim();
        address.UnitNo = Clean(request.UnitNo);
        address.City = request.City.Trim();
        address.Landmark = Clean(request.Landmark);
        address.Notes = Clean(request.Notes);
        address.Latitude = request.Lat;
        address.Longitude = request.Lng;
        address.Area = Areas.NameFor(request.Lat, request.Lng, address.City);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ApiException NotFound() => ApiException.NotFound(NotFoundCode, "Address not found.");
}
