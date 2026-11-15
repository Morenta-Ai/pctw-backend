namespace Rethread.Api.Data.Entities;

/// <summary>A phone to send a customer's push notifications to, by its FCM token.</summary>
public sealed class Device
{
    public required string Token { get; set; }
    public required string CustomerId { get; set; }
    /// <summary><c>android</c> or <c>ios</c>.</summary>
    public required string Platform { get; set; }
    public string? AppVersion { get; set; }
    public Language Locale { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Which notification categories a customer wants. Pickup updates can't be switched off.</summary>
public sealed class NotificationPreferences
{
    public required string CustomerId { get; set; }
    public bool Orders { get; set; } = true;
    public bool Payments { get; set; } = true;
    public bool Offers { get; set; }
}

public sealed class SupportTicket
{
    public required string Id { get; set; }
    /// <summary><c>TCK-1042</c>: what the customer quotes on the phone.</summary>
    public required string Reference { get; set; }
    public required string CustomerId { get; set; }
    public required string Category { get; set; }
    public string? PickupId { get; set; }
    public required string Message { get; set; }
    public string? ScreenshotFileId { get; set; }
    public DateTime CreatedAt { get; set; }
}
