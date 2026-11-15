using Rethread.Api.Common;

namespace Rethread.Api.Data.Entities;

/// <summary>
/// One pickup request: the customer's pickup, the collector's job and the portal's pickup request.
/// The address and the rates are copied in at booking, so later edits and rate changes don't alter it.
/// </summary>
public sealed class Pickup : ICursorRow
{
    /// <summary><c>RT-24817</c>, from a sequence.</summary>
    public required string Id { get; set; }
    public required string CustomerId { get; set; }
    public string? CollectorId { get; set; }
    public PickupStatus Status { get; set; } = PickupStatus.Pending;
    public PickupSource Source { get; set; }
    public PickupMethod Method { get; set; }
    public BulkMode? BulkMode { get; set; }
    /// <summary>The customer's rough count of pieces, for a bulk request from the user app.</summary>
    public int? PieceCount { get; set; }
    /// <summary>Bags as the customer said; <see cref="BagCount"/> is what the collector plans for.</summary>
    public int? DeclaredBagCount { get; set; }
    public decimal? DeclaredWeightKg { get; set; }
    public int? DeclaredPieces { get; set; }
    public decimal EstimatedKg { get; set; }
    public int? ItemCount { get; set; }
    public int BagCount { get; set; }
    public decimal? ActualWeightKg { get; set; }
    public WeightSource? WeightSource { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public Slot Slot { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public string? AddressId { get; set; }
    public AddressLabel AddressLabel { get; set; } = AddressLabel.Home;
    public required string AddressLine { get; set; }
    public required string Area { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Note { get; set; }
    public long CustomerRatePerKgCents { get; set; }
    public long CollectorRatePerKgCents { get; set; }
    public long? CustomerPayoutCents { get; set; }
    public long? CollectorPayoutCents { get; set; }
    public string? HubId { get; set; }
    public string? HubDeliveryId { get; set; }
    public string? CancelReason { get; set; }
    /// <summary>The user app's reason (<c>changedMind</c>, …) when the customer cancelled.</summary>
    public string? CustomerCancelReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CollectedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public uint Version { get; set; }

    public Customer? Customer { get; set; }
    public Collector? Collector { get; set; }
    public Hub? Hub { get; set; }
    public List<PickupItem> Items { get; set; } = [];
    public List<PickupEvent> Events { get; set; } = [];
    public List<PickupPreferredDate> PreferredDates { get; set; } = [];
}

/// <summary>One of the three dates the customer offered, first choice first.</summary>
public sealed class PickupPreferredDate
{
    public required string PickupId { get; set; }
    public int Priority { get; set; }
    public DateOnly Date { get; set; }
    public Slot Slot { get; set; }
}

public sealed class PickupItem
{
    public required string Id { get; set; }
    public required string PickupId { get; set; }
    /// <summary>What the collector and portal show: a garment type's name, or a bag's contents.</summary>
    public required string Name { get; set; }
    public string? GarmentType { get; set; }
    public string? MemberId { get; set; }
    public string? Size { get; set; }
    public int? Quantity { get; set; }
    public string? BagLabel { get; set; }
    public ItemCondition Condition { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>A step in a pickup's timeline.</summary>
public sealed class PickupEvent
{
    public long Id { get; set; }
    public required string PickupId { get; set; }
    public PickupStatus Status { get; set; }
    public DateTime OccurredAt { get; set; }
    /// <summary>The staff member or collector who did it, or null for the customer or the system.</summary>
    public string? Actor { get; set; }
}

public sealed class PickupPhoto
{
    public required string Id { get; set; }
    public required string PickupId { get; set; }
    public required string CollectorId { get; set; }
    public PhotoKind Kind { get; set; }
    public required string FileId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A call-centre offer of one pickup to one collector, open for 45 seconds.</summary>
public sealed class Assignment
{
    public required string Id { get; set; }
    public required string PickupId { get; set; }
    public required string CollectorId { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Offered;
    public string? Note { get; set; }
    public DateTime OfferedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? DeclineReason { get; set; }
    public string? OfferedByStaffId { get; set; }
    public required string OfferedBy { get; set; }
    public uint Version { get; set; }

    public Pickup? Pickup { get; set; }
    public Collector? Collector { get; set; }
}

/// <summary>A collector's drop at a hub. The ID is the receipt: <c>HUB-PLG-071126-0457</c>.</summary>
public sealed class HubDelivery
{
    public required string Id { get; set; }
    public required string HubId { get; set; }
    public required string CollectorId { get; set; }
    public int JobCount { get; set; }
    public decimal WeightKg { get; set; }
    public DateTime CreatedAt { get; set; }
}
