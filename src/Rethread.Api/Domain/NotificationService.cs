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
    public static class CustomerTypes
    {
        public const string WeightUpdated = "weightUpdated";
        public const string CollectorEnRoute = "collectorEnRoute";
        public const string HubArrival = "hubArrival";
        public const string OrderUpdate = "orderUpdate";
        public const string PaymentCredited = "paymentCredited";
        public const string DonationThanks = "donationThanks";
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
        string? targetId, DateTime? at = null) =>
        Add(Audience.Customer, customerId, Ids.New("ntf"), type, title, body, targetType, targetId, at);

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
