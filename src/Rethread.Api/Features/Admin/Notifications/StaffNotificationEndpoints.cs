using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Admin.Notifications;

public sealed record StaffNotificationDto(string Id, string Type, string Title, string Body, string SubjectId,
    DateTime CreatedAt, bool Read);

public sealed record StaffNotificationPage(IReadOnlyList<StaffNotificationDto> Items, string? NextCursor, int UnreadCount);

/// <summary>Call-centre alerts. Everyone sees the same alerts; read state is per staff member.</summary>
public static class StaffNotificationEndpoints
{
    public static void MapStaffNotifications(this RouteGroupBuilder admin)
    {
        var notifications = admin.MapGroup("/notifications");
        notifications.MapGet("", List);
        notifications.MapPost("/{id}/read", Read);
        notifications.MapPost("/read-all", ReadAll);
    }

    public static IQueryable<Notification> All(AppDbContext db) =>
        db.Notifications.Where(n => n.Audience == Audience.Staff);

    public static IQueryable<Notification> Unread(AppDbContext db, Data.Entities.Staff staff)
    {
        var before = staff.NotificationsReadBefore;
        return All(db).Where(n => (before == null || n.CreatedAt > before) &&
                                  !db.StaffNotificationReads.Any(r => r.StaffId == staff.Id && r.NotificationId == n.Id));
    }

    private static async Task<Ok<StaffNotificationPage>> List(bool? unread, int? limit, string? cursor, CurrentUser user,
        AppDbContext db, Maintenance maintenance, CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        await maintenance.RunAsync(ct);
        var query = unread == true ? Unread(db, staff) : All(db);
        var page = await query.AsNoTracking()
            .ToCursorPageAsync(cursor, Math.Clamp(limit ?? 20, 1, 50), n => n, ct);
        var ids = page.Items.Select(n => n.Id).ToList();
        var read = await db.StaffNotificationReads.Where(r => r.StaffId == staff.Id && ids.Contains(r.NotificationId))
            .Select(r => r.NotificationId).ToListAsync(ct);
        return TypedResults.Ok(new StaffNotificationPage(
            page.Items.Select(n => new StaffNotificationDto(n.Id, n.Type, n.Title, n.Body, n.TargetId ?? "", n.CreatedAt,
                read.Contains(n.Id) || n.CreatedAt <= staff.NotificationsReadBefore)).ToList(),
            page.NextCursor,
            await Unread(db, staff).CountAsync(ct)));
    }

    private static async Task<NoContent> Read(string id, CurrentUser user, AppDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        if (!await All(db).AnyAsync(n => n.Id == id, ct))
            throw ApiException.NotFound("NOTIFICATION_NOT_FOUND", "Notification not found.");
        if (!await db.StaffNotificationReads.AnyAsync(r => r.StaffId == staff.Id && r.NotificationId == id, ct))
        {
            db.StaffNotificationReads.Add(new StaffNotificationRead { StaffId = staff.Id, NotificationId = id, ReadAt = time.UtcNow() });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    /// <summary>Everything up to now counts as read; individual reads before it aren't needed any more.</summary>
    private static async Task<NoContent> ReadAll(CurrentUser user, AppDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        staff.NotificationsReadBefore = time.UtcNow();
        await db.SaveChangesAsync(ct);
        await db.StaffNotificationReads.Where(r => r.StaffId == staff.Id).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }
}
