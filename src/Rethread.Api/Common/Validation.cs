using FluentValidation;

namespace Rethread.Api.Common;

/// <summary>Runs the request's validator and answers 422 <c>VALIDATION_FAILED</c> with field messages.</summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<T>().FirstOrDefault()
            ?? throw ApiException.BadRequest(ErrorCodes.BadRequest, "A request body is required.");

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (!result.IsValid)
        {
            var errors = result.Errors
                .GroupBy(e => FieldName(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.First().ErrorMessage);
            throw ApiException.Validation(errors);
        }

        return await next(context);
    }

    /// <summary><c>Vehicle.Plate</c> → <c>vehicle.plate</c>, matching the JSON field names.</summary>
    internal static string FieldName(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(segment =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
}

public static class ValidationExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>();
}
