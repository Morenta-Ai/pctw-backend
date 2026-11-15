using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// A customer's environmental and social impact: kilograms collected times each factor an admin sets.
/// Nothing is stored, so a changed factor shows everywhere at once. The portal names the factors by what
/// they measure (<c>ghgSaved</c>); the user app by the figure they produce (<c>ghgSavedKg</c>).
/// </summary>
public sealed class ImpactCalculator(AppDbContext db, TimeProvider time)
{
    public sealed record AppMetric(string Key, string Label, string Unit, int Decimals);

    /// <summary>Portal multiplier key → the user app's metric.</summary>
    public static readonly IReadOnlyDictionary<string, AppMetric> AppMetrics = new Dictionary<string, AppMetric>
    {
        ["ghgSaved"] = new("ghgSavedKg", "GHG saved", "kgCo2e", 1),
        ["resourcesSaved"] = new("resourcesSavedKg", "Resources saved for next generations", "kg", 1),
        ["landfillCostSaved"] = new("taxSavedCents", "Tax money saved for local authority", "lkrCents", 0),
        ["forexSaved"] = new("forexSavedUsdCents", "Forex saved for country", "usdCents", 0),
        ["jobsGenerated"] = new("jobsGenerated", "Jobs generated", "jobs", 2),
        ["entrepreneursCreated"] = new("entrepreneursCreated", "Entrepreneurs created", "entrepreneurs", 2),
        ["garmentsDonated"] = new("clothesDonatedPieces", "Free clothes donated to people in need", "pieces", 0),
        ["donationValue"] = new("donationValueCents", "Total donation value", "lkrCents", 0),
    };

    public static readonly int[] MilestonesKg = [10, 25, 50, 75, 100, 150, 200, 300, 500];

    public sealed record Metrics(
        decimal GhgSavedKg,
        decimal ResourcesSavedKg,
        long TaxSavedCents,
        long ForexSavedUsdCents,
        decimal JobsGenerated,
        decimal EntrepreneursCreated,
        long ClothesDonatedPieces,
        long DonationValueCents);

    public sealed record Summary(string Period, decimal TotalKg, int PickupCount, Metrics Metrics, DateTime UpdatedAt);

    public sealed record MonthFigure(string Month, decimal TotalKg, decimal GhgSavedKg);

    public sealed record Factor(string Key, string Label, decimal PerKg, string Unit);

    public sealed record Factors(IReadOnlyList<Factor> Items, DateTime UpdatedAt);

    public sealed record Earned(string Id, string Title, DateTime AchievedAt);

    public sealed record Next(string Id, string Title, int TargetKg, decimal CurrentKg);

    public sealed record Milestones(IReadOnlyList<Earned> EarnedItems, Next? NextMilestone);

    public static bool IsMoney(string unit) => unit is "lkrCents" or "usdCents";

    /// <summary>For the portal's rule checks: money factors are whole cents.</summary>
    public static decimal Apply(decimal kg, ImpactMultiplier multiplier) =>
        Math.Round(kg * multiplier.PerKg, IsMoney(multiplier.Unit) ? 0 : 1, MidpointRounding.AwayFromZero);

    private sealed record Weighed(DateTime CollectedAt, decimal Kg);

    private async Task<List<Weighed>> WeighedAsync(string customerId, CancellationToken ct) =>
        await db.Pickups.AsNoTracking()
            .Where(p => p.CustomerId == customerId && p.CollectedAt != null && p.ActualWeightKg != null)
            .OrderBy(p => p.CollectedAt)
            .Select(p => new Weighed(p.CollectedAt!.Value, p.ActualWeightKg!.Value)).ToListAsync(ct);

    private async Task<Dictionary<string, decimal>> PerKgAsync(CancellationToken ct) =>
        await db.ImpactMultipliers.AsNoTracking().ToDictionaryAsync(m => m.Key, m => m.PerKg, ct);

