using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Features.Customer.Profile;

namespace Rethread.Api.Features.Customer.Auth;

public sealed record CustomerOtpRequest(string Channel, string Destination);

public sealed record CustomerSession(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    bool IsNewUser,
    CustomerMe User);

public sealed class CustomerOtpRequestValidator : AbstractValidator<CustomerOtpRequest>
{
    public CustomerOtpRequestValidator()
    {
        RuleFor(x => x.Channel).Must(c => c is "mobile" or "email").WithMessage("Use mobile or email.");
        RuleFor(x => x.Destination).Mobile().When(x => x.Channel == "mobile");
        RuleFor(x => x.Destination).NotEmpty().EmailAddress().When(x => x.Channel == "email");
    }
}

/// <summary>Sign-in for the user app. Sign-up and login are the same flow: the account is made on first verify.</summary>
public static class CustomerAuthEndpoints
{
    public static void MapCustomerAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/otp/request", RequestOtp).Validate<CustomerOtpRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        auth.MapPost("/otp/verify", VerifyOtp).Validate<OtpVerifyRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        auth.MapPost("/refresh", Refresh).Validate<RefreshRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        auth.MapPost("/logout", Logout);
    }

    private static async Task<Ok<OtpChallengeResponse>> RequestOtp(CustomerOtpRequest request, OtpService otp,
        CancellationToken ct)
    {
        var destination = Normalise(request.Channel, request.Destination);
        var challenge = await otp.SendAsync(Audience.Customer, request.Channel, destination, null, ct);
        return TypedResults.Ok(new OtpChallengeResponse(challenge.ChallengeId, challenge.ExpiresAt,
            challenge.ResendAfterSeconds));
    }

    private static async Task<Ok<CustomerSession>> VerifyOtp(OtpVerifyRequest request, OtpService otp,
        TokenService tokens, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var challenge = await otp.VerifyAsync(Audience.Customer, request.ChallengeId, request.Code, ct);
        var byEmail = challenge.Channel == "email";
        var customer = await db.Customers.SingleOrDefaultAsync(
            c => byEmail ? c.Email == challenge.Destination : c.Mobile == challenge.Destination, ct);

        var created = customer is null;
        if (customer is null)
        {
            customer = new Data.Entities.Customer
            {
                Id = Ids.New("cus"),
                Mobile = byEmail ? null : challenge.Destination,
                Email = byEmail ? challenge.Destination : null,
                CreatedAt = time.UtcNow(),
            };
            db.Customers.Add(customer);
        }
        else if (customer.Status == AccountStatus.Suspended)
        {
            await db.SaveChangesAsync(ct);
            throw CurrentUser.Suspended();
        }

        var issued = tokens.Issue(Audience.Customer, customer.Id, Roles.Customer);
        await db.SaveChangesAsync(ct);
        var hasBank = !created && await db.BankDetails.AnyAsync(b => b.CustomerId == customer.Id, ct);
        return TypedResults.Ok(new CustomerSession(issued.AccessToken, issued.RefreshToken, issued.ExpiresAt,
            created || customer.FirstName is null, CustomerMe.From(customer, hasBank)));
    }

    private static async Task<Ok<TokenPair>> Refresh(RefreshRequest request, TokenService tokens, AppDbContext db,
        CancellationToken ct)
    {
        var used = await tokens.UseRefreshTokenAsync(Audience.Customer, request.RefreshToken, ct);
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == used.SubjectId, ct);
        if (customer is null || customer.Status == AccountStatus.Suspended)
        {
            await db.SaveChangesAsync(ct);
            throw customer is null
                ? ApiException.Unauthorized(AuthErrors.SessionExpired, "Sign in again.")
                : CurrentUser.Suspended();
        }

        var issued = tokens.Issue(Audience.Customer, customer.Id, Roles.Customer);
        await tokens.LinkAsync(used, issued.RefreshToken, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TokenPair(issued.AccessToken, issued.RefreshToken, issued.ExpiresAt));
    }

    private static async Task<NoContent> Logout(CurrentUser user, TokenService tokens, CancellationToken ct)
    {
        await tokens.RevokeAllAsync(Audience.Customer, user.Id, ct);
        return TypedResults.NoContent();
    }

    private static string Normalise(string channel, string destination) =>
        channel == "email" ? destination.Trim().ToLowerInvariant() : destination.Trim();
}
