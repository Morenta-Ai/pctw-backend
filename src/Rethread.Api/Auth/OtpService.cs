using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Auth;

/// <summary>One-time codes for the apps. Codes are stored hashed and expire after five minutes.</summary>
public sealed class OtpService(
    AppDbContext db,
    IOptions<OtpOptions> options,
    IHostEnvironment environment,
    ISmsSender sms,
    IEmailSender email,
    TimeProvider time)
{
    public const string SignIn = "signIn";
    public const string ContactChange = "contactChange";

    public sealed record Challenge(string ChallengeId, DateTime ExpiresAt, int ExpiresInSeconds, int ResendAfterSeconds);

    private readonly OtpOptions _otp = options.Value;

    /// <param name="channel"><c>mobile</c> or <c>email</c>.</param>
    /// <param name="resendAfterSeconds">How long the app waits before offering a new code.</param>
    public async Task<Challenge> SendAsync(Audience audience, string channel, string destination, string? subjectId,
        CancellationToken ct, string purpose = SignIn, int? resendAfterSeconds = null)
    {
        var now = time.UtcNow();
        var resend = resendAfterSeconds ?? _otp.ResendAfterSeconds;
        var recent = await db.OtpChallenges
            .Where(c => c.Audience == audience && c.Destination == destination && c.CreatedAt > now.AddHours(-1))
            .OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        if (recent.Count > 0 && recent[0].ResendAfter > now)
        {
            throw new ApiException(StatusCodes.Status429TooManyRequests, AuthErrors.OtpRateLimited,
                "Wait before asking for another code.",
                new { retryAfterSeconds = (int)Math.Ceiling((recent[0].ResendAfter - now).TotalSeconds) });
        }

        if (recent.Count >= _otp.MaxPerHour)
            throw ApiException.TooManyRequests(AuthErrors.OtpRateLimited, "Too many codes requested. Try again later.");

        var code = !environment.IsProduction() && !string.IsNullOrEmpty(_otp.FixedCode)
            ? _otp.FixedCode
            : Secrets.SixDigitCode();
        var challenge = new OtpChallenge
        {
            Id = Ids.New(purpose == ContactChange ? "cc" : "otp"),
            Audience = audience,
            Purpose = purpose,
            Channel = channel,
            Destination = destination,
            SubjectId = subjectId,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_otp.LifetimeMinutes),
            ResendAfter = now.AddSeconds(resend),
            CodeHash = "",
        };
        challenge.CodeHash = Secrets.Hash($"{challenge.Id}:{code}");
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);

        var text = $"{code} is your ReThread code. It expires in {_otp.LifetimeMinutes} minutes.";
        if (channel == "email") await email.SendAsync(destination, "Your ReThread code", text, ct);
        else await sms.SendAsync(destination, text, ct);

        return new Challenge(challenge.Id, challenge.ExpiresAt, _otp.LifetimeMinutes * 60, resend);
    }

    /// <summary>
    /// Checks a code and uses up the challenge. The caller saves the context. Five wrong codes end the
    /// challenge (<c>OTP_TOO_MANY_ATTEMPTS</c>).
    /// </summary>
    public async Task<OtpChallenge> VerifyAsync(Audience audience, string challengeId, string code,
        CancellationToken ct, string purpose = SignIn)
    {
        var now = time.UtcNow();
        var challenge = await db.OtpChallenges.SingleOrDefaultAsync(c => c.Id == challengeId, ct);
        if (challenge is null || challenge.Audience != audience || challenge.Purpose != purpose ||
            challenge.ConsumedAt is not null)
        {
            throw ApiException.BadRequest(ErrorDialect.OtpUnknownRequest, "That code has expired. Ask for a new one.");
        }

        if (challenge.Attempts >= _otp.MaxAttempts)
        {
            throw new ApiException(StatusCodes.Status429TooManyRequests, ErrorDialect.OtpTooManyAttempts,
                "Too many wrong codes. Ask for a new one.");
        }

        if (challenge.ExpiresAt <= now)
            throw ApiException.BadRequest(AuthErrors.OtpExpired, "That code has expired. Ask for a new one.");

        if (!Secrets.HashesMatch(challenge.CodeHash, Secrets.Hash($"{challenge.Id}:{code}")))
        {
            challenge.Attempts++;
            await db.SaveChangesAsync(ct);
            throw ApiException.BadRequest(AuthErrors.InvalidOtp, "That code isn't right.",
                new { attemptsLeft = Math.Max(0, _otp.MaxAttempts - challenge.Attempts) });
        }

        challenge.ConsumedAt = now;
        return challenge;
    }
}
