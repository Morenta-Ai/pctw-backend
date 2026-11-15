using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Auth;

/// <summary>Issues access tokens (JWT) and rotating refresh tokens for the three audiences.</summary>
public sealed class TokenService(AppDbContext db, IOptions<JwtOptions> options, TimeProvider time)
{
    public sealed record Issued(string AccessToken, string RefreshToken, DateTime ExpiresAt, int ExpiresIn);

    private readonly JwtOptions _jwt = options.Value;

    public static SymmetricSecurityKey SigningKey(JwtOptions jwt) => new(Encoding.UTF8.GetBytes(jwt.SigningKey));

    /// <summary>Creates a token pair. The caller saves the context.</summary>
    public Issued Issue(Audience audience, string subjectId, string role)
    {
        var now = time.UtcNow();
        var minutes = audience == Audience.Staff ? _jwt.StaffAccessTokenMinutes : _jwt.AppAccessTokenMinutes;
        var expires = now.AddMinutes(minutes);
        var access = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, subjectId),
                new Claim("role", role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(SigningKey(_jwt), SecurityAlgorithms.HmacSha256),
        });

        var refresh = Secrets.Token("rt");
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Ids.New("rtk"),
            TokenHash = Secrets.Hash(refresh),
            Audience = audience,
            SubjectId = subjectId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(audience == Audience.Staff ? _jwt.StaffRefreshTokenDays : _jwt.AppRefreshTokenDays),
        });

        return new Issued(access, refresh, UtcDateTimeConverter.ToUtc(expires), minutes * 60);
    }

    /// <summary>
    /// Swaps a refresh token for a new one. A token that was already used means it leaked: every session of
    /// that account is revoked. Throws 401 <c>SESSION_EXPIRED</c> for anything that isn't a live token.
    /// </summary>
    public async Task<RefreshToken> UseRefreshTokenAsync(Audience audience, string refreshToken, CancellationToken ct)
    {
        var now = time.UtcNow();
        var hash = Secrets.Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || stored.Audience != audience) throw SessionExpired();

        if (stored.RevokedAt is not null)
        {
            if (stored.ReplacedById is not null) await RevokeAllAsync(audience, stored.SubjectId, ct);
            throw SessionExpired();
        }

        if (stored.ExpiresAt <= now) throw SessionExpired();
        stored.RevokedAt = now;
        return stored;
    }

    /// <summary>Marks the token used and records its successor, for reuse detection.</summary>
    public async Task LinkAsync(RefreshToken used, string newRefreshToken, CancellationToken ct)
    {
        var hash = Secrets.Hash(newRefreshToken);
        var successor = db.RefreshTokens.Local.SingleOrDefault(t => t.TokenHash == hash)
                        ?? await db.RefreshTokens.SingleAsync(t => t.TokenHash == hash, ct);
        used.ReplacedById = successor.Id;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Secrets.Hash(refreshToken);
        await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, time.UtcNow()), ct);
    }

    /// <summary>Ends every session; for a customer it also drops their push devices.</summary>
    public async Task RevokeAllAsync(Audience audience, string subjectId, CancellationToken ct)
    {
        await db.RefreshTokens.Where(t => t.Audience == audience && t.SubjectId == subjectId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, time.UtcNow()), ct);
        if (audience == Audience.Customer)
            await db.Devices.Where(d => d.CustomerId == subjectId).ExecuteDeleteAsync(ct);
    }

    private static ApiException SessionExpired() =>
        ApiException.Unauthorized(AuthErrors.SessionExpired, "Your session has ended. Sign in again.");
}
