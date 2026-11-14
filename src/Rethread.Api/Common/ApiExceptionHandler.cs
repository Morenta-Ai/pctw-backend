using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Rethread.Api.Common;

/// <summary>Turns exceptions into the contract's error body.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, body) = exception switch
        {
            ApiException api => (api.Status, new ErrorBody(api.Code, api.Message, api.Details)),
            BadHttpRequestException bad => (bad.StatusCode,
                new ErrorBody(ErrorCodes.BadRequest, "The request body or parameters are malformed.")),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
                new ErrorBody(ErrorCodes.Conflict, "Someone else changed this at the same time. Try again.")),
            _ => (StatusCodes.Status500InternalServerError,
                new ErrorBody(ErrorCodes.InternalError, "Something went wrong on our side.")),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method,
                context.Request.Path);
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(body, ct);
        return true;
    }
}
