using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Admin.Redemptions;

public sealed record PersonRef(string Id, string Name, string? Mobile);

/// <summary>A redemption for staff. <c>bank</c> carries the full account number, which staff need to pay it.</summary>
public sealed record RedemptionDto(
    string Id,
    RedemptionType Type,
    RedemptionStatus Status,
    long AmountCents,
    PersonRef Customer,
    RedemptionDto.BankInfo? Bank,
    RedemptionDto.VoucherInfo? Voucher,
    RedemptionDto.DonationInfo? Donation,
    DateTime RequestedAt,
    DateTime? ProcessedAt,
    string? ProcessedBy,
    string? Reference,
    string? RejectReason)
{
    public sealed record BankInfo(string BankName, string Branch, string AccountName, string AccountNumber);

    public sealed record VoucherInfo(string BrandName, string Code);

    public sealed record DonationInfo(string ProjectId, string ProjectName);
}

public sealed record PayRequest(string Reference);

public sealed record RejectRequest(string Reason);

public sealed class PayRequestValidator : AbstractValidator<PayRequest>
{
    public PayRequestValidator() => RuleFor(x => x.Reference).NotEmpty().MaximumLength(60);
}

public sealed class RejectRequestValidator : AbstractValidator<RejectRequest>
{
    public RejectRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(300);
}

/// <summary>The transfer queue and the record of vouchers and donations.</summary>
public static class RedemptionEndpoints
{
    public static void MapAdminRedemptions(this RouteGroupBuilder admin)
    {
        var redemptions = admin.MapGroup("/redemptions");
        redemptions.MapGet("", List);
        redemptions.MapPost("/{id}/pay", Pay).Validate<PayRequest>();
        redemptions.MapPost("/{id}/reject", Reject).Validate<RejectRequest>();
    }

    private static async Task<Ok<PagedList<RedemptionDto>>> List(string? type, string? status, string? q, int? page,
        int? pageSize, AppDbContext db, Domain.Redemptions redemptions, CancellationToken ct)
    {
        var query = db.Redemptions.Include(r => r.Customer).Include(r => r.Project).AsNoTracking();
        if (type is not null)
        {
            var t = CamelCaseEnumConverter<RedemptionType>.TryParse(type)
                    ?? throw ApiException.Validation("type", "Use cash, voucher or donation.");
            query = query.Where(r => r.Type == t);
        }

        RedemptionStatus? s = null;
        if (status is not null)
        {
            s = CamelCaseEnumConverter<RedemptionStatus>.TryParse(status)
                ?? throw ApiException.Validation("status", "Use pending, paid, rejected or completed.");
            query = query.Where(r => r.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(r => EF.Functions.ILike(r.Id, term) ||
                                     EF.Functions.ILike(r.Customer!.FirstName + " " + r.Customer.LastName, term) ||
                                     EF.Functions.ILike(r.Customer.Mobile ?? "", term));
        }

        // The queue works oldest first; everything else reads newest first.
        query = s == RedemptionStatus.Pending
            ? query.OrderBy(r => r.RequestedAt).ThenBy(r => r.Id)
            : query.OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.Id);
        return TypedResults.Ok(await query.ToPageAsync(PageRequest.From(page, pageSize), r => ToDto(r, redemptions), ct));
    }

    private static async Task<Ok<RedemptionDto>> Pay(string id, PayRequest request, CurrentUser user, AppDbContext db,
        Domain.Redemptions redemptions, CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        var redemption = await FindAsync(db, id, ct);
        await redemptions.PayAsync(redemption, request.Reference, staff, ct);
        return TypedResults.Ok(ToDto(redemption, redemptions));
    }

    private static async Task<Ok<RedemptionDto>> Reject(string id, RejectRequest request, CurrentUser user,
        AppDbContext db, Domain.Redemptions redemptions, CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        var redemption = await FindAsync(db, id, ct);
        await redemptions.RejectAsync(redemption, request.Reason, staff, ct);
        return TypedResults.Ok(ToDto(redemption, redemptions));
    }

    private static async Task<Redemption> FindAsync(AppDbContext db, string id, CancellationToken ct) =>
        await db.Redemptions.Include(r => r.Customer).Include(r => r.Project).SingleOrDefaultAsync(r => r.Id == id, ct)
        ?? throw ApiException.NotFound(Domain.Redemptions.RedemptionNotFound, "Request not found.");

    private static RedemptionDto ToDto(Redemption r, Domain.Redemptions redemptions) => new(
        r.Id, r.Type, r.Status, r.AmountCents,
        new PersonRef(r.CustomerId, r.Customer!.FullName, r.Customer.Mobile),
        r.Type == RedemptionType.Cash
            ? new RedemptionDto.BankInfo(r.BankName!, r.Branch!, r.AccountName!, redemptions.AccountNumber(r))
            : null,
        r.Type == RedemptionType.Voucher ? new RedemptionDto.VoucherInfo(r.VoucherBrandName!, r.VoucherCode!) : null,
        r.Type == RedemptionType.Donation ? new RedemptionDto.DonationInfo(r.ProjectId!, r.Project!.Name) : null,
        r.RequestedAt, r.ProcessedAt, r.ProcessedBy, r.Reference, r.RejectReason);
}
