using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Customer.Profile;

namespace Rethread.Api.Features.Customer.Auth;

/// <summary><c>channel</c> is <c>sms</c> or <c>email</c>.</summary>
public sealed record CustomerOtpRequest(string Channel, string Destination);

public sealed record CustomerOtpChallenge(string RequestId, int ExpiresInSeconds, int ResendAfterSeconds);

public sealed record CustomerOtpVerify(string RequestId, string Code);

public sealed record CustomerSession(string AccessToken, string RefreshToken, int ExpiresIn, bool IsNewUser,
    CustomerMe User);

public sealed record CustomerTokens(string AccessToken, string RefreshToken, int ExpiresIn);

public sealed class CustomerOtpRequestValidator : AbstractValidator<CustomerOtpRequest>
{
    public CustomerOtpRequestValidator()
    {
        RuleFor(x => x.Channel).Must(c => c is "sms" or "email").WithMessage("Use sms or email.");
        RuleFor(x => x.Destination).Mobile().When(x => x.Channel == "sms");
        RuleFor(x => x.Destination).NotEmpty().EmailAddress().When(x => x.Channel == "email");
    }
}

public sealed class CustomerOtpVerifyValidator : AbstractValidator<CustomerOtpVerify>
{
    public CustomerOtpVerifyValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Matches(@"^\d{6}$").WithMessage("The code is 6 digits.");
    }
}

/// <summary>Sign-in for the user app. Sign-up and login are the same flow: the account is made on first verify.</summary>
public static class CustomerAuthEndpoints
{
    /// <summary>The app waits this long before offering a new code.</summary>
    public const int ResendAfterSeconds = 60;

    public static void MapCustomerAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/otp/request", RequestOtp).Validate<CustomerOtpRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        auth.MapPost("/otp/verify", VerifyOtp).Validate<CustomerOtpVerify>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        auth.MapPost("/refresh", Refresh).Validate<RefreshRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        // The refresh token is the credential: the app signs out even when its access token has expired.
        auth.MapPost("/logout", Logout).Validate<RefreshRequest>().AllowAnonymous();
    }

    public static string Channel(string appChannel) => appChannel == "email" ? "email" : "mobile";

    public static string Normalise(string appChannel, string destination) =>
        appChannel == "email" ? destination.Trim().ToLowerInvariant() : destination.Trim();

    private static async Task<Ok<CustomerOtpChallenge>> RequestOtp(CustomerOtpRequest request, OtpService otp,
        CancellationToken ct)
    {
        var challenge = await otp.SendAsync(Audience.Customer, Channel(request.Channel),
            Normalise(request.Channel, request.Destination), null, ct, resendAfterSeconds: ResendAfterSeconds);
        return TypedResults.Ok(new CustomerOtpChallenge(challenge.ChallengeId, challenge.ExpiresInSeconds,
            challenge.ResendAfterSeconds));
    }

    private static async Task<Ok<CustomerSession>> VerifyOtp(CustomerOtpVerify request, OtpService otp,
        TokenService tokens, AppDbContext db, PublicUrls urls, TimeProvider time, CancellationToken ct)
    {
        var challenge = await otp.VerifyAsync(Audience.Customer, request.RequestId, request.Code, ct);
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
        return TypedResults.Ok(new CustomerSession(issued.AccessToken, issued.RefreshToken, issued.ExpiresIn,
            created || customer.FirstName is null, CustomerMe.From(customer, hasBank, urls)));
    }

    private static async Task<Ok<CustomerTokens>> Refresh(RefreshRequest request, TokenService tokens, AppDbContext db,
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
        return TypedResults.Ok(new CustomerTokens(issued.AccessToken, issued.RefreshToken, issued.ExpiresIn));
    }

    private static async Task<NoContent> Logout(RefreshRequest request, TokenService tokens, CancellationToken ct)
    {
        await tokens.RevokeAsync(request.RefreshToken, ct);
        return TypedResults.NoContent();
    }
}
