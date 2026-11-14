using System.Net;

namespace Rethread.Api.Common;

/// <summary>
/// A failure the client can branch on. Rendered as <c>{ code, message, details }</c> with
/// <see cref="Status"/> by <see cref="ApiExceptionHandler"/>.
/// </summary>
public sealed class ApiException(int status, string code, string message, object? details = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public object? Details { get; } = details;

    public static ApiException BadRequest(string code, string message, object? details = null) =>
        new((int)HttpStatusCode.BadRequest, code, message, details);

    public static ApiException Unauthorized(string code, string message) =>
        new((int)HttpStatusCode.Unauthorized, code, message);

    public static ApiException Forbidden(string code, string message) =>
        new((int)HttpStatusCode.Forbidden, code, message);

    public static ApiException NotFound(string code, string message, object? details = null) =>
        new((int)HttpStatusCode.NotFound, code, message, details);

    public static ApiException Conflict(string code, string message, object? details = null) =>
        new((int)HttpStatusCode.Conflict, code, message, details);

    public static ApiException TooManyRequests(string code, string message) =>
        new((int)HttpStatusCode.TooManyRequests, code, message);

    /// <summary>422 with <c>details</c> mapping each field to its message.</summary>
    public static ApiException Validation(IReadOnlyDictionary<string, string> fieldErrors) =>
        new((int)HttpStatusCode.UnprocessableEntity, ErrorCodes.ValidationFailed, "Check the highlighted fields.",
            fieldErrors);

    public static ApiException Validation(string field, string message) =>
        Validation(new Dictionary<string, string> { [field] = message });

    /// <summary>422 with a specific code, for business rules on a valid request.</summary>
    public static ApiException Unprocessable(string code, string message, object? details = null) =>
        new((int)HttpStatusCode.UnprocessableEntity, code, message, details);
}

/// <summary>The JSON body of every non-2xx response.</summary>
public sealed record ErrorBody(string Code, string Message, object? Details = null);
