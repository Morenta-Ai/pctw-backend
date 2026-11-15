using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Support;

public sealed record Faq(string Id, string Category, string Question, string Answer);

public sealed record TicketCreated(string Id, string Reference, DateTime CreatedAt);

/// <summary>Help for the user app: FAQs and support tickets.</summary>
public static class SupportEndpoints
{
    private const long MaxScreenshotBytes = 3 * 1024 * 1024;
    private static readonly string[] Categories = ["pickup", "pickupWeight", "payment", "order", "app", "account", "other"];

    public static readonly IReadOnlyList<Faq> Faqs =
    [
        new("faq_1", "pickups", "What can I give for recycling?", "Any clothes and household textiles, even torn or stained."),
        new("faq_2", "pickups", "Do I need to weigh my clothes?", "No. Your collector weighs them at your door, and you're paid for that weight."),
        new("faq_3", "pickups", "Why do I choose three dates?", "So we can find a collector who can come. We confirm one and tell you which."),
        new("faq_4", "payments", "When do I get paid?", "As soon as your clothes are weighed, the money is in your ReThread wallet."),
        new("faq_5", "payments", "How is my payment worked out?", "The weight times the rate per kg shown in the app on the day you booked."),
        new("faq_6", "shop", "Is every piece one of a kind?", "Almost always. Once a piece is sold it shows as sold out."),
        new("faq_7", "shop", "How long does delivery take?", "Usually 3 days to your door, or 2 days to a ReThread Point."),
        new("faq_8", "account", "How do I delete my account?", "Profile, then Delete account. Finish any open pickups and use your balance first."),
    ];

    public static void MapSupport(this RouteGroupBuilder api)
    {
        api.MapGet("/support/faqs", () => TypedResults.Ok(new ItemList<Faq>(Faqs)));
        api.MapPost("/support/tickets", CreateTicket).DisableAntiforgery();
    }

    private static async Task<Created<TicketCreated>> CreateTicket([FromForm] string category, [FromForm] string? pickupId,
        [FromForm] string message, IFormFile? screenshot, CurrentUser user, AppDbContext db, IFileStore files,
        TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var errors = new Dictionary<string, string>();
        if (!Categories.Contains(category)) errors["category"] = "Choose what it's about.";
        if (message is null || message.Trim().Length < 10) errors["message"] = "Tell us a little more (at least 10 characters).";
        if (errors.Count > 0) throw ApiException.Validation(errors);

        string? screenshotId = null;
        if (screenshot is { Length: > 0 })
        {
            if (screenshot.Length > MaxScreenshotBytes || await ImageSniffer.DetectAsync(screenshot, ct) != "image/jpeg")
                throw ApiException.Validation("screenshot", "Attach a JPEG screenshot under 3 MB.");
            await using var stream = screenshot.OpenReadStream();
            var stored = await files.SaveAsync("scr", stream, "image/jpeg", customer.Id, ct);
            db.Files.Add(stored);
            screenshotId = stored.Id;
        }

        var number = await db.NextAsync(Sequences.TicketNumber, ct);
        var ticket = new SupportTicket
        {
            Id = $"tck_{number}",
            Reference = $"TCK-{number}",
            CustomerId = customer.Id,
            Category = category,
            PickupId = string.IsNullOrWhiteSpace(pickupId) ? null : pickupId.Trim(),
            Message = message!.Trim(),
            ScreenshotFileId = screenshotId,
            CreatedAt = time.UtcNow(),
        };
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/support/tickets/{ticket.Id}", new TicketCreated(ticket.Id, ticket.Reference, ticket.CreatedAt));
    }
}
