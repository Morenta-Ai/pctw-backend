using Rethread.Api.Common;

namespace Rethread.Api.Data.Entities;

public sealed class Product : ICursorRow
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public ProductCategory Category { get; set; }
    public long PriceCents { get; set; }
    public List<string> Sizes { get; set; } = [];
    public int Stock { get; set; }
    public bool Published { get; set; }
    public string Story { get; set; } = "";
    public List<string> ImageUrls { get; set; } = [];
    /// <summary><c>likeNew</c>, <c>excellent</c>, <c>good</c> or <c>fair</c>.</summary>
    public string ConditionGrade { get; set; } = "excellent";
    /// <summary><c>women</c>, <c>men</c>, <c>kids</c> or <c>unisex</c>; from the category when not set.</summary>
    public string? Gender { get; set; }
    public string? Material { get; set; }
    public int? ChestCm { get; set; }
    public int? LengthCm { get; set; }
    public int? WaistCm { get; set; }
    public string? RejuvenatedBy { get; set; }
    /// <summary>Textile kept out of landfill by this piece.</summary>
    public decimal? ImpactKg { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }

    /// <summary>The size the user app shows for the piece: the middle of its sizes.</summary>
    public string DefaultSize => Sizes.Count == 0 ? "" : Sizes[Sizes.Count / 2];

    public string GenderOrDefault => Gender ?? Category switch
    {
        ProductCategory.Women => "women",
        ProductCategory.Men => "men",
        ProductCategory.Kids => "kids",
        _ => "unisex",
    };
}

public sealed class Order : ICursorRow
{
    /// <summary><c>ORD-5134</c>, from a sequence.</summary>
    public required string Id { get; set; }
    public required string CustomerId { get; set; }
    public FulfilmentType FulfilmentType { get; set; }
    /// <summary>The delivery address, or the vendor's for click &amp; collect.</summary>
    public required string Destination { get; set; }
    public string? AddressId { get; set; }
    public string? VendorId { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    /// <summary>The card gateway's reference once a payment went through.</summary>
    public string? PaymentReference { get; set; }
    public string? ContactName { get; set; }
    public string? ContactMobile { get; set; }
    public long SubtotalCents { get; set; }
    public long TransportChargeCents { get; set; }
    public long TotalCents { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Placed;
    public string? WaybillNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EstimatedAt { get; set; }
    public uint Version { get; set; }

    public Customer? Customer { get; set; }
    public Vendor? Vendor { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public List<OrderEvent> Events { get; set; } = [];
}

public sealed class OrderItem
{
    public long Id { get; set; }
    public required string OrderId { get; set; }
    public required string ProductId { get; set; }
    public required string Name { get; set; }
    public required string Size { get; set; }
    public int Quantity { get; set; }
    public long UnitPriceCents { get; set; }
}

public sealed class OrderEvent
{
    public long Id { get; set; }
    public required string OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Actor { get; set; }
}
