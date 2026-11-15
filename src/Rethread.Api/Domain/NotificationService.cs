using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// Writes notifications for all three audiences. It only adds rows to the context; the caller saves them
/// with the change that caused them, so a notification never goes out for something that didn't happen.
/// </summary>
public sealed class NotificationService(AppDbContext db, TimeProvider time)
{
    /// <summary>The user app's types. It shows anything else as <c>system</c>.</summary>
    public static class CustomerTypes
    {
        /// <summary>With <c>pickupStatus</c>: enRoute, collected, deliveredToHub or cancelled.</summary>
        public const string PickupStatus = "pickupStatus";
        public const string PickupDateConfirmed = "pickupDateConfirmed";
        public const string WeightFlagged = "weightFlagged";
        /// <summary>Its target is the pickup that earned the money.</summary>
        public const string PaymentCredited = "paymentCredited";
        public const string OrderUpdate = "orderUpdate";
        public const string Milestone = "milestone";
        public const string Promotion = "promotion";
        public const string System = "system";
    }

    public static class CollectorTypes
    {
        public const string Assignment = "assignment";
        public const string NewJob = "newJob";
        public const string JobCancelled = "jobCancelled";
        public const string HubReminder = "hubReminder";
        public const string Payout = "payout";
    }

    public static class StaffTypes
    {
        public const string PickupRequested = "pickupRequested";
        public const string PickupWaiting = "pickupWaiting";
        public const string OfferDeclined = "offerDeclined";
        public const string OrderPlaced = "orderPlaced";
        public const string TransferRequested = "transferRequested";
    }

    public Notification ToCustomer(string customerId, string type, string title, string body, string? targetType,
        string? targetId, DateTime? at = null, string? pickupStatus = null)
    {
        var notification = Add(Audience.Customer, customerId, Ids.New("ntf"), type, title, body, targetType, targetId, at);
        notification.PickupStatus = pickupStatus;
        return notification;
    }

    /// <summary>A pickup reached a status the customer hears about (enRoute, collected, deliveredToHub, cancelled).</summary>
    public Notification PickupStatusToCustomer(Pickup pickup, string status, (string Title, string Body) text,
        DateTime? at = null) =>
        ToCustomer(pickup.CustomerId, CustomerTypes.PickupStatus, text.Title, text.Body, "pickup", pickup.Id, at, status);

    public Notification ToCollector(string collectorId, string type, string title, string body,
        string? targetType, string? targetId, DateTime? at = null) =>
        Add(Audience.Collector, collectorId, Ids.New("ntf"), type, title, body, targetType, targetId, at);

    /// <summary>
    /// An alert for every staff member. Its ID is <c>ntf_{type}_{subjectId}</c>, so the same event never
    /// alerts twice (the maintenance job can safely retry).
    /// </summary>
    public async Task ToStaffAsync(string type, string subjectId, string title, string body, CancellationToken ct,
        DateTime? at = null)
    {
        var id = StaffId(type, subjectId);
        if (db.Notifications.Local.Any(n => n.Id == id) ||
            await db.Notifications.AnyAsync(n => n.Id == id, ct)) return;
        Add(Audience.Staff, null, id, type, title, body, TargetTypeFor(type), subjectId, at);
    }

    public static string StaffId(string type, string subjectId) => $"ntf_{type}_{subjectId}";

    private static string TargetTypeFor(string staffType) => staffType switch
    {
        StaffTypes.OrderPlaced => "order",
        StaffTypes.TransferRequested => "redemption",
        _ => "pickup",
    };

    private Notification Add(Audience audience, string? recipientId, string id, string type, string title,
        string body, string? targetType, string? targetId, DateTime? at)
    {
        var notification = new Notification
        {
            Id = id,
            Audience = audience,
            RecipientId = recipientId,
            Type = type,
            Title = title,
            Body = body,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAt = at ?? time.UtcNow(),
        };
        db.Notifications.Add(notification);
        return notification;
    }
}
