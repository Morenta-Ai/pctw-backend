using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Customer.Profile;

public sealed record UpdateProfileRequest(string? FirstName, string? LastName, Language? Language);

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(60).When(x => x.FirstName is not null);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(60).When(x => x.LastName is not null);
    }
}

public static class ProfileEndpoints
{
    public static void MapCustomerProfile(this RouteGroupBuilder api)
    {
        api.MapGet("/me", Get);
        api.MapPatch("/me", Update).Validate<UpdateProfileRequest>();
    }

    private static async Task<Ok<CustomerMe>> Get(CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        return TypedResults.Ok(CustomerMe.From(customer,
            await db.BankDetails.AnyAsync(b => b.CustomerId == customer.Id, ct)));
    }

    private static async Task<Ok<CustomerMe>> Update(UpdateProfileRequest request, CurrentUser user,
        AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        if (request.FirstName is not null) customer.FirstName = request.FirstName.Trim();
        if (request.LastName is not null) customer.LastName = request.LastName.Trim();
        if (request.Language is { } language) customer.Language = language;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(CustomerMe.From(customer,
            await db.BankDetails.AnyAsync(b => b.CustomerId == customer.Id, ct)));
    }
}
