using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Admin.Notifications;
using Rethread.Api.Features.Admin.Orders;

namespace Rethread.Api.Features.Admin.Dashboard;

public sealed record DashboardDto(
    DateOnly Date,
    DashboardDto.StatusCounts PickupsToday,
    int UnassignedOverdue,
    decimal CollectedTodayKg,
    long CustomerPayoutsTodayCents,
    long CollectorPayoutsTodayCents,
    DashboardDto.CollectorCounts Collectors,
    DashboardDto.OrderCounts Orders,
    IReadOnlyList<DashboardDto.Day> Daily,
    IReadOnlyList<DashboardDto.Activity> RecentActivity)
{
    public sealed record StatusCounts(int Total, int Pending, int Assigned, int EnRoute, int Arrived, int Collected,
        int DeliveredToHub, int Cancelled);

    public sealed record CollectorCounts(int Online, int Active);

    public sealed record OrderCounts(int Open, int PlacedToday);

    public sealed record Day(DateOnly Date, int Pickups, decimal CollectedKg);

    public sealed record Activity(string Id, string Type, string SubjectId, string Summary, DateTime OccurredAt);
}

public sealed record CountsDto(int NewOrders, int WaitingPickups, int PendingTransfers, int UnreadNotifications);

/// <summary>Today in figures, and the sidebar's badges. "Today" is the Colombo calendar day.</summary>
public static class DashboardEndpoints
{
    public static void MapDashboard(this RouteGroupBuilder admin)
    {
        admin.MapGet("/dashboard", Get);
        admin.MapGet("/counts", Counts);
    }

