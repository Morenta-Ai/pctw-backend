using Rethread.Api.Common;

namespace Rethread.Api.Data.Entities;

/// <summary>
/// A server-written alert. Customer and collector notifications have one recipient and their own read
/// time. Staff notifications have no recipient: everyone in the portal sees them, and read state is per
/// staff member (<see cref="StaffNotificationRead"/> and <see cref="Staff.NotificationsReadBefore"/>).
/// </summary>
public sealed class Notification : ICursorRow
{
    public required string Id { get; set; }
    public Audience Audience { get; set; }
    public string? RecipientId { get; set; }
    /// <summary>The audience's own type, such as <c>weightUpdated</c>, <c>assignment</c> or <c>pickupWaiting</c>.</summary>
    public required string Type { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public sealed class StaffNotificationRead
{
    public required string NotificationId { get; set; }
    public required string StaffId { get; set; }
    public DateTime ReadAt { get; set; }
}
