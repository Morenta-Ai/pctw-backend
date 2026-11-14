using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// Everything that changes a pickup's status, whoever does it: the customer, the collector or the call
/// centre. Keeping it in one place means a cancel or a collection does the same thing from every app.
/// </summary>
public sealed class PickupWorkflow(AppDbContext db, RateBook rates, NotificationService notifications,
    WalletLedger wallet, TimeProvider time)
{
    public const string NoCollectorsAvailable = "NO_COLLECTORS_AVAILABLE";
    public const string PickupNotCancellable = "PICKUP_NOT_CANCELLABLE";
    public const string PickupNotFound = "PICKUP_NOT_FOUND";
    public const string JobTaken = "JOB_TAKEN";
    public const string JobNotActive = "JOB_NOT_ACTIVE";
    public const string LoadFull = "LOAD_FULL";
    public const string NothingToDeliver = "NOTHING_TO_DELIVER";

    /// <summary>Statuses a pickup is still "open" in: on the way to being collected.</summary>
    public static readonly PickupStatus[] Open =
        [PickupStatus.Pending, PickupStatus.Assigned, PickupStatus.EnRoute, PickupStatus.Arrived];

    public sealed record ItemDraft(GarmentType Type, string? MemberId, string? Size, ItemCondition Condition,
        int Quantity);

    public sealed record Booking(
        Customer Customer,
        Address Address,
        PickupMethod Method,
        BulkMode? BulkMode,
        decimal? DeclaredWeightKg,
        int? DeclaredPieces,
        IReadOnlyList<ItemDraft> Items,
        DateOnly ScheduledDate,
        Slot Slot,
        string? Note,
        bool FallbackToCallCentre);

    /// <summary>Per piece, for bulk bookings counted in pieces.</summary>
    public const decimal KgPerPiece = 0.3m;

    public static (int StartHour, int EndHour) SlotHours(Slot slot) => slot switch
    {
        Slot.Morning => (8, 12),
        Slot.Afternoon => (12, 16),
        _ => (16, 19),
    };

    /// <summary>
    /// Books a pickup for a customer. For today, someone online has to cover the address, or the booking
    /// fails with <c>NO_COLLECTORS_AVAILABLE</c>; retrying with the call-centre fallback books it anyway for
    /// staff to assign by hand.
    /// </summary>
    public async Task<Pickup> BookAsync(Booking booking, CancellationToken ct)
    {
        var now = time.UtcNow();
        var address = booking.Address;
        if (booking.ScheduledDate == time.Today() && !booking.FallbackToCallCentre &&
            !await AnyCollectorCoversAsync(address.Latitude, address.Longitude, ct))
        {
            throw ApiException.Conflict(NoCollectorsAvailable, "No collectors are free near you right now.");
        }

        var estimate = booking.Method == PickupMethod.Itemised
            ? booking.Items.Sum(i => i.Type.EstimatedKg * i.Quantity)
            : booking.BulkMode == BulkMode.Pieces
                ? booking.DeclaredPieces!.Value * KgPerPiece
                : booking.DeclaredWeightKg!.Value;
        estimate = Weight.Round(estimate);
        var itemCount = booking.Method == PickupMethod.Itemised ? booking.Items.Sum(i => i.Quantity) : (int?)null;
        var (start, end) = SlotHours(booking.Slot);
        var rate = await rates.CurrentAsync(ct);
        var id = $"RT-{await db.NextAsync(Sequences.PickupNumber, ct)}";

        var pickup = new Pickup
        {
            Id = id,
            CustomerId = booking.Customer.Id,
            Source = booking.FallbackToCallCentre ? PickupSource.CallCentre : PickupSource.App,
            Method = booking.Method,
            BulkMode = booking.Method == PickupMethod.Bulk ? booking.BulkMode : null,
            DeclaredWeightKg = booking.BulkMode == BulkMode.Weight ? booking.DeclaredWeightKg : null,
            DeclaredPieces = booking.BulkMode == BulkMode.Pieces ? booking.DeclaredPieces : null,
            EstimatedKg = estimate,
            ItemCount = itemCount,
            BagCount = itemCount is { } n ? Math.Max(1, (int)Math.Ceiling(n / 10m)) : Math.Max(1, (int)Math.Ceiling(estimate / 4m)),
            ScheduledDate = booking.ScheduledDate,
            Slot = booking.Slot,
            WindowStart = Colombo.At(booking.ScheduledDate, start),
            WindowEnd = Colombo.At(booking.ScheduledDate, end),
            AddressId = address.Id,
            AddressLine = address.Line,
            Area = address.Area,
            Latitude = address.Latitude,
            Longitude = address.Longitude,
            Note = string.IsNullOrWhiteSpace(booking.Note) ? null : booking.Note.Trim(),
            CustomerRatePerKgCents = rate.CustomerRatePerKgCents,
            CollectorRatePerKgCents = rate.CollectorRatePerKgCents,
            CreatedAt = now,
            Items = booking.Items.Select((item, i) => new PickupItem
            {
                Id = $"itm_{id[3..]}_{i + 1}",
                PickupId = id,
                Name = item.Type.Name,
                GarmentType = item.Type.Id,
                MemberId = item.MemberId,
                Size = item.Size,
                Quantity = item.Quantity,
                Condition = item.Condition,
                SortOrder = i,
            }).ToList(),
            Events = [new PickupEvent { PickupId = id, Status = PickupStatus.Pending, OccurredAt = now }],
        };
        db.Pickups.Add(pickup);

        var (title, body) = NotificationTexts.PickupRequested(pickup);
        await notifications.ToStaffAsync(NotificationService.StaffTypes.PickupRequested, pickup.Id, title, body, ct);
        await db.SaveChangesAsync(ct);
        return pickup;
    }

    /// <summary>
    /// Cancels a pickup that hasn't been collected yet. Open call-centre offers are withdrawn and an assigned
    /// collector is told. <paramref name="actor"/> is the staff member's name, or null for the customer.
    /// </summary>
    public async Task CancelAsync(Pickup pickup, string reason, string? actor, CancellationToken ct)
    {
        if (!Open.Contains(pickup.Status))
        {
            throw ApiException.Conflict(PickupNotCancellable, "This pickup can't be cancelled any more.",
                new { pickupId = pickup.Id, status = CamelCaseEnum(pickup.Status) });
        }

        var now = time.UtcNow();
        pickup.Status = PickupStatus.Cancelled;
        pickup.CancelReason = reason;
        db.PickupEvents.Add(new PickupEvent
        {
            PickupId = pickup.Id,
            Status = PickupStatus.Cancelled,
            OccurredAt = now,
            Actor = actor,
        });
        await WithdrawOpenOffersAsync(pickup.Id, ct);

        if (pickup.CollectorId is { } collectorId)
        {
            var (title, body) = NotificationTexts.JobCancelled(pickup);
            notifications.ToCollector(collectorId, NotificationService.CollectorTypes.JobCancelled, title, body,
                "job", pickup.Id);
        }

        if (actor is not null)
        {
            notifications.ToCustomer(pickup.CustomerId, "pickupCancelled", "Pickup cancelled",
                $"{pickup.Id} was cancelled: {reason}", "pickup", pickup.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A collector takes an open job. Only one can win: the update only applies while the pickup is still
    /// pending, so a second collector gets <c>JOB_TAKEN</c>. Any open call-centre offer for it is withdrawn.
    /// </summary>
    public async Task AcceptAsync(string pickupId, Collector collector, CancellationToken ct)
    {
        var now = time.UtcNow();
        var taken = await db.Pickups
            .Where(p => p.Id == pickupId && p.Status == PickupStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PickupStatus.Assigned)
                .SetProperty(p => p.CollectorId, collector.Id), ct);
        if (taken == 0)
        {
            var current = await db.Pickups.AsNoTracking().Where(p => p.Id == pickupId)
                .Select(p => new { p.Status, p.CollectorId }).SingleOrDefaultAsync(ct);
            if (current is { Status: PickupStatus.Assigned } && current.CollectorId == collector.Id) return;
            if (current?.Status == PickupStatus.Cancelled)
                throw ApiException.Conflict(JobNotActive, "This job was cancelled.", new { jobId = pickupId });
            throw ApiException.Conflict(JobTaken, "Another collector accepted this job.", new { jobId = pickupId });
        }

        db.PickupEvents.Add(new PickupEvent
        {
            PickupId = pickupId,
            Status = PickupStatus.Assigned,
            OccurredAt = now,
            Actor = collector.FullName,
        });
        await WithdrawOpenOffersAsync(pickupId, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The collector sets off. The customer is told they're coming.</summary>
    public async Task StartAsync(Pickup pickup, Collector collector, CancellationToken ct)
    {
        RequireStatus(pickup, collector, PickupStatus.Assigned);
        Move(pickup, PickupStatus.EnRoute, collector.FullName);
        var (title, body) = NotificationTexts.CollectorEnRoute(pickup, collector.FullName);
        notifications.ToCustomer(pickup.CustomerId, NotificationService.CustomerTypes.CollectorEnRoute, title, body,
            "pickup", pickup.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArriveAsync(Pickup pickup, Collector collector, CancellationToken ct)
    {
        RequireStatus(pickup, collector, PickupStatus.EnRoute);
        Move(pickup, PickupStatus.Arrived, collector.FullName);
        await db.SaveChangesAsync(ct);
    }

    public sealed record Collection(decimal WeightKg, WeightSource WeightSource, int BagCount);

    /// <summary>
    /// Weighs and pays: the payout comes from the weight and the rates copied in at booking, never from the
    /// app. The customer's wallet is credited in the same save. It can't be undone.
    /// </summary>
    public async Task CollectAsync(Pickup pickup, Collector collector, Collection collection, CancellationToken ct)
    {
        RequireStatus(pickup, collector, PickupStatus.Arrived, PickupStatus.EnRoute);
        var weight = Weight.Round(collection.WeightKg);
        var (loadKg, _) = await LoadAsync(collector.Id, ct);
        if (loadKg + weight > collector.CapacityKg)
        {
            throw ApiException.Conflict(LoadFull, "This would go over the vehicle's capacity. Drop off at a hub first.",
                new { loadKg, capacityKg = collector.CapacityKg });
        }

        var now = time.UtcNow();
        pickup.ActualWeightKg = weight;
        pickup.WeightSource = collection.WeightSource;
        pickup.BagCount = collection.BagCount;
        pickup.CustomerPayoutCents = Money.Payout(weight, pickup.CustomerRatePerKgCents);
        pickup.CollectorPayoutCents = Money.Payout(weight, pickup.CollectorRatePerKgCents);
        pickup.CollectedAt = now;
        Move(pickup, PickupStatus.Collected, collector.FullName);
        wallet.CreditPickup(pickup);

        foreach (var (type, text) in new[]
                 {
                     (NotificationService.CustomerTypes.WeightUpdated, NotificationTexts.WeightUpdated(pickup)),
                     (NotificationService.CustomerTypes.PaymentCredited, NotificationTexts.PaymentCredited(pickup)),
                 })
        {
            notifications.ToCustomer(pickup.CustomerId, type, text.Title, text.Body,
                type == NotificationService.CustomerTypes.PaymentCredited ? "wallet" : "pickup",
                type == NotificationService.CustomerTypes.PaymentCredited ? null : pickup.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    public sealed record Delivery(HubDelivery Receipt, Hub Hub, int CustomersNotified);

    /// <summary>
    /// Drops collected jobs at a hub and issues the receipt. Every job must still be in the collector's load;
    /// otherwise nothing is delivered (<c>NOTHING_TO_DELIVER</c>), so a receipt never covers half a drop.
    /// </summary>
    public async Task<Delivery> DeliverToHubAsync(Collector collector, Hub hub, IReadOnlyCollection<string> jobIds,
        CancellationToken ct)
    {
        var ids = jobIds.Distinct().ToList();
        var jobs = await db.Pickups
            .Where(p => ids.Contains(p.Id) && p.CollectorId == collector.Id && p.Status == PickupStatus.Collected)
            .ToListAsync(ct);
        if (ids.Count == 0 || jobs.Count != ids.Count)
        {
            throw ApiException.Conflict(NothingToDeliver, "Some of these jobs aren't in your load any more.",
                new { jobIds = ids.Except(jobs.Select(j => j.Id)).ToList() });
        }

        var now = time.UtcNow();
        var receipt = HubReceipts.Number(hub.Code, now, collector.Code);
        var id = receipt;
        for (var n = 2; await db.HubDeliveries.AnyAsync(d => d.Id == id, ct); n++) id = $"{receipt}-{n}";

        var delivery = new HubDelivery
        {
            Id = id,
            HubId = hub.Id,
            CollectorId = collector.Id,
            JobCount = jobs.Count,
            WeightKg = jobs.Sum(j => j.ActualWeightKg ?? 0),
            CreatedAt = now,
        };
        db.HubDeliveries.Add(delivery);

        foreach (var job in jobs)
        {
            job.HubId = hub.Id;
            job.HubDeliveryId = delivery.Id;
            job.DeliveredAt = now;
            Move(job, PickupStatus.DeliveredToHub, collector.FullName);
            var (title, body) = NotificationTexts.HubArrival(job, hub.Name);
            notifications.ToCustomer(job.CustomerId, NotificationService.CustomerTypes.HubArrival, title, body,
                "pickup", job.Id);
        }

        await db.SaveChangesAsync(ct);
        return new Delivery(delivery, hub, jobs.Select(j => j.CustomerId).Distinct().Count());
    }

    /// <summary>What a collector is carrying: collected, not yet delivered to a hub.</summary>
    public async Task<(decimal WeightKg, int JobCount)> LoadAsync(string collectorId, CancellationToken ct)
    {
        var load = await db.Pickups.Where(p => p.CollectorId == collectorId && p.Status == PickupStatus.Collected)
            .Select(p => p.ActualWeightKg ?? 0).ToListAsync(ct);
        return (load.Sum(), load.Count);
    }

    private void Move(Pickup pickup, PickupStatus status, string? actor)
    {
        pickup.Status = status;
        db.PickupEvents.Add(new PickupEvent
        {
            PickupId = pickup.Id,
            Status = status,
            OccurredAt = time.UtcNow(),
            Actor = actor,
        });
    }

    private static void RequireStatus(Pickup pickup, Collector collector, params PickupStatus[] allowed)
    {
        if (pickup.CollectorId != collector.Id)
            throw ApiException.Forbidden(Common.ErrorCodes.Forbidden, "This job belongs to another collector.");
        if (!allowed.Contains(pickup.Status))
        {
            throw ApiException.Conflict(JobNotActive, "This job can't do that right now.",
                new { jobId = pickup.Id, status = CamelCaseEnum(pickup.Status) });
        }
    }

    /// <summary>Closes any open call-centre offer for a pickup (it was taken, or cancelled).</summary>
    public async Task WithdrawOpenOffersAsync(string pickupId, CancellationToken ct)
    {
        var now = time.UtcNow();
        var open = await db.Assignments.Where(a => a.PickupId == pickupId && a.Status == AssignmentStatus.Offered)
            .ToListAsync(ct);
        // An offer being accepted in this same unit of work is already tracked as accepted: leave it alone.
        foreach (var offer in open.Where(o => o.Status == AssignmentStatus.Offered))
        {
            offer.Status = AssignmentStatus.Withdrawn;
            offer.RespondedAt = now;
        }
    }

    /// <summary>Is any active, online collector within their radius of this point?</summary>
    public async Task<bool> AnyCollectorCoversAsync(double latitude, double longitude, CancellationToken ct)
    {
        var online = await db.Collectors
            .Where(c => c.Online && c.Status == AccountStatus.Active && c.Latitude != null && c.Longitude != null)
            .Select(c => new { c.Latitude, c.Longitude, c.RadiusKm }).ToListAsync(ct);
        return online.Any(c => Geo.DistanceKm(c.Latitude!.Value, c.Longitude!.Value, latitude, longitude) <= c.RadiusKm);
    }

    private static string CamelCaseEnum(PickupStatus status) => CamelCaseEnumConverter<PickupStatus>.ToText(status);
}
