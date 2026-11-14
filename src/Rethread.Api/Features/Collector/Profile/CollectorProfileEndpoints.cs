using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Collector.Jobs;

namespace Rethread.Api.Features.Collector.Profile;

public sealed record UpdateCollectorRequest(Language? Language, bool? ScaleAutoConnect);

public sealed record StatusRequest(bool Online, double? Latitude, double? Longitude);

public sealed record StatusResult(bool Online, int RadiusKm);

public sealed record Dashboard(
    Dashboard.TodayFigures Today,
    LoadSummary Load,
    JobSummary? ActiveJob,
    Dashboard.NearbyJobs? Nearby,
    int UnreadNotifications)
{
    public sealed record TodayFigures(int JobsDone, decimal CollectedKg, long EarnedCents);

    public sealed record NearbyJobs(int JobCount, double? ClosestKm);
}

public static class CollectorProfileEndpoints
{
    public static void MapCollectorProfile(this RouteGroupBuilder api)
    {
        api.MapGet("/me", Get);
        api.MapPatch("/me", Update);
        api.MapPut("/me/status", SetStatus);
        api.MapGet("/me/dashboard", GetDashboard);
    }

    private static async Task<Ok<CollectorMe>> Get(CurrentUser user, RateBook rates, CancellationToken ct) =>
        TypedResults.Ok(CollectorMe.From(await user.CollectorAsync(ct), await rates.CurrentAsync(ct)));

    private static async Task<Ok<CollectorMe>> Update(UpdateCollectorRequest request, CurrentUser user,
        AppDbContext db, RateBook rates, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        if (request.Language is { } language) collector.Language = language;
        if (request.ScaleAutoConnect is { } auto) collector.ScaleAutoConnect = auto;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(CollectorMe.From(collector, await rates.CurrentAsync(ct)));
    }

    /// <summary>
    /// Online/offline, with the latest position. The app sends it again whenever the collector moves more
    /// than 200 m, which also keeps <c>lastSeenAt</c> fresh for the dispatch map.
    /// </summary>
    private static async Task<Ok<StatusResult>> SetStatus(StatusRequest request, CurrentUser user, AppDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        if (request.Latitude is { } lat && request.Longitude is { } lng)
        {
            if (lat is < -90 or > 90 || lng is < -180 or > 180)
                throw ApiException.Validation("latitude", "That isn't a real position.");
            collector.Latitude = lat;
            collector.Longitude = lng;
        }

        collector.Online = request.Online;
        collector.LastSeenAt = time.UtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new StatusResult(collector.Online, collector.RadiusKm));
    }

    private static async Task<Ok<Dashboard>> GetDashboard(CurrentUser user, AppDbContext db, PickupWorkflow workflow,
        TimeProvider time, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        var dayStart = Colombo.StartOfDay(time.Today());
        var today = await db.Pickups
            .Where(p => p.CollectorId == collector.Id && p.CollectedAt >= dayStart)
            .Select(p => new { Kg = p.ActualWeightKg ?? 0, Paid = p.CollectorPayoutCents ?? 0 })
            .ToListAsync(ct);
        var (loadKg, loadJobs) = await workflow.LoadAsync(collector.Id, ct);
        var active = await JobsEndpoints.ActiveJobAsync(db, collector.Id, ct);

        Dashboard.NearbyJobs? nearby = null;
        if (collector.Online)
        {
            var open = await JobsEndpoints.OpenJobsAsync(db, collector, time, collector.RadiusKm, ct);
            nearby = new Dashboard.NearbyJobs(open.Count, open.Count == 0 ? null : open[0].DistanceKm);
        }

        var unread = await db.Notifications.CountAsync(
            n => n.Audience == Audience.Collector && n.RecipientId == collector.Id && n.ReadAt == null, ct);

        return TypedResults.Ok(new Dashboard(
            new Dashboard.TodayFigures(today.Count, today.Sum(t => t.Kg), today.Sum(t => t.Paid)),
            new LoadSummary(loadKg, collector.CapacityKg, loadJobs),
            active is null ? null : JobMapper.Summary(active, collector),
            nearby,
            unread));
    }
}
