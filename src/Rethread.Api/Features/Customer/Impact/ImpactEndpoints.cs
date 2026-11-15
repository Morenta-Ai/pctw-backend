using Microsoft.AspNetCore.Http.HttpResults;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Impact;

public sealed record MilestonesDto(IReadOnlyList<ImpactCalculator.Earned> Earned, ImpactCalculator.Next? Next);

public sealed record FactorsDto(IReadOnlyList<ImpactCalculator.Factor> Factors, DateTime UpdatedAt);

/// <summary>Every figure is worked out here from kilograms collected; the app only formats it.</summary>
public static class ImpactEndpoints
{
    public static void MapImpact(this RouteGroupBuilder api)
    {
        var impact = api.MapGroup("/impact");
        impact.MapGet("/summary", async Task<Ok<ImpactCalculator.Summary>> (string? period, CurrentUser user,
            ImpactCalculator calculator, CancellationToken ct) =>
            TypedResults.Ok(await calculator.SummaryAsync((await user.CustomerAsync(ct)).Id, period ?? "all", ct)));
        impact.MapGet("/history", async Task<Ok<ItemList<ImpactCalculator.MonthFigure>>> (int? months, CurrentUser user,
            ImpactCalculator calculator, CancellationToken ct) =>
            TypedResults.Ok(new ItemList<ImpactCalculator.MonthFigure>(await calculator.HistoryAsync(
                (await user.CustomerAsync(ct)).Id, Math.Clamp(months ?? 12, 1, 24), ct))));
        impact.MapGet("/factors", async Task<Ok<FactorsDto>> (ImpactCalculator calculator, CancellationToken ct) =>
        {
            var factors = await calculator.FactorsAsync(ct);
            return TypedResults.Ok(new FactorsDto(factors.Items, factors.UpdatedAt));
        });
        impact.MapGet("/milestones", async Task<Ok<MilestonesDto>> (CurrentUser user, ImpactCalculator calculator,
            CancellationToken ct) =>
        {
            var milestones = await calculator.MilestonesAsync((await user.CustomerAsync(ct)).Id, ct);
            return TypedResults.Ok(new MilestonesDto(milestones.EarnedItems, milestones.NextMilestone));
        });
    }
}
