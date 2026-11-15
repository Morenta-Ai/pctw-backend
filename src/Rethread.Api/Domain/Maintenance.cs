using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// Time-based housekeeping: expiring offers, alerting on pickups that have waited 30 minutes, and taking
/// silent collectors offline. Safe to run any number of times; the API also runs it before reads that
/// depend on it, so it's right even where the background job doesn't run.
/// </summary>
public sealed class Maintenance(
    AppDbContext db,
    AssignmentService assignments,
    NotificationService notifications,
    OfferSimulator simulator,
    OrderWorkflow orders,
    IConfiguration config,
    TimeProvider time)
{
    public static readonly TimeSpan WaitingAlertAfter = TimeSpan.FromMinutes(30);

    public async Task RunAsync(CancellationToken ct)
    {
        await simulator.RunAsync(ct);
        await assignments.ExpireStaleAsync(ct);
        await AlertWaitingPickupsAsync(ct);
        await TakeSilentCollectorsOfflineAsync(ct);
        await orders.ReleaseUnpaidAsync(ct);
    }

    public async Task AlertWaitingPickupsAsync(CancellationToken ct)
    {
        var cutoff = time.UtcNow() - WaitingAlertAfter;
        var waiting = await db.Pickups
            .Where(p => p.Status == PickupStatus.Pending && p.CreatedAt <= cutoff &&
                        !db.Notifications.Any(n => n.Id == "ntf_pickupWaiting_" + p.Id))
            .ToListAsync(ct);
        foreach (var pickup in waiting)
        {
            var (title, body) = NotificationTexts.PickupWaiting(pickup);
            await notifications.ToStaffAsync(NotificationService.StaffTypes.PickupWaiting, pickup.Id, title, body, ct,
                pickup.CreatedAt + WaitingAlertAfter);
        }

        if (waiting.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A collector whose app has stopped reporting is probably out of signal or closed the app; keeping them
    /// "online" would send offers into the void. Off when <c>Maintenance:StaleCollectorMinutes</c> is 0.
    /// </summary>
    private async Task TakeSilentCollectorsOfflineAsync(CancellationToken ct)
    {
        var minutes = config.GetValue("Maintenance:StaleCollectorMinutes", 30);
        if (minutes <= 0) return;
        var cutoff = time.UtcNow().AddMinutes(-minutes);
        await db.Collectors.Where(c => c.Online && (c.LastSeenAt == null || c.LastSeenAt < cutoff))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Online, false), ct);
    }
}

/// <summary>
/// Development only: answers call-centre offers the way the portal's mock collectors do, so the portal can
/// be demoed without a phone. A collector already on a job declines after 6 seconds, COL-0421 never
/// answers, and anyone else accepts after 8. Off unless <c>Simulation:AutoAnswerOffers</c> is true, and
/// never in Production.
/// </summary>
public sealed class OfferSimulator(AppDbContext db, AssignmentService assignments, IConfiguration config,
    IHostEnvironment environment, TimeProvider time)
{
    public bool Enabled => !environment.IsProduction() && config.GetValue<bool>("Simulation:AutoAnswerOffers");

    public async Task RunAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        var now = time.UtcNow();
        var open = await db.Assignments.Include(a => a.Collector)
            .Where(a => a.Status == AssignmentStatus.Offered && a.ExpiresAt > now).ToListAsync(ct);
        foreach (var offer in open)
        {
            var collector = offer.Collector!;
            if (collector.Code == "COL-0421") continue;
            // Busy means on another job; the pickup on offer doesn't count.
            var busy = await db.Pickups.AnyAsync(p => p.CollectorId == collector.Id && p.Id != offer.PickupId &&
                (p.Status == PickupStatus.Assigned || p.Status == PickupStatus.EnRoute || p.Status == PickupStatus.Arrived), ct);
            try
            {
                if (busy && now >= offer.OfferedAt.AddSeconds(6))
                    await assignments.DeclineAsync(offer, "Already on a job", ct, offer.OfferedAt.AddSeconds(6));
                else if (!busy && now >= offer.OfferedAt.AddSeconds(8))
                    await assignments.AcceptAsync(offer, collector, ct);
            }
            catch (ApiException)
            {
                // Someone else answered, withdrew or expired it first.
            }
        }
    }
}

/// <summary>Runs <see cref="Maintenance"/> every few seconds. Off when <c>Maintenance:Enabled</c> is false.</summary>
public sealed class MaintenanceService(IServiceScopeFactory scopes, IConfiguration config,
    ILogger<MaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Maintenance:Enabled", true)) return;
        var interval = TimeSpan.FromSeconds(config.GetValue("Maintenance:IntervalSeconds", 5));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<Maintenance>().RunAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Maintenance run failed; trying again next tick.");
            }
        }
    }
}
