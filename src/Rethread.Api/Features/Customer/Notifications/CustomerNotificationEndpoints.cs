using Microsoft.AspNetCore.Http.HttpResults;
using Rethread.Api.Auth;
using Rethread.Api.Data.Entities;
using Rethread.Api.Features.Shared;

namespace Rethread.Api.Features.Customer.Notifications;

public static class CustomerNotificationEndpoints
{
    public static void MapCustomerNotifications(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/notifications");
        notifications.MapGet("", async Task<Ok<NotificationPage>> (string? cursor, CurrentUser user,
            RecipientNotifications inbox, CancellationToken ct) =>
        {
            var customer = await user.CustomerAsync(ct);
            return TypedResults.Ok(await inbox.ListAsync(Audience.Customer, customer.Id, cursor, ct));
        });
        notifications.MapPost("/{id}/read", async Task<NoContent> (string id, CurrentUser user,
            RecipientNotifications inbox, CancellationToken ct) =>
        {
            var customer = await user.CustomerAsync(ct);
            await inbox.MarkReadAsync(Audience.Customer, customer.Id, id, ct);
            return TypedResults.NoContent();
        });
        notifications.MapPost("/read-all", async Task<NoContent> (CurrentUser user, RecipientNotifications inbox,
            CancellationToken ct) =>
        {
            var customer = await user.CustomerAsync(ct);
            await inbox.MarkAllReadAsync(Audience.Customer, customer.Id, ct);
            return TypedResults.NoContent();
        });
    }
}
