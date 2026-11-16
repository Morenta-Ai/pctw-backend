namespace Rethread.Api.Data.Entities;

public sealed class Hub
{
    public required string Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>Local time, <c>HH:mm</c>.</summary>
    public required string ClosesAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

/// <summary>A click &amp; collect point.</summary>
public sealed class Vendor
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string OpeningHours { get; set; } = "";
    public string? Phone { get; set; }
    /// <summary>Taking click &amp; collect orders.</summary>
    public bool Active { get; set; } = true;
}

/// <summary>Reference weights for the itemised pickup flow.</summary>
public sealed class GarmentType
{
    public required string Id { get; set; }
    /// <summary>What the collector and portal show, such as <c>T-shirt</c>.</summary>
    public required string Name { get; set; }
    /// <summary>A key the user app maps to its own icon set.</summary>
    public string Icon { get; set; } = "other";
    /// <summary>The measurement it's sized by: <c>chest</c>, <c>waist</c>, <c>height</c> or <c>none</c>.</summary>
    public string SizeBasis { get; set; } = "none";
    public decimal EstimatedKg { get; set; }
    public int SortOrder { get; set; }
}

public sealed class VoucherBrand
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    /// <summary>Two letters that start the brand's voucher codes, such as <c>LF</c>.</summary>
    public required string CodePrefix { get; set; }
    public List<long> ValuesCents { get; set; } = [];
    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>A set of rates from a day onwards. The latest one that has started is in force.</summary>
public sealed class RateSet
{
    public required string Id { get; set; }
    public long CustomerRatePerKgCents { get; set; }
    public long CollectorRatePerKgCents { get; set; }
    public long HomeDeliveryChargeCents { get; set; }
    public long ClickAndCollectChargeCents { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
}

/// <summary>One impact metric per kg collected, as the user app's impact screen shows them.</summary>
public sealed class ImpactMultiplier
{
    public required string Key { get; set; }
    public required string Unit { get; set; }
    public decimal PerKg { get; set; }
    public int SortOrder { get; set; }
    public DateTime UpdatedAt { get; set; }
    public required string UpdatedBy { get; set; }
}

/// <summary>An uploaded file: catalogue photos, avatars, collection photos and support screenshots.</summary>
public sealed class StoredFile
{
    public required string Id { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>The bytes, in their own table so listing files never loads them.</summary>
    public StoredFileContent? Content { get; set; }
}

public sealed class StoredFileContent
{
    public required string FileId { get; set; }
    public required byte[] Bytes { get; set; }
}
