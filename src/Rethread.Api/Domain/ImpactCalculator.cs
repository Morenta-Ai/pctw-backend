using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// A customer's environmental and social impact: kilograms collected times each multiplier. Changing a
/// multiplier changes everyone's figures, because nothing is stored.
/// </summary>
public sealed class ImpactCalculator(AppDbContext db, TimeProvider time)
{
    public const int Months = 6;

    public sealed record Metric(string Key, decimal Value, string Unit, IReadOnlyList<decimal> Series);

    public sealed record MonthWeight(string Month, decimal WeightKg);

    public sealed record Impact(decimal TotalWeightKg, int PickupCount, IReadOnlyList<Metric> Metrics,
        IReadOnlyList<MonthWeight> MonthlyWeights);

    public static bool IsMoney(string unit) => unit is "lkrCents" or "usdCents";

    /// <summary>Money comes out in whole cents; everything else to one decimal.</summary>
    public static decimal Apply(decimal kg, ImpactMultiplier multiplier) =>
        Math.Round(kg * multiplier.PerKg, IsMoney(multiplier.Unit) ? 0 : 1, MidpointRounding.AwayFromZero);

    public async Task<Impact> ForCustomerAsync(string customerId, CancellationToken ct)
    {
        var weighed = await db.Pickups.AsNoTracking()
            .Where(p => p.CustomerId == customerId && p.CollectedAt != null)
            .Select(p => new { p.CollectedAt, Kg = p.ActualWeightKg ?? 0 }).ToListAsync(ct);
        var multipliers = await db.ImpactMultipliers.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync(ct);

        var today = time.Today();
        var months = Enumerable.Range(0, Months).Select(i => new DateOnly(today.Year, today.Month, 1).AddMonths(i - (Months - 1)))
            .ToList();
        var byMonth = months.Select(m => weighed
            .Where(w => Colombo.DateOf(w.CollectedAt!.Value) is var d && d.Year == m.Year && d.Month == m.Month)
            .Sum(w => w.Kg)).ToList();
        var total = weighed.Sum(w => w.Kg);

        return new Impact(
            total,
            weighed.Count,
            multipliers.Select(m => new Metric(m.Key, Apply(total, m), m.Unit,
                byMonth.Select(kg => Apply(kg, m)).ToList())).ToList(),
            months.Select((m, i) => new MonthWeight(m.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), byMonth[i]))
                .ToList());
    }
}
