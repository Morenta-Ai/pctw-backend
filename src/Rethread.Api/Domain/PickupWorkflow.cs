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
    TimeProvider time)
{
    public const string NoCollectorsAvailable = "NO_COLLECTORS_AVAILABLE";
    public const string PickupNotCancellable = "PICKUP_NOT_CANCELLABLE";
    public const string PickupNotFound = "PICKUP_NOT_FOUND";

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

    /// <summary>Closes any open call-centre offer for a pickup (it was taken, or cancelled).</summary>
    public async Task WithdrawOpenOffersAsync(string pickupId, CancellationToken ct)
    {
        var now = time.UtcNow();
        var open = await db.Assignments.Where(a => a.PickupId == pickupId && a.Status == AssignmentStatus.Offered)
            .ToListAsync(ct);
        foreach (var offer in open)
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
