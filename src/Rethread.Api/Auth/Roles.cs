namespace Rethread.Api.Auth;

/// <summary>The <c>role</c> claim in access tokens.</summary>
public static class Roles
{
    public const string Customer = "customer";
    public const string Collector = "collector";
    public const string Admin = "admin";
    public const string CallCentre = "callCentre";
}

public static class Policies
{
    public const string Customer = "customer";
    public const string Collector = "collector";
    public const string Staff = "staff";
}

public static class AuthErrors
{
    public const string InvalidOtp = "INVALID_OTP";
    public const string OtpExpired = "OTP_EXPIRED";
    public const string OtpRateLimited = "OTP_RATE_LIMITED";
    public const string CollectorNotFound = "COLLECTOR_NOT_FOUND";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountSuspended = "ACCOUNT_SUSPENDED";
    public const string TooManyAttempts = "TOO_MANY_ATTEMPTS";
    public const string SessionExpired = "SESSION_EXPIRED";
    public const string InvalidInvite = "INVALID_INVITE";
}
