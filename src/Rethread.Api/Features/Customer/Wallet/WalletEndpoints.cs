using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Wallet;

public sealed record WalletDto(long BalanceCents, long PendingCents, long RatePerKgCents, string Currency);

public sealed record TransactionDto(
    string Id,
    TransactionType Type,
    TransactionStatus Status,
    string Title,
    long AmountCents,
    DateTime OccurredAt,
    string? PickupId,
    decimal? WeightKg,
    long? RatePerKgCents)
{
    public static TransactionDto From(WalletTransaction t) => new(t.Id, t.Type, t.Status, t.Title, t.AmountCents,
        t.OccurredAt, t.PickupId, t.WeightKg, t.RatePerKgCents);
}

public sealed record RedemptionRequest(RedemptionType Type, long AmountCents, string? VoucherBrandId, string? ProjectId);

public sealed record RedemptionResult(string Id, RedemptionType Type, long AmountCents, RedemptionStatus Status,
    long BalanceCents, string? VoucherCode);

public sealed record VoucherBrandDto(string Id, string Name, string Description, IReadOnlyList<long> ValuesCents);

public sealed record DonationProjectSummary(string Id, string Name, string Summary, long RaisedCents, long GoalCents);

public static class WalletEndpoints
{
    public static void MapWallet(this RouteGroupBuilder api)
    {
        var wallet = api.MapGroup("/wallet");
        wallet.MapGet("", Get);
        wallet.MapGet("/transactions", Transactions);
        wallet.MapPost("/redemptions", Redeem);
        wallet.MapGet("/voucher-brands", VoucherBrands);
        wallet.MapGet("/donation-projects", DonationProjects);
    }

    private static async Task<Ok<WalletDto>> Get(CurrentUser user, WalletLedger wallet, RateBook rates,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var totals = await wallet.TotalsAsync(customer.Id, ct);
        return TypedResults.Ok(new WalletDto(totals.BalanceCents, totals.PendingCents,
            (await rates.CurrentAsync(ct)).CustomerRatePerKgCents, "LKR"));
    }

    private static async Task<Ok<CursorPage<TransactionDto>>> Transactions(string? cursor, CurrentUser user,
        AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var page = await db.WalletTransactions.Where(t => t.CustomerId == customer.Id)
            .ToCursorPageAsync(cursor, 20, t => t.OccurredAt, t => t.Id, TransactionDto.From, ct);
        return TypedResults.Ok(page);
    }

    private static async Task<Created<RedemptionResult>> Redeem(RedemptionRequest request, CurrentUser user,
        Redemptions redemptions, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var outcome = await redemptions.RedeemAsync(customer,
            new Redemptions.Request(request.Type, request.AmountCents, request.VoucherBrandId, request.ProjectId), ct);
        var r = outcome.Redemption;
        return TypedResults.Created($"/api/v1/wallet/redemptions/{r.Id}",
            new RedemptionResult(r.Id, r.Type, r.AmountCents, r.Status, outcome.BalanceCents, r.VoucherCode));
    }

    private static async Task<Ok<ItemList<VoucherBrandDto>>> VoucherBrands(AppDbContext db, CancellationToken ct)
    {
        var brands = await db.VoucherBrands.Where(b => b.Active).OrderBy(b => b.SortOrder).ToListAsync(ct);
        return TypedResults.Ok(new ItemList<VoucherBrandDto>(
            brands.Select(b => new VoucherBrandDto(b.Id, b.Name, b.Description, b.ValuesCents)).ToList()));
    }

    /// <summary>Projects taking donations now.</summary>
    private static async Task<Ok<ItemList<DonationProjectSummary>>> DonationProjects(AppDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var today = time.Today();
        var projects = await db.DonationProjects.Where(p => p.Active && (p.EndsOn == null || p.EndsOn >= today))
            .OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        return TypedResults.Ok(new ItemList<DonationProjectSummary>(projects
            .Select(p => new DonationProjectSummary(p.Id, p.Name, p.Summary, p.RaisedCents, p.GoalCents)).ToList()));
    }
}
