namespace Rethread.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "rethread-api";
    public string Audience { get; set; } = "rethread-clients";
    /// <summary>HS256 key, at least 32 characters. Set it from a secret store; never commit a real one.</summary>
    public string SigningKey { get; set; } = "";
    public int AppAccessTokenMinutes { get; set; } = 60;
    public int StaffAccessTokenMinutes { get; set; } = 15;
    public int AppRefreshTokenDays { get; set; } = 60;
    public int StaffRefreshTokenDays { get; set; } = 7;
}

public sealed class OtpOptions
{
    public const string Section = "Otp";

    /// <summary>Development and tests only: every code is this value. Ignored in Production.</summary>
    public string? FixedCode { get; set; }
    public int LifetimeMinutes { get; set; } = 5;
    public int ResendAfterSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 5;
    public int MaxPerHour { get; set; } = 5;
}