    private static async Task<Ok<DashboardDto>> Get(AppDbContext db, Maintenance maintenance, TimeProvider time,
        CancellationToken ct)
    {
        await maintenance.RunAsync(ct);
        var now = time.UtcNow();
        var today = Colombo.DateOf(now);
        var dayStart = Colombo.StartOfDay(today);
        var firstDay = today.AddDays(-13);

        var todayStatuses = await db.Pickups.Where(p => p.ScheduledDate == today)
            .GroupBy(p => p.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(PickupStatus s) => todayStatuses.SingleOrDefault(x => x.Key == s)?.Count ?? 0;

        var weighed = await db.Pickups.Where(p => p.CollectedAt >= Colombo.StartOfDay(firstDay))
            .Select(p => new
            {
                p.CollectedAt,
                Kg = p.ActualWeightKg ?? 0,
                Customer = p.CustomerPayoutCents ?? 0,
                Collector = p.CollectorPayoutCents ?? 0,
            }).ToListAsync(ct);
        var weighedToday = weighed.Where(w => w.CollectedAt >= dayStart).ToList();
        var scheduled = await db.Pickups.Where(p => p.ScheduledDate >= firstDay && p.ScheduledDate <= today)
            .GroupBy(p => p.ScheduledDate).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var overdueCutoff = now - Maintenance.WaitingAlertAfter;

        var daily = Enumerable.Range(0, 14).Select(i => firstDay.AddDays(i)).Select(day => new DashboardDto.Day(day,
            scheduled.SingleOrDefault(s => s.Key == day)?.Count ?? 0,
            weighed.Where(w => Colombo.DateOf(w.CollectedAt!.Value) == day).Sum(w => w.Kg))).ToList();

        return TypedResults.Ok(new DashboardDto(
            today,
            new DashboardDto.StatusCounts(todayStatuses.Sum(s => s.Count), Count(PickupStatus.Pending),
                Count(PickupStatus.Assigned), Count(PickupStatus.EnRoute), Count(PickupStatus.Arrived),
                Count(PickupStatus.Collected), Count(PickupStatus.DeliveredToHub), Count(PickupStatus.Cancelled)),
            await db.Pickups.CountAsync(p => p.Status == PickupStatus.Pending && p.CreatedAt < overdueCutoff, ct),
            weighedToday.Sum(w => w.Kg),
            weighedToday.Sum(w => w.Customer),
            weighedToday.Sum(w => w.Collector),
            new DashboardDto.CollectorCounts(
                await db.Collectors.CountAsync(c => c.Status == AccountStatus.Active && c.Online, ct),
                await db.Collectors.CountAsync(c => c.Status == AccountStatus.Active, ct)),
            new DashboardDto.OrderCounts(
                await AdminOrderEndpoints.VisibleToStaff(db.Orders).CountAsync(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled, ct),
                await AdminOrderEndpoints.VisibleToStaff(db.Orders).CountAsync(o => o.CreatedAt >= dayStart, ct)),
            daily,
            await RecentActivityAsync(db, now, ct)));
    }

    private static readonly PickupStatus[] ActivityStatuses =
    [
        PickupStatus.Pending, PickupStatus.Assigned, PickupStatus.Collected, PickupStatus.DeliveredToHub,
        PickupStatus.Cancelled,
    ];

    /// <summary>The newest 10 pickup and order events.</summary>
    private static async Task<List<DashboardDto.Activity>> RecentActivityAsync(AppDbContext db, DateTime now,
        CancellationToken ct)
    {
        var events = await db.PickupEvents
            .Where(e => ActivityStatuses.Contains(e.Status) && e.OccurredAt <= now)
            .OrderByDescending(e => e.OccurredAt).Take(10)
            .Join(db.Pickups.Include(p => p.Collector), e => e.PickupId, p => p.Id, (e, p) => new { Event = e, Pickup = p })
            .ToListAsync(ct);
        var orders = await AdminOrderEndpoints.VisibleToStaff(db.Orders).Include(o => o.Customer).Include(o => o.Items)
            .Where(o => o.CreatedAt <= now).OrderByDescending(o => o.CreatedAt).Take(10).AsSplitQuery().ToListAsync(ct);

        return events.Select(x => new DashboardDto.Activity(
                $"act_{x.Pickup.Id}_{CamelCaseEnumConverter<PickupStatus>.ToText(x.Event.Status)}",
                ActivityType(x.Event.Status), x.Pickup.Id, Summary(x.Pickup, x.Event.Status), x.Event.OccurredAt))
            .Concat(orders.Select(o => new DashboardDto.Activity($"act_{o.Id}_placed", "orderPlaced", o.Id,
                $"{o.Customer?.FirstName ?? "Customer"} · {o.Items.Count} item{(o.Items.Count == 1 ? "" : "s")} · " +
                (o.FulfilmentType == FulfilmentType.HomeDelivery ? "home delivery" : "click & collect"),
                o.CreatedAt)))
            .OrderByDescending(a => a.OccurredAt).Take(10).ToList();
    }

    private static string ActivityType(PickupStatus status) => status switch
    {
        PickupStatus.Pending => "pickupRequested",
        PickupStatus.Assigned => "pickupAssigned",
        PickupStatus.Collected => "pickupCollected",
        PickupStatus.DeliveredToHub => "pickupDelivered",
        _ => "pickupCancelled",
    };

    private static string Summary(Pickup p, PickupStatus status)
    {
        var kg = (p.ActualWeightKg ?? 0).ToString("0.00", CultureInfo.InvariantCulture);
        return status switch
        {
            PickupStatus.Pending => $"{p.Area} · about {NotificationTexts.Kg(p.EstimatedKg)} kg · " +
                                    (p.Source == PickupSource.App ? "booked in the app" : "booked by the call centre"),
            PickupStatus.Assigned => $"{p.Area} · {p.Collector?.FirstName ?? "a collector"} accepted",
            PickupStatus.Collected => $"{p.Area} · {kg} kg weighed by {p.Collector?.FirstName ?? "the collector"}",
            PickupStatus.DeliveredToHub => $"{p.Area} · {kg} kg at the hub",
            _ => $"{p.Area} · {p.CancelReason ?? "cancelled"}",
        };
    }

    private static async Task<Ok<CountsDto>> Counts(CurrentUser user, AppDbContext db, Maintenance maintenance,
        CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        await maintenance.RunAsync(ct);
        return TypedResults.Ok(new CountsDto(
            await AdminOrderEndpoints.VisibleToStaff(db.Orders).CountAsync(o => o.Status == OrderStatus.Placed, ct),
            await db.Pickups.CountAsync(p => p.Status == PickupStatus.Pending, ct),
            await db.Redemptions.CountAsync(r => r.Type == RedemptionType.Cash && r.Status == RedemptionStatus.Pending, ct),
            await StaffNotificationEndpoints.Unread(db, staff).CountAsync(ct)));
    }
}
