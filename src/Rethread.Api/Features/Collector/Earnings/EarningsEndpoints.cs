using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Collector.Earnings;

public enum EarningsPeriod { Today, Week }

public enum EarningStatus { Collected, AtHub }

public sealed record EarningsDto(
    EarningsPeriod Period,
    long TotalCents,
    long RatePerKgCents,
    int JobCount,
    decimal CollectedKg,
    IReadOnlyList<EarningsDto.Job> Jobs)
{
    public sealed record Job(string JobId, string Area, DateTime CompletedAt, decimal WeightKg, EarningStatus Status);
}

/// <summary>What the collector has earned, by the day they weighed (Colombo time; weeks start on Monday).</summary>
public static class EarningsEndpoints
{
    public static void MapEarnings(this RouteGroupBuilder api) => api.MapGet("/earnings", Get);

    private static async Task<Ok<EarningsDto>> Get(string? period, CurrentUser user, AppDbContext db, RateBook rates,
        TimeProvider time, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        var which = period is null ? EarningsPeriod.Today
            : CamelCaseEnumConverter<EarningsPeriod>.TryParse(period)
              ?? throw ApiException.Validation("period", "Use today or week.");
        var today = time.Today();
        var from = Colombo.StartOfDay(which == EarningsPeriod.Today ? today : Colombo.StartOfWeek(today));

        var jobs = await db.Pickups
            .Where(p => p.CollectorId == collector.Id && p.CollectedAt >= from &&
                        (p.Status == PickupStatus.Collected || p.Status == PickupStatus.DeliveredToHub))
            .OrderByDescending(p => p.CollectedAt).ToListAsync(ct);

        return TypedResults.Ok(new EarningsDto(
            which,
            jobs.Sum(j => j.CollectorPayoutCents ?? 0),
            (await rates.CurrentAsync(ct)).CollectorRatePerKgCents,
            jobs.Count,
            jobs.Sum(j => j.ActualWeightKg ?? 0),
            jobs.Select(j => new EarningsDto.Job(j.Id, j.Area, j.CollectedAt!.Value, j.ActualWeightKg ?? 0,
                j.Status == PickupStatus.DeliveredToHub ? EarningStatus.AtHub : EarningStatus.Collected)).ToList()));
    }
}
