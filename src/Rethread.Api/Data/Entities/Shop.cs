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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }
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
