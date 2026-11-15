using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Customer.Devices;

public sealed record DeviceRequest(string Token, string Platform, string? AppVersion, Language? Locale);

public sealed class DeviceRequestValidator : AbstractValidator<DeviceRequest>
{
    public DeviceRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Platform).Must(p => p is "android" or "ios").WithMessage("Use android or ios.");
        RuleFor(x => x.AppVersion).MaximumLength(30);
    }
}

/// <summary>Push tokens. A token belongs to whoever registered it last: one phone, two accounts.</summary>
public static class DeviceEndpoints
{
    public static void MapDevices(this RouteGroupBuilder api)
    {
        api.MapPost("/devices", Register).Validate<DeviceRequest>();
        api.MapDelete("/devices/{token}", Remove);
    }

    private static async Task<NoContent> Register(DeviceRequest request, CurrentUser user, AppDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Token == request.Token, ct);
        if (device is null)
        {
            device = new Device { Token = request.Token, CustomerId = customer.Id, Platform = request.Platform };
            db.Devices.Add(device);
        }

        device.CustomerId = customer.Id;
        device.Platform = request.Platform;
        device.AppVersion = request.AppVersion;
        device.Locale = request.Locale ?? customer.Language;
        device.UpdatedAt = time.UtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>204 even for an unknown token: signing out shouldn't fail.</summary>
    private static async Task<NoContent> Remove(string token, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        await db.Devices.Where(d => d.Token == token && d.CustomerId == customer.Id).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }
}
