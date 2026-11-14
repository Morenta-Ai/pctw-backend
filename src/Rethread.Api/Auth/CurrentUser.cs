using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Auth;

/// <summary>
/// The signed-in account for this request, loaded once from the database. Tokens can be up to an hour old,
/// so suspension and staff roles are always checked against the database, never the token alone.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor, AppDbContext db)
{
    private Customer? _customer;
    private Collector? _collector;
    private Staff? _staff;

    public string Id => Principal.FindFirstValue("sub")
                        ?? throw ApiException.Unauthorized(ErrorCodes.Unauthorized, "Sign in again.");

    public string? Role => Principal.FindFirstValue("role");

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public async Task<Customer> CustomerAsync(CancellationToken ct)
    {
        _customer ??= await db.Customers.SingleOrDefaultAsync(c => c.Id == Id, ct) ?? throw Gone();
        if (_customer.Status == AccountStatus.Suspended) throw Suspended();
        return _customer;
    }

    public async Task<Collector> CollectorAsync(CancellationToken ct)
    {
        _collector ??= await db.Collectors.SingleOrDefaultAsync(c => c.Id == Id, ct) ?? throw Gone();
        if (_collector.Status == AccountStatus.Suspended) throw Suspended();
        return _collector;
    }

    public async Task<Staff> StaffAsync(CancellationToken ct)
    {
        _staff ??= await db.Staff.SingleOrDefaultAsync(s => s.Id == Id, ct) ?? throw Gone();
        if (_staff.Status == AccountStatus.Suspended) throw Suspended();
        return _staff;
    }

    public static ApiException Suspended() =>
        ApiException.Forbidden(AuthErrors.AccountSuspended, "This account is suspended.");

    private static ApiException Gone() => ApiException.Unauthorized(ErrorCodes.Unauthorized, "Sign in again.");
}

/// <summary>Rejects suspended (or deleted) accounts on every authenticated request in a route group.</summary>
public sealed class ActiveAccountFilter(Audience audience) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.RequestServices.GetRequiredService<CurrentUser>();
        if (user.IsAuthenticated)
        {
            var ct = context.HttpContext.RequestAborted;
            _ = audience switch
            {
                Audience.Customer => (object)await user.CustomerAsync(ct),
                Audience.Collector => await user.CollectorAsync(ct),
                _ => await user.StaffAsync(ct),
            };
        }

        return await next(context);
    }
}

/// <summary>Admin-only staff endpoints: 403 <c>FORBIDDEN</c> for the call centre, by the role in the database.</summary>
public sealed class AdminOnlyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.RequestServices.GetRequiredService<CurrentUser>();
        var staff = await user.StaffAsync(context.HttpContext.RequestAborted);
        if (staff.Role != StaffRole.Admin)
            throw ApiException.Forbidden(ErrorCodes.Forbidden, "Only admins can do this.");
        return await next(context);
    }
}

public static class AuthEndpointExtensions
{
    public static TBuilder AdminOnly<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new AdminOnlyFilter());
        return builder;
    }
}
