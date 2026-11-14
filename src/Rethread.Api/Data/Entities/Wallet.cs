namespace Rethread.Api.Data.Entities;

/// <summary>
/// The customer's wallet ledger. Amounts are signed: credits positive, debits negative. The balance is the
/// sum of completed and pending rows (a pending transfer is already set aside); failed rows don't count.
/// </summary>
public sealed class WalletTransaction
{
    public required string Id { get; set; }
    public required string CustomerId { get; set; }
    public TransactionType Type { get; set; }
    public TransactionStatus Status { get; set; }
    public required string Title { get; set; }
    public long AmountCents { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? PickupId { get; set; }
    public decimal? WeightKg { get; set; }
    public long? RatePerKgCents { get; set; }
    public string? RedemptionId { get; set; }
    public string? OrderId { get; set; }
}

public sealed class Redemption
{
    public required string Id { get; set; }
    public required string CustomerId { get; set; }
    public RedemptionType Type { get; set; }
    public RedemptionStatus Status { get; set; }
    public long AmountCents { get; set; }
    public string? BankName { get; set; }
    public string? Branch { get; set; }
    public string? AccountName { get; set; }
    public string? AccountNumberProtected { get; set; }
    public string? VoucherBrandId { get; set; }
    public string? VoucherBrandName { get; set; }
    public string? VoucherCode { get; set; }
    public string? ProjectId { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessedBy { get; set; }
    public string? Reference { get; set; }
    public string? RejectReason { get; set; }
    public uint Version { get; set; }

    public Customer? Customer { get; set; }
    public DonationProject? Project { get; set; }
}

public sealed class DonationProject
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Summary { get; set; }
    public long GoalCents { get; set; }
    /// <summary>Kept as a running total: projects started with money raised before the app.</summary>
    public long RaisedCents { get; set; }
    public int DonorCount { get; set; }
    public bool Active { get; set; } = true;
    public DateOnly? EndsOn { get; set; }
    public DateTime CreatedAt { get; set; }
}
