using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Pickups;

/// <summary>The user app's status names. It still reads the older <c>pending</c> and <c>assigned</c>.</summary>
public enum CustomerPickupStatus { AwaitingConfirmation, Confirmed, EnRoute, Collected, DeliveredToHub, Cancelled }

/// <summary>The user app's condition set has no <c>fair</c>.</summary>
public enum CustomerCondition { Good, Worn, Damaged }

/// <summary>A pickup as the user app sees it (<c>GET /pickups/{id}</c> in the user contract).</summary>
public sealed record CustomerPickupDto(
    string Id,
    CustomerPickupStatus Status,
    PickupMethod Mode,
    int? PieceCount,
    int? BagCount,
    IReadOnlyList<CustomerPickupDto.Item> Items,
    long RatePerKgCents,
    IReadOnlyList<CustomerPickupDto.PreferredDate> PreferredDates,
    CustomerPickupDto.Confirmed? ConfirmedDate,
    CustomerPickupDto.WeighingInfo? Weighing,
    object? WeightFlag,
    string? AddressId,
    AddressLabel AddressLabel,
    string AddressLine,
    CustomerPickupDto.Point AddressLocation,
    string? Notes,
    CustomerPickupDto.CollectorCard? Collector,
    IReadOnlyList<CustomerPickupDto.Step> StatusHistory,
    string? CancelReason,
    DateTime CreatedAt)
{
    public sealed record Item(string GarmentTypeId, string? FamilyMemberId, string? Size, CustomerCondition Condition,
        int Quantity);

    public sealed record SlotDto(string Id, string Label, string Start, string End)
    {
        public static SlotDto From(Slot slot)
        {
            var info = PickupSlots.Info(slot);
            return new SlotDto(CamelCaseEnumConverter<Slot>.ToText(slot), info.Label, info.Start, info.End);
        }
    }

    public sealed record PreferredDate(int Priority, DateOnly Date, SlotDto Slot);

    public sealed record Confirmed(DateOnly Date, string SlotId, string SlotLabel);

    public sealed record WeighingInfo(decimal WeightKg, string Method, long RatePerKgCents, long EarningsCents,
        DateTime WeighedAt);

    public sealed record Point(double Lat, double Lng);

    public sealed record CollectorCard(string Id, string Name, decimal Rating, string VehicleType,
        string VehiclePlate, string Phone, string? PhotoUrl);

    public sealed record Step(CustomerPickupStatus Status, DateTime OccurredAt);

    public static CustomerPickupStatus StatusFor(PickupStatus status) => status switch
    {
        PickupStatus.Pending => CustomerPickupStatus.AwaitingConfirmation,
        PickupStatus.Assigned => CustomerPickupStatus.Confirmed,
        PickupStatus.EnRoute or PickupStatus.Arrived => CustomerPickupStatus.EnRoute,
        PickupStatus.Collected => CustomerPickupStatus.Collected,
        PickupStatus.DeliveredToHub => CustomerPickupStatus.DeliveredToHub,
        _ => CustomerPickupStatus.Cancelled,
    };

    /// <summary>Confirmed once a collector took it; the date is then the scheduled one.</summary>
    public static Confirmed? ConfirmedFor(Pickup p) =>
        p.Events.Any(e => e.Status == PickupStatus.Assigned)
            ? new Confirmed(p.ScheduledDate, CamelCaseEnumConverter<Slot>.ToText(p.Slot), PickupSlots.Info(p.Slot).Label)
            : null;

    public static IReadOnlyList<PreferredDate> PreferredFor(Pickup p) =>
        p.PreferredDates.Count > 0
            ? p.PreferredDates.OrderBy(d => d.Priority).Select(d => new PreferredDate(d.Priority, d.Date, SlotDto.From(d.Slot))).ToList()
            : [new PreferredDate(1, p.ScheduledDate, SlotDto.From(p.Slot))];

    public static CollectorCard? CardFor(Data.Entities.Collector? c) => c is null ? null
        : new CollectorCard(c.Id, c.FullName, c.Rating, VehicleLabel(c.VehicleType), c.VehiclePlate, c.Mobile, null);

    /// <param name="garmentIdsByName">Seeded items only have a name; this maps it back to a garment type.</param>
    public static CustomerPickupDto From(Pickup p, IReadOnlyDictionary<string, string> garmentIdsByName) => new(
        p.Id,
        StatusFor(p.Status),
        p.Method,
        p.Method == PickupMethod.Bulk ? p.PieceCount ?? (int)Math.Round(p.EstimatedKg / PickupWorkflow.KgPerPiece) : null,
        p.Method == PickupMethod.Bulk ? p.DeclaredBagCount : null,
        p.Method == PickupMethod.Bulk ? [] : p.Items.OrderBy(i => i.SortOrder).Select(i => new Item(
            i.GarmentType ?? garmentIdsByName.GetValueOrDefault(i.Name, "other"), i.MemberId, i.Size,
            i.Condition switch
            {
                ItemCondition.Worn => CustomerCondition.Worn,
                ItemCondition.Damaged => CustomerCondition.Damaged,
                _ => CustomerCondition.Good,
            }, i.Quantity ?? 1)).ToList(),
        p.CustomerRatePerKgCents,
        PreferredFor(p),
        ConfirmedFor(p),
        p.ActualWeightKg is { } kg && p.CollectedAt is { } at
            ? new WeighingInfo(kg, p.WeightSource == WeightSource.Manual ? "manual" : "bluetoothScale",
                p.CustomerRatePerKgCents, p.CustomerPayoutCents ?? 0, at)
            : null,
        null,
        p.AddressId,
        p.AddressLabel,
        p.AddressLine,
        new Point(p.Latitude, p.Longitude),
        p.Note,
        CardFor(p.Collector),
        p.Events.Where(e => e.Status != PickupStatus.Arrived).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Select(e => new Step(StatusFor(e.Status), e.OccurredAt)).ToList(),
        p.Status == PickupStatus.Cancelled ? p.CustomerCancelReason ?? "other" : null,
        p.CreatedAt);

    public static string VehicleLabel(VehicleType type) => type switch
    {
        VehicleType.ThreeWheeler => "Three-wheeler",
        VehicleType.Motorbike => "Motorbike",
        VehicleType.Van => "Van",
        _ => "Lorry",
    };
}
