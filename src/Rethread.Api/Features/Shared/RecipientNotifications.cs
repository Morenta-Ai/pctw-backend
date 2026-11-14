using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Shared;

/// <summary>One app notification, as both the user and collector contracts define it.</summary>
public sealed record NotificationDto(
    string Id,
    string Type,
    string Title,
    string Body,
    DateTime CreatedAt,
    DateTime? ReadAt,
    string? TargetType,
    string? TargetId)
{
    public static NotificationDto From(Notification n) =>
        new(n.Id, n.Type, n.Title, n.Body, n.CreatedAt, n.ReadAt, n.TargetType, n.TargetId);
}

public sealed record NotificationPage(IReadOnlyList<NotificationDto> Items, int UnreadCount, string? NextCursor);

/// <summary>The inbox of one customer or collector: newest first, 20 a page.</summary>
public sealed class RecipientNotifications(AppDbContext db, TimeProvider time)
{
    private const int PageSize = 20;

    public async Task<NotificationPage> ListAsync(Audience audience, string recipientId, string? cursor,
        CancellationToken ct)
    {
        var mine = db.Notifications.Where(n => n.Audience == audience && n.RecipientId == recipientId);
        var page = await mine.ToCursorPageAsync(cursor, PageSize, NotificationDto.From, ct);
        var unread = await mine.CountAsync(n => n.ReadAt == null, ct);
        return new NotificationPage(page.Items, unread, page.NextCursor);
    }

    public async Task MarkReadAsync(Audience audience, string recipientId, string id, CancellationToken ct)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(
                               n => n.Id == id && n.Audience == audience && n.RecipientId == recipientId, ct)
                           ?? throw ApiException.NotFound("NOTIFICATION_NOT_FOUND", "Notification not found.");
        notification.ReadAt ??= time.UtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(Audience audience, string recipientId, CancellationToken ct)
    {
        var now = time.UtcNow();
        await db.Notifications
            .Where(n => n.Audience == audience && n.RecipientId == recipientId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }
}
