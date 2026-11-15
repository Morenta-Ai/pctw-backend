using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// What customers do with their wallet: cash to the bank (staff make the transfer), shop vouchers and
/// donations (both complete at once).
/// </summary>
public sealed class Redemptions(
    AppDbContext db,
    WalletLedger wallet,
    NotificationService notifications,
    BankAccountProtector bankAccounts,
    TimeProvider time)
{
    public const long MinimumCashCents = 50_000;
    public const long MinimumDonationCents = 5_000;
    public const string InsufficientBalance = "INSUFFICIENT_BALANCE";
    public const string BelowMinimumAmount = "BELOW_MINIMUM_AMOUNT";
    public const string BankDetailsRequired = "BANK_DETAILS_REQUIRED";
    public const string RedemptionClosed = "REDEMPTION_CLOSED";
    public const string RedemptionNotFound = "REDEMPTION_NOT_FOUND";

    public sealed record Request(RedemptionType Type, long AmountCents, string? VoucherBrandId, string? ProjectId);

    public sealed record Outcome(Redemption Redemption, long BalanceCents);

    public async Task<Outcome> RedeemAsync(Customer customer, Request request, CancellationToken ct)
    {
        if (request.AmountCents <= 0) throw ApiException.Validation("amountCents", "Enter an amount.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await wallet.LockAsync(customer.Id, ct);

        var now = time.UtcNow();
        var redemption = new Redemption
        {
            Id = Ids.New("red"),
            CustomerId = customer.Id,
            Type = request.Type,
            AmountCents = request.AmountCents,
            RequestedAt = now,
        };
        string title;

        switch (request.Type)
        {
            case RedemptionType.Cash:
                {
                    if (request.AmountCents < MinimumCashCents) throw BelowMinimum(MinimumCashCents);
                    var bank = await db.BankDetails.SingleOrDefaultAsync(b => b.CustomerId == customer.Id, ct)
                               ?? throw ApiException.Conflict(BankDetailsRequired, "Add your bank details first.");
                    redemption.Status = RedemptionStatus.Pending;
                    redemption.BankName = bank.BankName;
                    redemption.Branch = bank.Branch;
                    redemption.AccountName = bank.AccountName;
                    redemption.AccountNumberProtected = bank.AccountNumberProtected;
                    title = "Bank transfer";
                    break;
                }

            case RedemptionType.Voucher:
                {
                    var brand = await db.VoucherBrands.SingleOrDefaultAsync(b => b.Id == request.VoucherBrandId && b.Active, ct)
                                ?? throw ApiException.Validation("voucherBrandId", "Choose one of the vouchers.");
                    if (!brand.ValuesCents.Contains(request.AmountCents))
                        throw ApiException.Validation("amountCents", "Choose one of this voucher's values.");
                    redemption.Status = RedemptionStatus.Completed;
                    redemption.ProcessedAt = now;
                    redemption.VoucherBrandId = brand.Id;
                    redemption.VoucherBrandName = brand.Name;
                    redemption.VoucherCode = $"{brand.CodePrefix}-{Ids.Random(4, Alphabet)}-{Ids.Random(4, Digits)}";
                    title = $"Voucher · {brand.Name}";
                    break;
                }

            default:
                {
                    if (request.AmountCents < MinimumDonationCents) throw BelowMinimum(MinimumDonationCents);
                    var today = time.Today();
                    var project = await db.DonationProjects.SingleOrDefaultAsync(p => p.Id == request.ProjectId, ct);
                    if (project is null || !project.Active || project.EndsOn < today)
                        throw ApiException.Validation("projectId", "This project isn't taking donations.");
                    var firstGift = !await db.Redemptions.AnyAsync(
                        r => r.CustomerId == customer.Id && r.ProjectId == project.Id, ct);
                    project.RaisedCents += request.AmountCents;
                    if (firstGift) project.DonorCount++;
                    redemption.Status = RedemptionStatus.Completed;
                    redemption.ProcessedAt = now;
                    redemption.ProjectId = project.Id;
                    title = $"Donation · {project.Name}";
                    var (thanksTitle, thanksBody) = NotificationTexts.DonationThanks(request.AmountCents, project.Name);
                    notifications.ToCustomer(customer.Id, NotificationService.CustomerTypes.System, thanksTitle,
                        thanksBody, "wallet", null);
                    break;
                }
        }

        var balance = (await wallet.TotalsAsync(customer.Id, ct)).BalanceCents;
        if (balance < request.AmountCents)
        {
            throw ApiException.Conflict(InsufficientBalance, "Amount is more than your balance.",
                new { balanceCents = balance });
        }

        db.Redemptions.Add(redemption);
        wallet.Add(new WalletTransaction
        {
            Id = $"txn_{redemption.Id}",
            CustomerId = customer.Id,
            RedemptionId = redemption.Id,
            AmountCents = -request.AmountCents,
            OccurredAt = now,
            Type = request.Type switch
            {
                RedemptionType.Cash => TransactionType.BankTransfer,
                RedemptionType.Voucher => TransactionType.Voucher,
                _ => TransactionType.Donation,
            },
            Status = request.Type == RedemptionType.Cash ? TransactionStatus.Pending : TransactionStatus.Completed,
            Title = title,
        });

        if (request.Type == RedemptionType.Cash)
        {
            var (alertTitle, alertBody) = NotificationTexts.TransferRequested(redemption, customer.FullName);
            await notifications.ToStaffAsync(NotificationService.StaffTypes.TransferRequested, redemption.Id,
                alertTitle, alertBody, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new Outcome(redemption, balance - request.AmountCents);
    }

    /// <summary>Staff made the bank transfer: the pending debit becomes final.</summary>
    public async Task PayAsync(Redemption redemption, string reference, Staff staff, CancellationToken ct)
    {
        RequirePending(redemption);
        redemption.Status = RedemptionStatus.Paid;
        redemption.Reference = reference.Trim();
        await CloseAsync(redemption, staff, TransactionStatus.Completed, NotificationTexts.TransferPaid(redemption), ct);
    }

    /// <summary>Staff can't make the transfer: the money goes back into the wallet.</summary>
    public async Task RejectAsync(Redemption redemption, string reason, Staff staff, CancellationToken ct)
    {
        RequirePending(redemption);
        redemption.Status = RedemptionStatus.Rejected;
        redemption.RejectReason = reason.Trim();
        await CloseAsync(redemption, staff, TransactionStatus.Failed, NotificationTexts.TransferRejected(redemption), ct);
    }

    public string AccountNumber(Redemption redemption) =>
        redemption.AccountNumberProtected is null ? "" : bankAccounts.Unprotect(redemption.AccountNumberProtected);

    private async Task CloseAsync(Redemption redemption, Staff staff, TransactionStatus ledgerStatus,
        (string Title, string Body) text, CancellationToken ct)
    {
        redemption.ProcessedAt = time.UtcNow();
        redemption.ProcessedBy = staff.Name;
        await db.WalletTransactions.Where(t => t.RedemptionId == redemption.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, ledgerStatus), ct);
        notifications.ToCustomer(redemption.CustomerId, NotificationService.CustomerTypes.System, text.Title,
            text.Body, "wallet", null);
        await db.SaveChangesAsync(ct);
    }

    private static void RequirePending(Redemption redemption)
    {
        if (redemption.Type != RedemptionType.Cash || redemption.Status != RedemptionStatus.Pending)
            throw ApiException.Conflict(RedemptionClosed, "This request has already been handled.");
    }

    private static ApiException BelowMinimum(long minimumCents) =>
        ApiException.Unprocessable(BelowMinimumAmount, $"The minimum is {Money.Format(minimumCents)}.",
            new { minimumCents });

    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
    private const string Digits = "0123456789";
}