    private static decimal Figure(decimal kg, Dictionary<string, decimal> perKg, string key) =>
        Math.Round(kg * perKg.GetValueOrDefault(key), AppMetrics[key].Decimals, MidpointRounding.AwayFromZero);

    public static Metrics MetricsFor(decimal kg, Dictionary<string, decimal> perKg) => new(
        Figure(kg, perKg, "ghgSaved"), Figure(kg, perKg, "resourcesSaved"), (long)Figure(kg, perKg, "landfillCostSaved"),
        (long)Figure(kg, perKg, "forexSaved"), Figure(kg, perKg, "jobsGenerated"), Figure(kg, perKg, "entrepreneursCreated"),
        (long)Figure(kg, perKg, "garmentsDonated"), (long)Figure(kg, perKg, "donationValue"));

    /// <summary><c>month</c> and <c>year</c> are the current calendar month and year in Colombo.</summary>
    public async Task<Summary> SummaryAsync(string customerId, string period, CancellationToken ct)
    {
        var today = time.Today();
        DateTime? from = period switch
        {
            "month" => Colombo.StartOfDay(new DateOnly(today.Year, today.Month, 1)),
            "year" => Colombo.StartOfDay(new DateOnly(today.Year, 1, 1)),
            "all" => null,
            _ => throw ApiException.Validation("period", "Use month, year or all."),
        };
        var weighed = (await WeighedAsync(customerId, ct)).Where(w => from is null || w.CollectedAt >= from).ToList();
        var kg = Weight.Round(weighed.Sum(w => w.Kg));
        return new Summary(period, kg, weighed.Count, MetricsFor(kg, await PerKgAsync(ct)), time.UtcNow());
    }

    /// <summary>Exactly <paramref name="months"/> months, oldest first, ending with this one; empty months are zero.</summary>
    public async Task<List<MonthFigure>> HistoryAsync(string customerId, int months, CancellationToken ct)
    {
        var today = time.Today();
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var weighed = await WeighedAsync(customerId, ct);
        var perKg = await PerKgAsync(ct);
        return Enumerable.Range(0, months).Select(i => thisMonth.AddMonths(i - months + 1)).Select(month =>
        {
            var kg = Weight.Round(weighed.Where(w => Colombo.DateOf(w.CollectedAt) is var d && d.Year == month.Year && d.Month == month.Month)
                .Sum(w => w.Kg));
            return new MonthFigure(month.ToString("yyyy-MM", CultureInfo.InvariantCulture), kg, Figure(kg, perKg, "ghgSaved"));
        }).ToList();
    }

    public async Task<Factors> FactorsAsync(CancellationToken ct)
    {
        var multipliers = await db.ImpactMultipliers.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync(ct);
        return new Factors(multipliers.Where(m => AppMetrics.ContainsKey(m.Key)).Select(m =>
        {
            var metric = AppMetrics[m.Key];
            return new Factor(metric.Key, metric.Label, m.PerKg, metric.Unit);
        }).ToList(), multipliers.Max(m => m.UpdatedAt));
    }

    /// <summary>Kilogram clubs: each is earned the moment a weighing takes the total past it.</summary>
    public async Task<Milestones> MilestonesAsync(string customerId, CancellationToken ct)
    {
        var weighed = await WeighedAsync(customerId, ct);
        var earned = new List<Earned>();
        var total = 0m;
        foreach (var w in weighed)
        {
            var before = total;
            total += w.Kg;
            earned.AddRange(MilestonesKg.Where(kg => before < kg && total >= kg)
                .Select(kg => new Earned($"ms_{kg}kg", $"{kg} kg club", w.CollectedAt)));
        }

        var next = MilestonesKg.FirstOrDefault(kg => total < kg);
        return new Milestones(earned, next == 0 ? null : new Next($"ms_{next}kg", $"{next} kg club", next, Weight.Round(total)));
    }
}
