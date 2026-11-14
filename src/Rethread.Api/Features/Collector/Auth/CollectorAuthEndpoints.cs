using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Collector.Profile;

namespace Rethread.Api.Features.Collector.Auth;

public sealed record CollectorOtpRequest(string CollectorCode, string Mobile);

public sealed record CollectorSession(string AccessToken, string RefreshToken, DateTime ExpiresAt,
    CollectorMe Collector);

public sealed class CollectorOtpRequestValidator : AbstractValidator<CollectorOtpRequest>
{
    public CollectorOtpRequestValidator()
    {
        RuleFor(x => x.CollectorCode).NotEmpty().Matches(@"^(?i)COL-\d{4}$").WithMessage("Codes look like COL-0457.");
        RuleFor(x => x.Mobile).Mobile();
    }
}

/// <summary>
/// Sign-in for the collector app. Collectors are registered by operations, so the code and mobile must
/// already belong to one account.
/// </summary>
public static class CollectorAuthEndpoints
{
    public static void MapCollectorAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/otp/request", RequestOtp).Validate<CollectorOtpRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        auth.MapPost("/otp/verify", VerifyOtp).Validate<OtpVerifyRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        auth.MapPost("/refresh", Refresh).Validate<RefreshRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        auth.MapPost("/logout", Logout);
    }

    private static async Task<Ok<OtpChallengeResponse>> RequestOtp(CollectorOtpRequest request, OtpService otp,
        AppDbContext db, CancellationToken ct)
    {
        var code = request.CollectorCode.Trim().ToUpperInvariant();
        var collector = await db.Collectors.SingleOrDefaultAsync(
            c => c.Code == code && c.Mobile == request.Mobile.Trim(), ct)
            ?? throw ApiException.NotFound(AuthErrors.CollectorNotFound,
                "That code and mobile number don't belong to the same collector.");
        if (collector.Status == AccountStatus.Suspended) throw CurrentUser.Suspended();

        var challenge = await otp.SendAsync(Audience.Collector, "mobile", collector.Mobile, collector.Id, ct);
        return TypedResults.Ok(new OtpChallengeResponse(challenge.ChallengeId, challenge.ExpiresAt,
            challenge.ResendAfterSeconds));
    }

    private static async Task<Ok<CollectorSession>> VerifyOtp(OtpVerifyRequest request, OtpService otp,
        TokenService tokens, AppDbContext db, RateBook rates, CancellationToken ct)
    {
        var challenge = await otp.VerifyAsync(Audience.Collector, request.ChallengeId, request.Code, ct);
        var collector = await db.Collectors.SingleAsync(c => c.Id == challenge.SubjectId, ct);
        if (collector.Status == AccountStatus.Suspended)
        {
            await db.SaveChangesAsync(ct);
            throw CurrentUser.Suspended();
        }

        var issued = tokens.Issue(Audience.Collector, collector.Id, Roles.Collector);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new CollectorSession(issued.AccessToken, issued.RefreshToken, issued.ExpiresAt,
            CollectorMe.From(collector, await rates.CurrentAsync(ct))));
    }

    private static async Task<Ok<TokenPair>> Refresh(RefreshRequest request, TokenService tokens, AppDbContext db,
        CancellationToken ct)
    {
        var used = await tokens.UseRefreshTokenAsync(Audience.Collector, request.RefreshToken, ct);
        var collector = await db.Collectors.SingleOrDefaultAsync(c => c.Id == used.SubjectId, ct);
        if (collector is null || collector.Status == AccountStatus.Suspended)
        {
            await db.SaveChangesAsync(ct);
            throw collector is null
                ? ApiException.Unauthorized(AuthErrors.SessionExpired, "Sign in again.")
                : CurrentUser.Suspended();
        }

        var issued = tokens.Issue(Audience.Collector, collector.Id, Roles.Collector);
        await tokens.LinkAsync(used, issued.RefreshToken, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TokenPair(issued.AccessToken, issued.RefreshToken, issued.ExpiresAt));
    }

    /// <summary>Signs out everywhere and takes the collector offline, so no more offers reach them.</summary>
    private static async Task<NoContent> Logout(CurrentUser user, TokenService tokens, AppDbContext db,
        CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        collector.Online = false;
        await db.SaveChangesAsync(ct);
        await tokens.RevokeAllAsync(Audience.Collector, collector.Id, ct);
        return TypedResults.NoContent();
    }
}
