using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Admin.Rates;

public sealed record RateSetDto(
    string Id,
    long CustomerRatePerKgCents,
    long CollectorRatePerKgCents,
    long HomeDeliveryChargeCents,
    long ClickAndCollectChargeCents,
    DateOnly EffectiveFrom,
    DateTime CreatedAt,
    string CreatedBy)
{
    public static RateSetDto From(RateSet r) => new(r.Id, r.CustomerRatePerKgCents, r.CollectorRatePerKgCents,
        r.HomeDeliveryChargeCents, r.ClickAndCollectChargeCents, r.EffectiveFrom, r.CreatedAt, r.CreatedBy);
}

public sealed record RatesDto(RateSetDto Current, RateSetDto? Scheduled, IReadOnlyList<RateSetDto> History);

public sealed record NewRatesRequest(
    long CustomerRatePerKgCents,
    long CollectorRatePerKgCents,
    long HomeDeliveryChargeCents,
    long ClickAndCollectChargeCents,
    DateOnly EffectiveFrom);

public sealed record MultiplierDto(string Key, string Unit, decimal PerKg);

public sealed record MultipliersDto(IReadOnlyList<MultiplierDto> Items, DateTime UpdatedAt, string UpdatedBy);

public sealed record MultipliersRequest(IReadOnlyList<MultipliersRequest.Change> Items)
{
    public sealed record Change(string Key, decimal PerKg);
}

/// <summary>What customers and collectors earn, the shop's transport charges, and the impact factors (Admin).</summary>
public static class RatesEndpoints
{
    public static void MapRates(this RouteGroupBuilder admin)
    {
        var rates = admin.MapGroup("/rates").AdminOnly();
        rates.MapGet("", Get);
        rates.MapPost("", Set);
        rates.MapDelete("/scheduled", CancelScheduled);

        var multipliers = admin.MapGroup("/impact-multipliers").AdminOnly();
        multipliers.MapGet("", GetMultipliers);
        multipliers.MapPut("", UpdateMultipliers);
    }

    private static async Task<RatesDto> ViewAsync(RateBook book, CancellationToken ct)
    {
        var current = await book.CurrentAsync(ct);
        var scheduled = await book.ScheduledAsync(ct);
        var history = await book.HistoryAsync(current, ct);
        return new RatesDto(RateSetDto.From(current), scheduled is null ? null : RateSetDto.From(scheduled),
            history.Select(RateSetDto.From).ToList());
    }

    private static async Task<Ok<RatesDto>> Get(RateBook book, CancellationToken ct) =>
        TypedResults.Ok(await ViewAsync(book, ct));

    /// <summary>
    /// Today takes effect at once; a later date becomes the scheduled change, replacing any earlier one.
    /// Pickups already booked keep the rates they were booked with.
    /// </summary>
    private static async Task<Created<RatesDto>> Set(NewRatesRequest request, CurrentUser user, AppDbContext db,
        RateBook book, TimeProvider time, CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        var errors = new Dictionary<string, string>();
        void Positive(string field, long value)
        {
            if (value <= 0) errors[field] = "Must be more than zero.";
        }

        Positive("customerRatePerKgCents", request.CustomerRatePerKgCents);
        Positive("collectorRatePerKgCents", request.CollectorRatePerKgCents);
        Positive("homeDeliveryChargeCents", request.HomeDeliveryChargeCents);
        Positive("clickAndCollectChargeCents", request.ClickAndCollectChargeCents);
        var today = time.Today();
        if (request.EffectiveFrom < today) errors["effectiveFrom"] = "Choose today or a later date.";
        if (request.CollectorRatePerKgCents > request.CustomerRatePerKgCents)
            errors["collectorRatePerKgCents"] = "The collector rate can't be more than the customer rate.";
        if (errors.Count > 0) throw ApiException.Validation(errors);

        await db.RateSets.Where(r => r.EffectiveFrom > today).ExecuteDeleteAsync(ct);
        db.RateSets.Add(new RateSet
        {
            Id = Ids.New("rate"),
            CustomerRatePerKgCents = request.CustomerRatePerKgCents,
            CollectorRatePerKgCents = request.CollectorRatePerKgCents,
            HomeDeliveryChargeCents = request.HomeDeliveryChargeCents,
            ClickAndCollectChargeCents = request.ClickAndCollectChargeCents,
            EffectiveFrom = request.EffectiveFrom,
            CreatedAt = time.UtcNow(),
            CreatedBy = staff.Name,
        });
        await db.SaveChangesAsync(ct);
        return TypedResults.Created("/api/v1/admin/rates", await ViewAsync(book, ct));
    }

    private static async Task<Ok<RatesDto>> CancelScheduled(AppDbContext db, RateBook book, TimeProvider time,
        CancellationToken ct)
    {
        var today = time.Today();
        var removed = await db.RateSets.Where(r => r.EffectiveFrom > today).ExecuteDeleteAsync(ct);
        if (removed == 0) throw ApiException.NotFound("NO_SCHEDULED_RATES", "There's no scheduled change.");
        return TypedResults.Ok(await ViewAsync(book, ct));
    }

    private static async Task<MultipliersDto> MultipliersViewAsync(AppDbContext db, CancellationToken ct)
    {
        var items = await db.ImpactMultipliers.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync(ct);
        var latest = items.MaxBy(m => m.UpdatedAt)!;
        return new MultipliersDto(items.Select(m => new MultiplierDto(m.Key, m.Unit, m.PerKg)).ToList(),
            latest.UpdatedAt, latest.UpdatedBy);
    }

    private static async Task<Ok<MultipliersDto>> GetMultipliers(AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await MultipliersViewAsync(db, ct));

    /// <summary>Send any subset. Money factors are whole cents; others allow 4 decimals.</summary>
    private static async Task<Ok<MultipliersDto>> UpdateMultipliers(MultipliersRequest request, CurrentUser user,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        var all = await db.ImpactMultipliers.ToDictionaryAsync(m => m.Key, ct);
        var errors = new Dictionary<string, string>();
        foreach (var (key, perKg) in request.Items ?? [])
        {
            if (!all.TryGetValue(key, out var multiplier)) errors[key] = $"Unknown metric {key}.";
            else if (perKg < 0) errors[key] = "Can't be negative.";
            else if (ImpactCalculator.IsMoney(multiplier.Unit) && perKg != decimal.Truncate(perKg))
                errors[key] = "Money factors must be whole cents.";
            else if (decimal.Round(perKg, 4) != perKg) errors[key] = "Use at most 4 decimals.";
        }

        if (errors.Count > 0) throw ApiException.Validation(errors);
        var now = time.UtcNow();
        foreach (var (key, perKg) in request.Items ?? [])
        {
            all[key].PerKg = perKg;
            all[key].UpdatedAt = now;
            all[key].UpdatedBy = staff.Name;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await MultipliersViewAsync(db, ct));
    }
}
