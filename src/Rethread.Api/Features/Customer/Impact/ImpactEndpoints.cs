using Microsoft.AspNetCore.Http.HttpResults;
using Rethread.Api.Auth;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Impact;

public static class ImpactEndpoints
{
    public static void MapImpact(this RouteGroupBuilder api) =>
        api.MapGet("/impact", async Task<Ok<ImpactCalculator.Impact>> (CurrentUser user, ImpactCalculator impact,
            CancellationToken ct) => TypedResults.Ok(await impact.ForCustomerAsync((await user.CustomerAsync(ct)).Id, ct)));
}
