using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Addresses;

public sealed record AddressDto(string Id, AddressLabel Label, string Line, double Latitude, double Longitude,
    bool IsDefault)
{
    public static AddressDto From(Address a) => new(a.Id, a.Label, a.Line, a.Latitude, a.Longitude, a.IsDefault);
}

public sealed record CreateAddressRequest(AddressLabel Label, string Line, double Latitude, double Longitude,
    bool IsDefault);

public sealed record UpdateAddressRequest(AddressLabel? Label, string? Line, double? Latitude, double? Longitude,
    bool? IsDefault);

public sealed class CreateAddressRequestValidator : AbstractValidator<CreateAddressRequest>
{
    public CreateAddressRequestValidator()
    {
        RuleFor(x => x.Line).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Latitude).InclusiveBetween(5.5, 10.0).WithMessage("Pin a location in Sri Lanka.");
        RuleFor(x => x.Longitude).InclusiveBetween(79.4, 82.0).WithMessage("Pin a location in Sri Lanka.");
    }
}

public sealed class UpdateAddressRequestValidator : AbstractValidator<UpdateAddressRequest>
{
    public UpdateAddressRequestValidator()
    {
        RuleFor(x => x.Line).NotEmpty().MaximumLength(200).When(x => x.Line is not null);
        RuleFor(x => x.Latitude).InclusiveBetween(5.5, 10.0).When(x => x.Latitude is not null)
            .WithMessage("Pin a location in Sri Lanka.");
        RuleFor(x => x.Longitude).InclusiveBetween(79.4, 82.0).When(x => x.Longitude is not null)
            .WithMessage("Pin a location in Sri Lanka.");
    }
}

/// <summary>A customer's pickup addresses. Exactly one is the default once there are any.</summary>
public static class AddressEndpoints
{
    private const string NotFoundCode = "ADDRESS_NOT_FOUND";

    public static void MapAddresses(this RouteGroupBuilder api)
    {
        var addresses = api.MapGroup("/addresses");
        addresses.MapGet("", List);
        addresses.MapPost("", Create).Validate<CreateAddressRequest>();
        addresses.MapPatch("/{id}", Update).Validate<UpdateAddressRequest>();
        addresses.MapDelete("/{id}", Delete);
    }

    private static async Task<Ok<ItemList<AddressDto>>> List(CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var items = await db.Addresses.Where(a => a.CustomerId == customer.Id)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt).ToListAsync(ct);
        return TypedResults.Ok(new ItemList<AddressDto>(items.Select(AddressDto.From).ToList()));
    }

    private static async Task<Created<AddressDto>> Create(CreateAddressRequest request, CurrentUser user,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var others = await db.Addresses.Where(a => a.CustomerId == customer.Id).ToListAsync(ct);
        var address = new Address
        {
            Id = Ids.New("adr"),
            CustomerId = customer.Id,
            Label = request.Label,
            Line = request.Line.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Area = Areas.NameFor(request.Latitude, request.Longitude, request.Line),
            IsDefault = request.IsDefault || others.Count == 0,
            CreatedAt = time.UtcNow(),
        };
        if (address.IsDefault) others.ForEach(a => a.IsDefault = false);
        // Clear the old default first: the database allows only one default per customer.
        await db.SaveChangesAsync(ct);
        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/addresses/{address.Id}", AddressDto.From(address));
    }

    private static async Task<Ok<AddressDto>> Update(string id, UpdateAddressRequest request, CurrentUser user,
        AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var all = await db.Addresses.Where(a => a.CustomerId == customer.Id).ToListAsync(ct);
        var address = all.SingleOrDefault(a => a.Id == id)
                      ?? throw ApiException.NotFound(NotFoundCode, "Address not found.");

        if (request.IsDefault == false && address.IsDefault)
            throw ApiException.Validation("isDefault", "Make another address the default instead.");

        if (request.Label is { } label) address.Label = label;
        if (request.Line is not null) address.Line = request.Line.Trim();
        if (request.Latitude is { } lat) address.Latitude = lat;
        if (request.Longitude is { } lng) address.Longitude = lng;
        if (request.Line is not null || request.Latitude is not null || request.Longitude is not null)
            address.Area = Areas.NameFor(address.Latitude, address.Longitude, address.Line);

        if (request.IsDefault == true && !address.IsDefault)
        {
            all.ForEach(a => a.IsDefault = false);
            await db.SaveChangesAsync(ct);
            address.IsDefault = true;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AddressDto.From(address));
    }

    private static async Task<NoContent> Delete(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var all = await db.Addresses.Where(a => a.CustomerId == customer.Id).ToListAsync(ct);
        var address = all.SingleOrDefault(a => a.Id == id)
                      ?? throw ApiException.NotFound(NotFoundCode, "Address not found.");
        db.Addresses.Remove(address);
        await db.SaveChangesAsync(ct);

        // Pickups keep their own copy of the address, so deleting one never changes a booking.
        if (address.IsDefault && all.Where(a => a.Id != id).MaxBy(a => a.CreatedAt) is { } next)
        {
            next.IsDefault = true;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }
}
