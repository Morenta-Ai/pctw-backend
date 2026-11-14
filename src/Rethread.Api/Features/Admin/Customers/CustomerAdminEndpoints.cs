using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Admin.Customers;

public sealed record CustomerRow(
    string Id,
    string Name,
    string? Mobile,
    string? Email,
    string Area,
    AccountStatus Status,
    DateTime JoinedAt,
    int PickupCount,
    decimal CollectedKg,
    long WalletBalanceCents);

public sealed record CustomerDetail(
    string Id,
    string Name,
    string? Mobile,
    string? Email,
    string Area,
    AccountStatus Status,
    DateTime JoinedAt,
    int PickupCount,
    decimal CollectedKg,
    long WalletBalanceCents,
    string? Address,
    int FamilyMemberCount,
    long EarnedCents,
    IReadOnlyList<CustomerDetail.RecentPickup> RecentPickups)
{
    public sealed record RecentPickup(string Id, PickupStatus Status, DateOnly ScheduledDate, decimal EstimatedKg,
        decimal? ActualWeightKg, long? CustomerPayoutCents);
}

/// <summary>Suspending needs a reason; reactivating doesn't.</summary>
public sealed record AccountStatusRequest(AccountStatus Status, string? Reason);

public sealed class AccountStatusRequestValidator : AbstractValidator<AccountStatusRequest>
{
    public AccountStatusRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300).When(x => x.Status == AccountStatus.Suspended)
            .WithMessage("Say why the account is suspended.");
}

public static class CustomerAdminEndpoints
{
    private const string NotFoundCode = "CUSTOMER_NOT_FOUND";

    public static void MapAdminCustomers(this RouteGroupBuilder admin)
    {
        var customers = admin.MapGroup("/customers");
        customers.MapGet("", List);
        customers.MapGet("/{id}", Get);
        customers.MapPost("/{id}/status", SetStatus).Validate<AccountStatusRequest>();
    }

    private sealed record Stats(int PickupCount, decimal CollectedKg, long EarnedCents, long BalanceCents);

    private static async Task<Ok<PagedList<CustomerRow>>> List(string? q, string? status, int? page, int? pageSize,
        AppDbContext db, CancellationToken ct)
    {
        var query = db.Customers.Include(c => c.Addresses).AsNoTracking().AsSplitQuery();
        if (status is not null)
        {
            var s = CamelCaseEnumConverter<AccountStatus>.TryParse(status)
                    ?? throw ApiException.Validation("status", "Use active or suspended.");
            query = query.Where(c => c.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.FirstName + " " + c.LastName, term) ||
                                     EF.Functions.ILike(c.Mobile ?? "", term) || EF.Functions.ILike(c.Email ?? "", term) ||
                                     c.Addresses.Any(a => a.IsDefault && EF.Functions.ILike(a.Area, term)));
        }

        var paging = PageRequest.From(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Skip(paging.Skip).Take(paging.PageSize).ToListAsync(ct);
        var stats = await StatsAsync(db, rows.Select(c => c.Id).ToList(), ct);
        return TypedResults.Ok(new PagedList<CustomerRow>(rows.Select(c =>
        {
            var s = stats.GetValueOrDefault(c.Id) ?? new Stats(0, 0, 0, 0);
            return new CustomerRow(c.Id, c.FullName, c.Mobile, c.Email, AreaOf(c), c.Status, c.CreatedAt, s.PickupCount,
                s.CollectedKg, s.BalanceCents);
        }).ToList(), paging.Page, paging.PageSize, total));
    }

    private static async Task<Ok<CustomerDetail>> Get(string id, AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await DetailAsync(db, id, ct));

    private static async Task<Ok<CustomerDetail>> SetStatus(string id, AccountStatusRequest request, AppDbContext db,
        TokenService tokens, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw ApiException.NotFound(NotFoundCode, "Customer not found.");
        customer.Status = request.Status;
        customer.StatusReason = request.Status == AccountStatus.Suspended ? request.Reason!.Trim() : null;
        await db.SaveChangesAsync(ct);
        if (request.Status == AccountStatus.Suspended) await tokens.RevokeAllAsync(Audience.Customer, id, ct);
        return TypedResults.Ok(await DetailAsync(db, id, ct));
    }

    private static async Task<CustomerDetail> DetailAsync(AppDbContext db, string id, CancellationToken ct)
    {
        var c = await db.Customers.Include(x => x.Addresses).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw ApiException.NotFound(NotFoundCode, "Customer not found.");
        var s = (await StatsAsync(db, [id], ct)).GetValueOrDefault(id) ?? new Stats(0, 0, 0, 0);
        var recent = await db.Pickups.AsNoTracking().Where(p => p.CustomerId == id)
            .OrderByDescending(p => p.CreatedAt).Take(10).ToListAsync(ct);
        return new CustomerDetail(c.Id, c.FullName, c.Mobile, c.Email, AreaOf(c), c.Status, c.CreatedAt, s.PickupCount,
            s.CollectedKg, s.BalanceCents, c.Addresses.SingleOrDefault(a => a.IsDefault)?.Line,
            await db.FamilyMembers.CountAsync(m => m.CustomerId == id, ct), s.EarnedCents,
            recent.Select(p => new CustomerDetail.RecentPickup(p.Id, p.Status, p.ScheduledDate, p.EstimatedKg,
                p.ActualWeightKg, p.CustomerPayoutCents)).ToList());
    }

    /// <summary>Weighed pickups only; the balance is the wallet ledger's.</summary>
    private static async Task<Dictionary<string, Stats>> StatsAsync(AppDbContext db, List<string> ids,
        CancellationToken ct)
    {
        var pickups = await db.Pickups.Where(p => ids.Contains(p.CustomerId) && p.CollectedAt != null)
            .GroupBy(p => p.CustomerId)
            .Select(g => new { g.Key, Count = g.Count(), Kg = g.Sum(p => p.ActualWeightKg ?? 0), Earned = g.Sum(p => p.CustomerPayoutCents ?? 0) })
            .ToListAsync(ct);
        var balances = await db.WalletTransactions.Where(t => ids.Contains(t.CustomerId) && t.Status != TransactionStatus.Failed)
            .GroupBy(t => t.CustomerId).Select(g => new { g.Key, Balance = g.Sum(t => t.AmountCents) }).ToListAsync(ct);
        return ids.ToDictionary(id => id, id =>
        {
            var p = pickups.SingleOrDefault(x => x.Key == id);
            return new Stats(p?.Count ?? 0, p?.Kg ?? 0, p?.Earned ?? 0, balances.SingleOrDefault(b => b.Key == id)?.Balance ?? 0);
        });
    }

    private static string AreaOf(Data.Entities.Customer c) =>
        c.Addresses.SingleOrDefault(a => a.IsDefault)?.Area ?? c.Addresses.FirstOrDefault()?.Area ?? "";
}
