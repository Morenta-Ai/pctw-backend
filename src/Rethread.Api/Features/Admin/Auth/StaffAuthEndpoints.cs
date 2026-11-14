using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Features.Admin.Staff;

namespace Rethread.Api.Features.Admin.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record StaffSession(string AccessToken, string RefreshToken, int ExpiresIn, StaffSummary Staff);

public sealed record SetPasswordRequest(string Token, string Password);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class SetPasswordRequestValidator : AbstractValidator<SetPasswordRequest>
{
    public SetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).MinimumLength(10).WithMessage("Use at least 10 characters.").MaximumLength(128);
    }
}

/// <summary>Email and password sign-in for the portal.</summary>
public static class StaffAuthEndpoints
{
    public static void MapStaffAuth(this RouteGroupBuilder admin)
    {
        var auth = admin.MapGroup("/auth");
        auth.MapPost("/login", Login).Validate<LoginRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        auth.MapPost("/refresh", Refresh).Validate<RefreshRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        // The refresh token is the credential here, so a staff member whose access token has expired can
        // still sign out cleanly.
        auth.MapPost("/logout", Logout).Validate<RefreshRequest>().AllowAnonymous();
        auth.MapPost("/set-password", SetPassword).Validate<SetPasswordRequest>().AllowAnonymous()
            .RequireRateLimiting(AuthSetup.AuthRateLimit);
        admin.MapGet("/me", Me);
    }

    private static async Task<Ok<StaffSession>> Login(LoginRequest request, AppDbContext db,
        IPasswordHasher<Data.Entities.Staff> passwords, LoginThrottle throttle, TokenService tokens, TimeProvider time,
        CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        throttle.EnsureAllowed(email);

        var staff = await db.Staff.SingleOrDefaultAsync(s => s.Email == email, ct);
        var result = staff?.PasswordHash is null
            ? PasswordVerificationResult.Failed
            : passwords.VerifyHashedPassword(staff, staff.PasswordHash, request.Password);
        if (staff is null || result == PasswordVerificationResult.Failed)
        {
            throttle.Failed(email);
            throw ApiException.Unauthorized(AuthErrors.InvalidCredentials, "The email or password is wrong.");
        }

        throttle.Succeeded(email);
        if (staff.Status == AccountStatus.Suspended) throw CurrentUser.Suspended();
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            staff.PasswordHash = passwords.HashPassword(staff, request.Password);

        staff.LastSignInAt = time.UtcNow();
        var issued = tokens.Issue(Audience.Staff, staff.Id, RoleClaim(staff));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new StaffSession(issued.AccessToken, issued.RefreshToken, issued.ExpiresIn,
            StaffSummary.From(staff)));
    }

    private static async Task<Ok<StaffSession>> Refresh(RefreshRequest request, TokenService tokens, AppDbContext db,
        CancellationToken ct)
    {
        var used = await tokens.UseRefreshTokenAsync(Audience.Staff, request.RefreshToken, ct);
        var staff = await db.Staff.SingleOrDefaultAsync(s => s.Id == used.SubjectId, ct);
        if (staff is null || staff.Status == AccountStatus.Suspended)
        {
            await db.SaveChangesAsync(ct);
            throw staff is null
                ? ApiException.Unauthorized(AuthErrors.SessionExpired, "Sign in again.")
                : CurrentUser.Suspended();
        }

        var issued = tokens.Issue(Audience.Staff, staff.Id, RoleClaim(staff));
        await tokens.LinkAsync(used, issued.RefreshToken, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new StaffSession(issued.AccessToken, issued.RefreshToken, issued.ExpiresIn,
            StaffSummary.From(staff)));
    }

    private static async Task<NoContent> Logout(RefreshRequest request, TokenService tokens, CancellationToken ct)
    {
        await tokens.RevokeAsync(request.RefreshToken, ct);
        return TypedResults.NoContent();
    }

    /// <summary>Finishes an invite: the link in the invite email carries the token.</summary>
    private static async Task<NoContent> SetPassword(SetPasswordRequest request, AppDbContext db,
        IPasswordHasher<Data.Entities.Staff> passwords, TimeProvider time, CancellationToken ct)
    {
        var hash = Secrets.Hash(request.Token);
        var staff = await db.Staff.SingleOrDefaultAsync(s => s.InviteTokenHash == hash, ct);
        if (staff is null || staff.InviteExpiresAt <= time.UtcNow())
            throw ApiException.BadRequest(AuthErrors.InvalidInvite, "This invite link has expired. Ask an admin for a new one.");

        staff.PasswordHash = passwords.HashPassword(staff, request.Password);
        staff.InviteTokenHash = null;
        staff.InviteExpiresAt = null;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<StaffSummary>> Me(CurrentUser user, CancellationToken ct) =>
        TypedResults.Ok(StaffSummary.From(await user.StaffAsync(ct)));

    private static string RoleClaim(Data.Entities.Staff staff) =>
        staff.Role == StaffRole.Admin ? Roles.Admin : Roles.CallCentre;
}
