using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Auth;

/// <summary>One-time sign-in codes for the apps. Codes are stored hashed and expire after five minutes.</summary>
public sealed class OtpService(
    AppDbContext db,
    IOptions<OtpOptions> options,
    IHostEnvironment environment,
    ISmsSender sms,
    IEmailSender email,
    TimeProvider time)
{
    public sealed record Challenge(string ChallengeId, DateTime ExpiresAt, int ResendAfterSeconds);

    private readonly OtpOptions _otp = options.Value;

    public async Task<Challenge> SendAsync(Audience audience, string channel, string destination, string? subjectId,
        CancellationToken ct)
    {
        var now = time.UtcNow();
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
            Id = Ids.New("otp"),
            Audience = audience,
            Channel = channel,
            Destination = destination,
            SubjectId = subjectId,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_otp.LifetimeMinutes),
            ResendAfter = now.AddSeconds(_otp.ResendAfterSeconds),
            CodeHash = "",
        };
        challenge.CodeHash = Secrets.Hash($"{challenge.Id}:{code}");
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);

        var text = $"{code} is your ReThread code. It expires in {_otp.LifetimeMinutes} minutes.";
        if (channel == "email") await email.SendAsync(destination, "Your ReThread code", text, ct);
        else await sms.SendAsync(destination, text, ct);

        return new Challenge(challenge.Id, challenge.ExpiresAt, _otp.ResendAfterSeconds);
    }

    /// <summary>Checks a code and uses up the challenge. The caller saves the context.</summary>
    public async Task<OtpChallenge> VerifyAsync(Audience audience, string challengeId, string code,
        CancellationToken ct)
    {
        var now = time.UtcNow();
        var challenge = await db.OtpChallenges.SingleOrDefaultAsync(c => c.Id == challengeId, ct);
        if (challenge is null || challenge.Audience != audience || challenge.ConsumedAt is not null)
            throw ApiException.BadRequest(AuthErrors.InvalidOtp, "That code isn't valid. Ask for a new one.");
        if (challenge.ExpiresAt <= now || challenge.Attempts >= _otp.MaxAttempts)
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
