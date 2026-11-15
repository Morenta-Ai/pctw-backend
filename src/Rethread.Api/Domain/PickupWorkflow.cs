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

    public sealed record PreferredDate(int Priority, DateOnly Date, Slot Slot);

    public sealed record Booking(
        Customer Customer,
        Address Address,
        PickupMethod Method,
        int? PieceCount,
        int? BagCount,
        IReadOnlyList<ItemDraft> Items,
        IReadOnlyList<PreferredDate> PreferredDates,
        string? Note,
        bool FallbackToCallCentre);

    /// <summary>Per piece, to estimate a bulk request's weight for collectors and the portal.</summary>
    public const decimal KgPerPiece = 0.3m;

    public static (int StartHour, int EndHour) SlotHours(Slot slot) => slot switch
    {
        Slot.Morning => (8, 12),
        Slot.Afternoon => (12, 16),
        _ => (16, 19),
    };

    /// <summary>
    /// Books a pickup. The customer gives no weight: the estimate (for collectors and the portal) comes from
    /// the piece count or each garment's average weight. It's scheduled on the first choice until a collector
    /// confirms it. If no collector works near the address, it fails with <c>NO_COLLECTORS_AVAILABLE</c>
    /// unless the customer falls back to the call centre.
    /// </summary>
    public async Task<Pickup> BookAsync(Booking booking, CancellationToken ct)
    {
        var now = time.UtcNow();
        var address = booking.Address;
        if (!booking.FallbackToCallCentre && !await AnyCollectorWorksNearAsync(address.Latitude, address.Longitude, ct))
            throw ApiException.Conflict(NoCollectorsAvailable, "No collectors work near you yet.");

        var estimate = Weight.Round(booking.Method == PickupMethod.Itemised
            ? booking.Items.Sum(i => i.Type.EstimatedKg * i.Quantity)
            : booking.PieceCount!.Value * KgPerPiece);
        var itemCount = booking.Method == PickupMethod.Itemised ? booking.Items.Sum(i => i.Quantity) : (int?)null;
        var first = booking.PreferredDates.OrderBy(d => d.Priority).First();
        var (start, end) = SlotHours(first.Slot);
        var rate = await rates.CurrentAsync(ct);
        var id = $"RT-{await db.NextAsync(Sequences.PickupNumber, ct)}";

        var pickup = new Pickup
        {
            Id = id,
            CustomerId = booking.Customer.Id,
            Source = booking.FallbackToCallCentre ? PickupSource.CallCentre : PickupSource.App,
            Method = booking.Method,
            BulkMode = booking.Method == PickupMethod.Bulk ? BulkMode.Pieces : null,
            PieceCount = booking.Method == PickupMethod.Bulk ? booking.PieceCount : null,
            DeclaredBagCount = booking.Method == PickupMethod.Bulk ? booking.BagCount : null,
            EstimatedKg = estimate,
            ItemCount = itemCount,
            BagCount = booking.BagCount ?? (itemCount is { } n
                ? Math.Max(1, (int)Math.Ceiling(n / 10m))
                : Math.Max(1, (int)Math.Ceiling(estimate / 4m))),
            ScheduledDate = first.Date,
            Slot = first.Slot,
            WindowStart = Colombo.At(first.Date, start),
            WindowEnd = Colombo.At(first.Date, end),
            AddressId = address.Id,
            AddressLabel = address.Label,
            AddressLine = address.FullLine,
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
            PreferredDates = booking.PreferredDates
                .Select(d => new PickupPreferredDate { PickupId = id, Priority = d.Priority, Date = d.Date, Slot = d.Slot })
                .ToList(),
        };
        db.Pickups.Add(pickup);

        var (title, body) = NotificationTexts.PickupRequested(pickup);
        await notifications.ToStaffAsync(NotificationService.StaffTypes.PickupRequested, pickup.Id, title, body, ct);
        await db.SaveChangesAsync(ct);
        return pickup;
    }

    /// <summary>Customers can cancel until a collector is on the way; staff until the clothes are collected.</summary>
    public static readonly PickupStatus[] CustomerCancellable = [PickupStatus.Pending, PickupStatus.Assigned];

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
            notifications.PickupStatusToCustomer(pickup, "cancelled", NotificationTexts.PickupCancelled(pickup, reason));
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
        // The claim, its timeline event and withdrawing other offers commit together, or not at all.
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await using var _ = own;
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
        // Taking it confirms its date: the customer hears which day and slot.
        var confirmed = await db.Pickups.AsNoTracking().SingleAsync(p => p.Id == pickupId, ct);
        var (confirmedTitle, confirmedBody) = NotificationTexts.PickupDateConfirmed(confirmed);
        notifications.ToCustomer(confirmed.CustomerId, NotificationService.CustomerTypes.PickupDateConfirmed,
            confirmedTitle, confirmedBody, "pickup", pickupId);
        await WithdrawOpenOffersAsync(pickupId, ct);
        await db.SaveChangesAsync(ct);
        if (own is not null) await own.CommitAsync(ct);
    }

    /// <summary>The collector sets off. The customer is told they're coming.</summary>
    public async Task StartAsync(Pickup pickup, Collector collector, CancellationToken ct)
    {
        RequireStatus(pickup, collector, PickupStatus.Assigned);
        Move(pickup, PickupStatus.EnRoute, collector.FullName);
        int? eta = collector is { Latitude: { } lat, Longitude: { } lng }
            ? Geo.EtaMinutes(Geo.DistanceKm(lat, lng, pickup.Latitude, pickup.Longitude))
            : null;
        notifications.PickupStatusToCustomer(pickup, "enRoute", NotificationTexts.CollectorEnRoute(pickup, collector.FullName, eta));
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

        notifications.PickupStatusToCustomer(pickup, "collected", NotificationTexts.PickupCollected(pickup));
        var (paidTitle, paidBody) = NotificationTexts.PaymentCredited(pickup);
        notifications.ToCustomer(pickup.CustomerId, NotificationService.CustomerTypes.PaymentCredited, paidTitle, paidBody,
            "pickup", pickup.Id);

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
            notifications.PickupStatusToCustomer(job, "deliveredToHub", NotificationTexts.HubArrival(job, hub.Name));
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
        await db.Assignments.Where(a => a.PickupId == pickupId && a.Status == AssignmentStatus.Offered)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AssignmentStatus.Withdrawn)
                .SetProperty(a => a.RespondedAt, now), ct);
    }

    /// <summary>Is any active, online collector within their radius of this point?</summary>
    public async Task<bool> AnyCollectorCoversAsync(double latitude, double longitude, CancellationToken ct)
    {
        var online = await db.Collectors
            .Where(c => c.Online && c.Status == AccountStatus.Active && c.Latitude != null && c.Longitude != null)
            .Select(c => new { c.Latitude, c.Longitude, c.RadiusKm }).ToListAsync(ct);
        return online.Any(c => Geo.DistanceKm(c.Latitude!.Value, c.Longitude!.Value, latitude, longitude) <= c.RadiusKm);
    }

    /// <summary>
    /// Does any active collector work near this point, online or not? Their last position or home suburb,
    /// within twice their radius: bookings are for the coming days, not this minute.
    /// </summary>
    public async Task<bool> AnyCollectorWorksNearAsync(double latitude, double longitude, CancellationToken ct)
    {
        var collectors = await db.Collectors.Where(c => c.Status == AccountStatus.Active)
            .Select(c => new { c.Latitude, c.Longitude, c.Area, c.RadiusKm }).ToListAsync(ct);
        return collectors.Any(c =>
        {
            var home = Areas.Find(c.Area);
            var points = new List<(double, double)>();
            if (c.Latitude is { } lat && c.Longitude is { } lng) points.Add((lat, lng));
            if (home is not null) points.Add((home.Latitude, home.Longitude));
            return points.Any(p => Geo.DistanceKm(p.Item1, p.Item2, latitude, longitude) <= c.RadiusKm * 2);
        });
    }

    private static string CamelCaseEnum(PickupStatus status) => CamelCaseEnumConverter<PickupStatus>.ToText(status);
}
