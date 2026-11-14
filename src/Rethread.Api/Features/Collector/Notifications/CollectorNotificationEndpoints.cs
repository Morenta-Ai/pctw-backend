using Microsoft.AspNetCore.Http.HttpResults;
using Rethread.Api.Auth;
using Rethread.Api.Data.Entities;
using Rethread.Api.Features.Shared;

namespace Rethread.Api.Features.Collector.Notifications;

public static class CollectorNotificationEndpoints
{
    public static void MapCollectorNotifications(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/notifications");
        notifications.MapGet("", async Task<Ok<NotificationPage>> (string? cursor, CurrentUser user,
            RecipientNotifications inbox, CancellationToken ct) =>
        {
            var collector = await user.CollectorAsync(ct);
            return TypedResults.Ok(await inbox.ListAsync(Audience.Collector, collector.Id, cursor, ct));
        });
        notifications.MapPost("/{id}/read", async Task<NoContent> (string id, CurrentUser user,
            RecipientNotifications inbox, CancellationToken ct) =>
        {
            var collector = await user.CollectorAsync(ct);
            await inbox.MarkReadAsync(Audience.Collector, collector.Id, id, ct);
            return TypedResults.NoContent();
        });
        notifications.MapPost("/read-all", async Task<NoContent> (CurrentUser user, RecipientNotifications inbox,
            CancellationToken ct) =>
        {
            var collector = await user.CollectorAsync(ct);
            await inbox.MarkAllReadAsync(Audience.Collector, collector.Id, ct);
            return TypedResults.NoContent();
        });
    }
}
