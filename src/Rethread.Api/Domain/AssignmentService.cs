using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// Call-centre offers: staff offer a pending pickup to one collector, who has 45 seconds to accept or
/// decline in the app. Expiry is applied whenever offers are read and by the maintenance job, so it holds
/// even if the background job isn't running.
/// </summary>
public sealed class AssignmentService(AppDbContext db, PickupWorkflow pickups, NotificationService notifications,
    TimeProvider time)
{
    public static readonly TimeSpan OfferLifetime = TimeSpan.FromSeconds(45);

    public const string AssignmentNotFound = "ASSIGNMENT_NOT_FOUND";
    public const string PickupAlreadyAssigned = "PICKUP_ALREADY_ASSIGNED";
    public const string AssignmentPending = "ASSIGNMENT_PENDING";
    public const string CollectorOffline = "COLLECTOR_OFFLINE";
    public const string AssignmentClosed = "ASSIGNMENT_CLOSED";
    public const string AssignmentExpired = "ASSIGNMENT_EXPIRED";

    public static string ReasonLabel(DeclineReason reason) => reason switch
    {
        DeclineReason.VehicleFull => "Vehicle full",
        DeclineReason.TooFar => "Too far",
        DeclineReason.EndingShift => "Ending shift",
        DeclineReason.VehicleProblem => "Vehicle problem",
        _ => "Other reason",
    };

    public async Task<Assignment> OfferAsync(Pickup pickup, string collectorId, string? note, Staff staff,
        CancellationToken ct)
    {
        await ExpireStaleAsync(ct);
        if (pickup.Status != PickupStatus.Pending)
            throw ApiException.Conflict(PickupAlreadyAssigned, "This pickup already has a collector.", new { pickupId = pickup.Id });
        if (await db.Assignments.AnyAsync(a => a.PickupId == pickup.Id && a.Status == AssignmentStatus.Offered, ct))
            throw ApiException.Conflict(AssignmentPending, "An offer for this pickup is still open.", new { pickupId = pickup.Id });

        var collector = await db.Collectors.SingleOrDefaultAsync(c => c.Id == collectorId, ct)
                        ?? throw ApiException.Validation("collectorId", "Choose one of the collectors.");
        if (!collector.Online || collector.Status != AccountStatus.Active)
            throw ApiException.Conflict(CollectorOffline, "That collector is offline.", new { collectorId });
        var (loadKg, _) = await pickups.LoadAsync(collector.Id, ct);
        if (loadKg + pickup.EstimatedKg > collector.CapacityKg)
            throw ApiException.Conflict(PickupWorkflow.LoadFull, "That collector has no room for this pickup.",
                new { collectorId, loadKg, capacityKg = collector.CapacityKg });

        var now = time.UtcNow();
        var offer = new Assignment
        {
            Id = Ids.New("asg"),
            PickupId = pickup.Id,
            CollectorId = collector.Id,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            OfferedAt = now,
            ExpiresAt = now + OfferLifetime,
            OfferedByStaffId = staff.Id,
            OfferedBy = staff.Name,
            Collector = collector,
        };
        db.Assignments.Add(offer);

        var km = Geo.DistanceKm(collector.Latitude ?? pickup.Latitude, collector.Longitude ?? pickup.Longitude,
            pickup.Latitude, pickup.Longitude);
        var (title, body) = NotificationTexts.Assignment(pickup, km);
        notifications.ToCollector(collector.Id, NotificationService.CollectorTypes.Assignment, title, body,
            "assignment", offer.Id);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two staff offered the same pickup at once; the database let one through.
            throw ApiException.Conflict(AssignmentPending, "An offer for this pickup is still open.", new { pickupId = pickup.Id });
        }

        return offer;
    }

    public async Task<Assignment> WithdrawAsync(Assignment offer, CancellationToken ct)
    {
        await ExpireStaleAsync(ct);
        await db.Entry(offer).ReloadAsync(ct);
        if (offer.Status != AssignmentStatus.Offered)
            throw ApiException.Conflict(AssignmentClosed, "This offer was already answered or has expired.");
        offer.Status = AssignmentStatus.Withdrawn;
        offer.RespondedAt = time.UtcNow();
        await db.SaveChangesAsync(ct);
        return offer;
    }

    /// <summary>The collector accepts: the pickup becomes theirs, exactly as if they took it from the board.</summary>
    public async Task AcceptAsync(Assignment offer, Collector collector, CancellationToken ct)
    {
        await RequireOpenAsync(offer, ct);
        offer.Status = AssignmentStatus.Accepted;
        offer.RespondedAt = time.UtcNow();
        try
        {
            await pickups.AcceptAsync(offer.PickupId, collector, ct);
        }
        catch (ApiException e) when (e.Code is PickupWorkflow.JobTaken or PickupWorkflow.JobNotActive)
        {
            db.ChangeTracker.Clear();
            await db.Assignments.Where(a => a.Id == offer.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AssignmentStatus.Withdrawn)
                    .SetProperty(a => a.RespondedAt, time.UtcNow()), ct);
            throw ApiException.Conflict(AssignmentExpired, "This pickup has gone to someone else.");
        }
    }

    public async Task DeclineAsync(Assignment offer, DeclineReason reason, CancellationToken ct)
    {
        await RequireOpenAsync(offer, ct);
        offer.Status = AssignmentStatus.Declined;
        offer.DeclineReason = ReasonLabel(reason);
        offer.RespondedAt = time.UtcNow();
        await AlertStaffAsync(offer, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Marks every offer past its 45 seconds as expired and alerts the call centre.</summary>
    public async Task<int> ExpireStaleAsync(CancellationToken ct)
    {
        var now = time.UtcNow();
        var stale = await db.Assignments.Include(a => a.Collector)
            .Where(a => a.Status == AssignmentStatus.Offered && a.ExpiresAt <= now).ToListAsync(ct);
        foreach (var offer in stale)
        {
            offer.Status = AssignmentStatus.Expired;
            offer.RespondedAt = offer.ExpiresAt;
            await AlertStaffAsync(offer, ct);
        }

        if (stale.Count > 0) await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    private async Task RequireOpenAsync(Assignment offer, CancellationToken ct)
    {
        await ExpireStaleAsync(ct);
        if (offer.Status != AssignmentStatus.Offered)
            throw ApiException.Conflict(AssignmentExpired, "This offer has closed.", new { assignmentId = offer.Id });
    }

    private async Task AlertStaffAsync(Assignment offer, CancellationToken ct)
    {
        var collector = offer.Collector ?? await db.Collectors.SingleAsync(c => c.Id == offer.CollectorId, ct);
        var (title, body) = NotificationTexts.OfferDeclined(offer, collector.FullName);
        await notifications.ToStaffAsync(NotificationService.StaffTypes.OfferDeclined, offer.Id, title, body, ct,
            offer.RespondedAt);
    }
}
