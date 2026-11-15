using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Notifications;

public sealed record CustomerNotificationDto(string Id, string Type, string Title, string Body, DateTime CreatedAt,
    DateTime? ReadAt, string? TargetId, string? PickupStatus);

public sealed record CustomerNotificationPage(IReadOnlyList<CustomerNotificationDto> Items, int Page, int PageSize, bool HasMore);

public sealed record UnreadCountDto(int UnreadCount);

public sealed record PreferencesDto(bool PickupUpdates, bool Orders, bool Payments, bool Offers);

/// <summary>The user app's inbox. Switched-off categories drop out of the list (and, later, push).</summary>
public static class CustomerNotificationEndpoints
{
    public static void MapCustomerNotifications(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/notifications");
        notifications.MapGet("", List);
        notifications.MapGet("/unread-count", Unread);
        notifications.MapPost("/read-all", ReadAll);
        notifications.MapGet("/preferences", GetPreferences);
        notifications.MapPut("/preferences", SavePreferences);
        notifications.MapPost("/{id}/read", Read);
        notifications.MapDelete("/{id}", Delete);
    }

    /// <summary>The customer's notifications, without the categories they switched off.</summary>
    public static async Task<IQueryable<Notification>> InboxAsync(AppDbContext db, string customerId, CancellationToken ct)
    {
        var prefs = await db.NotificationPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.CustomerId == customerId, ct)
                    ?? new NotificationPreferences { CustomerId = customerId };
        var hidden = new List<string>();
        if (!prefs.Orders) hidden.Add(NotificationService.CustomerTypes.OrderUpdate);
        if (!prefs.Payments) hidden.Add(NotificationService.CustomerTypes.PaymentCredited);
        if (!prefs.Offers) hidden.Add(NotificationService.CustomerTypes.Promotion);
        return db.Notifications.Where(n => n.Audience == Audience.Customer && n.RecipientId == customerId && !hidden.Contains(n.Type));
    }

    private static async Task<Ok<CustomerNotificationPage>> List(int? page, int? pageSize, CurrentUser user, AppDbContext db,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var paging = PageRequest.From(page, pageSize ?? 20);
        var rows = await (await InboxAsync(db, customer.Id, ct)).AsNoTracking()
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(paging.Skip).Take(paging.PageSize + 1).ToListAsync(ct);
        return TypedResults.Ok(new CustomerNotificationPage(rows.Take(paging.PageSize).Select(n => new CustomerNotificationDto(
                n.Id, n.Type, n.Title, n.Body, n.CreatedAt, n.ReadAt, n.TargetId, n.PickupStatus)).ToList(),
            paging.Page, paging.PageSize, rows.Count > paging.PageSize));
    }

    public static async Task<int> UnreadCountAsync(AppDbContext db, string customerId, CancellationToken ct) =>
        await (await InboxAsync(db, customerId, ct)).CountAsync(n => n.ReadAt == null, ct);

    private static async Task<Ok<UnreadCountDto>> Unread(CurrentUser user, AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(new UnreadCountDto(await UnreadCountAsync(db, (await user.CustomerAsync(ct)).Id, ct)));

    private static async Task<NoContent> Read(string id, CurrentUser user, AppDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var notification = await Mine(db, customer.Id).SingleOrDefaultAsync(n => n.Id == id, ct)
                           ?? throw ApiException.NotFound("NOTIFICATION_NOT_FOUND", "Notification not found.");
        notification.ReadAt ??= time.UtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ReadAll(CurrentUser user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var now = time.UtcNow();
        await Mine(db, customer.Id).Where(n => n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> Delete(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        if (await Mine(db, customer.Id).Where(n => n.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw ApiException.NotFound("NOTIFICATION_NOT_FOUND", "Notification not found.");
        return TypedResults.NoContent();
    }

    private static async Task<Ok<PreferencesDto>> GetPreferences(CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var prefs = await db.NotificationPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.CustomerId == customer.Id, ct)
                    ?? new NotificationPreferences { CustomerId = customer.Id };
        return TypedResults.Ok(new PreferencesDto(true, prefs.Orders, prefs.Payments, prefs.Offers));
    }

    /// <summary><c>pickupUpdates</c> is always on: the customer must hear when a collector is on the way.</summary>
    private static async Task<Ok<PreferencesDto>> SavePreferences(PreferencesDto request, CurrentUser user, AppDbContext db,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var prefs = await db.NotificationPreferences.SingleOrDefaultAsync(p => p.CustomerId == customer.Id, ct);
        if (prefs is null)
        {
            prefs = new NotificationPreferences { CustomerId = customer.Id };
            db.NotificationPreferences.Add(prefs);
        }

        prefs.Orders = request.Orders;
        prefs.Payments = request.Payments;
        prefs.Offers = request.Offers;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new PreferencesDto(true, prefs.Orders, prefs.Payments, prefs.Offers));
    }

    private static IQueryable<Notification> Mine(AppDbContext db, string customerId) =>
        db.Notifications.Where(n => n.Audience == Audience.Customer && n.RecipientId == customerId);
}
