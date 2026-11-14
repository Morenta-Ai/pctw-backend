namespace Rethread.Api.Common;

/// <summary>Codes shared by every audience. Feature-specific codes live next to their feature.</summary>
public static class ErrorCodes
{
    public const string BadRequest = "BAD_REQUEST";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    public const string Conflict = "CONFLICT";
    public const string InternalError = "INTERNAL_ERROR";
}
