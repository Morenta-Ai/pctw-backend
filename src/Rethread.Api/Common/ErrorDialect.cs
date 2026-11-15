namespace Rethread.Api.Common;

/// <summary>
/// The three clients' contracts name some errors differently: the user app says <c>VALIDATION_ERROR</c>,
/// <c>NOT_FOUND</c> and <c>OTP_INVALID</c> where the portal says <c>VALIDATION_FAILED</c>,
/// <c>PICKUP_NOT_FOUND</c> and the collector app <c>INVALID_OTP</c>. Domain code throws one set of codes; this
/// translates them on the way out, by which API the request was for.
/// </summary>
public static class ErrorDialect
{
    public const string OtpUnknownRequest = "OTP_UNKNOWN_REQUEST";
    public const string OtpTooManyAttempts = "OTP_TOO_MANY_ATTEMPTS";

    public static (int Status, ErrorBody Body) Translate(HttpContext context, int status, ErrorBody body)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api/v1/collector"))
        {
            return body.Code switch
            {
                OtpUnknownRequest => (status, body with { Code = "INVALID_OTP" }),
                OtpTooManyAttempts => (StatusCodes.Status400BadRequest, body with { Code = "OTP_EXPIRED" }),
                _ => (status, body),
            };
        }

        if (path.StartsWithSegments("/api/v1/admin") || !path.StartsWithSegments("/api/v1")) return (status, body);

        // The user app.
        var code = body.Code switch
        {
            ErrorCodes.ValidationFailed => "VALIDATION_ERROR",
            ErrorCodes.TooManyRequests or "OTP_RATE_LIMITED" => "RATE_LIMITED",
            "INVALID_OTP" => "OTP_INVALID",
            OtpUnknownRequest => "OTP_EXPIRED",
            "PICKUP_NOT_CANCELLABLE" => "CANNOT_CANCEL",
            var c when status == StatusCodes.Status404NotFound && c.EndsWith("NOT_FOUND", StringComparison.Ordinal) => ErrorCodes.NotFound,
            var c => c,
        };
        return (status, body with { Code = code });
    }
}
