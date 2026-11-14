using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// The customer wallet. Every credit and debit is a ledger row; the balance is never stored, so it can't
/// drift from the history the app shows.
/// </summary>
public sealed class WalletLedger(AppDbContext db, TimeProvider time)
{
    public sealed record Totals(long BalanceCents, long PendingCents);

    /// <summary>Completed and pending rows count (a pending transfer is already set aside); failed ones don't.</summary>
    public async Task<Totals> TotalsAsync(string customerId, CancellationToken ct)
    {
        var rows = db.WalletTransactions.Where(t => t.CustomerId == customerId && t.Status != TransactionStatus.Failed);
        var balance = await rows.SumAsync(t => (long?)t.AmountCents, ct) ?? 0;
        var pending = await rows.Where(t => t.Status == TransactionStatus.Pending).SumAsync(t => (long?)-t.AmountCents, ct) ?? 0;
        return new Totals(balance, pending);
    }

    /// <summary>Pays the customer for a weighed pickup. The caller saves the context.</summary>
    public WalletTransaction CreditPickup(Pickup pickup) => Add(new WalletTransaction
    {
        Id = $"txn_{pickup.Id}",
        CustomerId = pickup.CustomerId,
        Type = TransactionType.PickupEarning,
        Status = TransactionStatus.Completed,
        Title = $"Pickup #{pickup.Id}",
        AmountCents = pickup.CustomerPayoutCents ?? 0,
        OccurredAt = pickup.CollectedAt ?? time.UtcNow(),
        PickupId = pickup.Id,
        WeightKg = pickup.ActualWeightKg,
        RatePerKgCents = pickup.CustomerRatePerKgCents,
    });

    /// <summary>
    /// Serialises money movements for one customer until the current transaction ends, so two redemptions
    /// sent at once can't both spend the same balance. Call inside a database transaction.
    /// </summary>
    public async Task LockAsync(string customerId, CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({"wallet:" + customerId}))", ct);

    public WalletTransaction Add(WalletTransaction transaction)
    {
        db.WalletTransactions.Add(transaction);
        return transaction;
    }
}
