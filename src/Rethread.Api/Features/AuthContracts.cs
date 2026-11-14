using FluentValidation;

namespace Rethread.Api.Features;

/// <summary>Request shapes the three audiences' auth endpoints share.</summary>
public sealed record OtpVerifyRequest(string ChallengeId, string Code);

public sealed record RefreshRequest(string RefreshToken);

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTime ExpiresAt);

public sealed record OtpChallengeResponse(string ChallengeId, DateTime ExpiresAt, int ResendAfterSeconds);

public static class ContactRules
{
    /// <summary>E.164, such as <c>+94771234567</c>.</summary>
    public const string MobilePattern = @"^\+[1-9]\d{7,14}$";

    public static IRuleBuilderOptions<T, string> Mobile<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Matches(MobilePattern).WithMessage("Use an international number, such as +94771234567.");
}

public sealed class OtpVerifyRequestValidator : AbstractValidator<OtpVerifyRequest>
{
    public OtpVerifyRequestValidator()
    {
        RuleFor(x => x.ChallengeId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Matches(@"^\d{6}$").WithMessage("The code is 6 digits.");
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}
