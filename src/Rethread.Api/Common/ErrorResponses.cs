namespace Rethread.Api.Common;

public static class ErrorResponses
{
    /// <summary>
    /// Gives bodiless error statuses (401 from the JWT handler, 404 for an unknown route, and so on) the
    /// same <c>{ code, message }</c> body as every other error.
    /// </summary>
    public static IApplicationBuilder UseContractStatusCodes(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async context =>
        {
            var response = context.HttpContext.Response;
            var body = response.StatusCode switch
            {
                401 => new ErrorBody(ErrorCodes.Unauthorized, "Sign in again."),
                403 => new ErrorBody(ErrorCodes.Forbidden, "You don't have access to this."),
                404 => new ErrorBody(ErrorCodes.NotFound, "Not found."),
                405 => new ErrorBody(ErrorCodes.MethodNotAllowed, "Method not allowed."),
                415 => new ErrorBody("UNSUPPORTED_FILE_TYPE", "Unsupported content type."),
                429 => new ErrorBody(ErrorCodes.TooManyRequests, "Too many requests. Try again shortly."),
                >= 400 and < 500 => new ErrorBody(ErrorCodes.BadRequest, "Bad request."),
                _ => new ErrorBody(ErrorCodes.InternalError, "Something went wrong on our side."),
            };
            var (_, translated) = ErrorDialect.Translate(context.HttpContext, response.StatusCode, body);
            await response.WriteAsJsonAsync(translated);
        });
}
