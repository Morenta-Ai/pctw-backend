using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Customer.Notifications;
using Rethread.Api.Features.Customer.Pickups;

namespace Rethread.Api.Features.Customer.Home;

public sealed record HomeSummary(
    HomeSummary.UserName User,
    HomeSummary.WalletInfo Wallet,
    IReadOnlyList<HomeSummary.ActivePickup> ActivePickups,
    int ActivePickupCount,
    HomeSummary.ImpactTotals Impact,
    HomeSummary.LatestMilestone? Milestone,
    IReadOnlyList<HomeSummary.NewProduct> NewProducts,
    int UnreadNotificationCount)
{
    public sealed record UserName(string? FirstName, string? LastName);

    public sealed record WalletInfo(long AvailableBalanceCents, long PendingBalanceCents, string Currency, long RatePerKgCents);

    public sealed record CollectorInfo(string Name, string VehicleType, string? PhotoUrl);

    public sealed record ActivePickup(string Id, CustomerPickupStatus Status, int? PieceCount, int? ItemCount,
        IReadOnlyList<CustomerPickupDto.PreferredDate> PreferredDates, CustomerPickupDto.Confirmed? ConfirmedDate,
        CollectorInfo? Collector);

    public sealed record ImpactTotals(decimal TotalWeightKg, decimal GhgSavedKg, long GarmentsDonated, long TotalDonationValueCents);

    public sealed record LatestMilestone(string Id, string Title, string Message);

    public sealed record NewProduct(string Id, string Name, string Size, long PriceCents, string? ImageUrl);
}

/// <summary>Everything the Home tab shows, in one call the app can cache whole.</summary>
public static class HomeEndpoints
{
    public static void MapHome(this RouteGroupBuilder api) => api.MapGet("/home/summary", Get);

    private static async Task<Ok<HomeSummary>> Get(CurrentUser user, AppDbContext db, WalletLedger wallet, RateBook rates,
        ImpactCalculator impact, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var totals = await wallet.TotalsAsync(customer.Id, ct);
        var active = await CustomerPickupEndpoints.WithDetails(db.Pickups).AsNoTracking()
            .Where(p => p.CustomerId == customer.Id && (PickupWorkflow.Open.Contains(p.Status) || p.Status == PickupStatus.Collected))
            .OrderBy(p => p.ScheduledDate).ThenBy(p => p.WindowStart).ThenBy(p => p.Id).ToListAsync(ct);
        var summary = await impact.SummaryAsync(customer.Id, "all", ct);
        var milestones = await impact.MilestonesAsync(customer.Id, ct);
        var latest = milestones.EarnedItems.LastOrDefault();
        var products = await db.Products.AsNoTracking().Where(p => p.Published && p.Stock > 0)
            .OrderByDescending(p => p.CreatedAt).Take(6).ToListAsync(ct);

        return TypedResults.Ok(new HomeSummary(
            new HomeSummary.UserName(customer.FirstName, customer.LastName),
            new HomeSummary.WalletInfo(totals.BalanceCents, totals.PendingCents, "LKR", (await rates.CurrentAsync(ct)).CustomerRatePerKgCents),
            active.Take(3).Select(p => new HomeSummary.ActivePickup(p.Id, CustomerPickupDto.StatusFor(p.Status),
                p.Method == PickupMethod.Bulk ? p.PieceCount : null, p.Method == PickupMethod.Itemised ? p.ItemCount : null,
                CustomerPickupDto.PreferredFor(p), CustomerPickupDto.ConfirmedFor(p),
                p.Collector is null ? null : new HomeSummary.CollectorInfo(p.Collector.FullName,
                    CustomerPickupDto.VehicleLabel(p.Collector.VehicleType), null))).ToList(),
            active.Count,
            new HomeSummary.ImpactTotals(summary.TotalKg, summary.Metrics.GhgSavedKg, summary.Metrics.ClothesDonatedPieces,
                summary.Metrics.DonationValueCents),
            latest is null ? null : new HomeSummary.LatestMilestone(latest.Id, $"You've recycled {latest.Title.Replace(" club", "", StringComparison.Ordinal)}!",
                $"Welcome to the {latest.Title}{(customer.FirstName is null ? "" : $", {customer.FirstName}")}."),
            products.Select(p => new HomeSummary.NewProduct(p.Id, p.Name, p.DefaultSize, p.PriceCents, p.ImageUrls.FirstOrDefault())).ToList(),
            await CustomerNotificationEndpoints.UnreadCountAsync(db, customer.Id, ct)));
    }
}
