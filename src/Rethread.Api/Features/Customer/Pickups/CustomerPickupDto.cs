using Rethread.Api.Common;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Customer.Pickups;

/// <summary>A pickup as the user app sees it (<c>GET /pickups/{id}</c> in the user contract).</summary>
public sealed record CustomerPickupDto(
    string Id,
    PickupStatus Status,
    PickupMethod Method,
    BulkMode? BulkMode,
    decimal? DeclaredWeightKg,
    int? DeclaredPieces,
    decimal EstimatedWeightKg,
    decimal? ActualWeightKg,
    long RatePerKgCents,
    long? EarningsCents,
    DateOnly ScheduledDate,
    Slot Slot,
    string? AddressId,
    string AddressLine,
    string? Notes,
    IReadOnlyList<CustomerPickupDto.Item> Items,
    CustomerPickupDto.CollectorCard? Collector,
    int? EtaMinutes,
    IReadOnlyList<CustomerPickupDto.Step> Timeline,
    DateTime CreatedAt)
{
    public sealed record Item(string GarmentType, string? MemberId, string? Size, ItemCondition Condition,
        int Quantity);

    public sealed record CollectorCard(string Id, string Name, decimal Rating, string VehicleType,
        string VehiclePlate, string Phone, string? PhotoUrl);

    public sealed record Step(PickupStatus Status, DateTime? OccurredAt, DateTime? ExpectedAt);

    /// <summary>The steps the user app's tracker shows. It folds <c>arrived</c> into <c>enRoute</c>.</summary>
    private static readonly PickupStatus[] Tracker =
    [
        PickupStatus.Pending, PickupStatus.Assigned, PickupStatus.EnRoute, PickupStatus.Collected,
        PickupStatus.DeliveredToHub,
    ];

    /// <param name="garmentIdsByName">Seeded items only have a name; this maps it back to a garment type.</param>
    public static CustomerPickupDto From(Pickup p, IReadOnlyDictionary<string, string> garmentIdsByName)
    {
        var collector = p.Collector;
        int? eta = p.Status is PickupStatus.EnRoute && collector?.Latitude is { } lat && collector.Longitude is { } lng
            ? Geo.EtaMinutes(Geo.DistanceKm(lat, lng, p.Latitude, p.Longitude))
            : null;

        return new CustomerPickupDto(
            p.Id,
            p.Status == PickupStatus.Arrived ? PickupStatus.EnRoute : p.Status,
            p.Method,
            p.BulkMode,
            p.DeclaredWeightKg,
            p.DeclaredPieces,
            p.EstimatedKg,
            p.ActualWeightKg,
            p.CustomerRatePerKgCents,
            p.CustomerPayoutCents,
            p.ScheduledDate,
            p.Slot,
            p.AddressId,
            p.AddressLine,
            p.Note,
            p.Method == PickupMethod.Bulk
                ? []
                : p.Items.OrderBy(i => i.SortOrder).Select(i => new Item(
                    i.GarmentType ?? garmentIdsByName.GetValueOrDefault(i.Name, "other"), i.MemberId, i.Size,
                    i.Condition, i.Quantity ?? 1)).ToList(),
            collector is null ? null : new CollectorCard(collector.Id, collector.FullName, collector.Rating,
                VehicleLabel(collector.VehicleType), collector.VehiclePlate, collector.Mobile, null),
            eta,
            BuildTimeline(p),
            p.CreatedAt);
    }

    private static List<Step> BuildTimeline(Pickup p)
    {
        DateTime? At(params PickupStatus[] statuses) =>
            p.Events.Where(e => statuses.Contains(e.Status)).OrderBy(e => e.OccurredAt).FirstOrDefault()?.OccurredAt;

        var steps = Tracker.Select(status => new Step(status,
            status == PickupStatus.EnRoute ? At(PickupStatus.EnRoute, PickupStatus.Arrived) : At(status),
            null)).ToList();

        if (p.Status == PickupStatus.Cancelled)
        {
            steps = steps.Where(s => s.OccurredAt is not null).ToList();
            steps.Add(new Step(PickupStatus.Cancelled, At(PickupStatus.Cancelled), null));
            return steps;
        }

        // When the clothes should reach the hub: that afternoon, or the next day for evening pickups.
        var hub = steps[^1];
        if (hub.OccurredAt is null)
        {
            var expected = p.Slot == Slot.Evening
                ? Colombo.At(p.ScheduledDate.AddDays(1), 12)
                : Colombo.At(p.ScheduledDate, 16);
            steps[^1] = hub with { ExpectedAt = expected };
        }

        return steps;
    }

    public static string VehicleLabel(VehicleType type) => type switch
    {
        VehicleType.ThreeWheeler => "Three-wheeler",
        VehicleType.Motorbike => "Motorbike",
        VehicleType.Van => "Van",
        _ => "Lorry",
    };
}
