using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Wallet;

/// <summary>What the app sees: never the full account number.</summary>
public sealed record MaskedBankDetails(string BankName, string Branch, string AccountName, string AccountNumberMasked)
{
    public static MaskedBankDetails From(BankDetails b) =>
        new(b.BankName, b.Branch, b.AccountName, BankAccountProtector.Mask(b.AccountLast4));
}

public sealed record BankDetailsRequest(string BankName, string Branch, string AccountName, string AccountNumber);

public sealed class BankDetailsRequestValidator : AbstractValidator<BankDetailsRequest>
{
    public BankDetailsRequestValidator()
    {
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Branch).NotEmpty().MaximumLength(80);
        RuleFor(x => x.AccountName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.AccountNumber).NotEmpty().Matches(@"^\d{6,20}$").WithMessage("Use the account number's digits only.");
    }
}

/// <summary>Where cash redemptions are paid. The number is encrypted at rest and only ever shown masked.</summary>
public static class BankDetailsEndpoints
{
    public const string NotFoundCode = "BANK_DETAILS_NOT_FOUND";

    public static void MapBankDetails(this RouteGroupBuilder api)
    {
        var bank = api.MapGroup("/wallet/bank-details");
        bank.MapGet("", Get);
        bank.MapPut("", Put).Validate<BankDetailsRequest>();
        bank.MapDelete("", Delete);
    }

    private static async Task<Ok<MaskedBankDetails>> Get(CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var details = await db.BankDetails.SingleOrDefaultAsync(b => b.CustomerId == customer.Id, ct)
                      ?? throw ApiException.NotFound(NotFoundCode, "No bank details saved.");
        return TypedResults.Ok(MaskedBankDetails.From(details));
    }

    private static async Task<Ok<MaskedBankDetails>> Put(BankDetailsRequest request, CurrentUser user,
        AppDbContext db, BankAccountProtector protector, TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var details = await db.BankDetails.SingleOrDefaultAsync(b => b.CustomerId == customer.Id, ct);
        if (details is null)
        {
            details = new BankDetails
            {
                CustomerId = customer.Id,
                BankName = "",
                Branch = "",
                AccountName = "",
                AccountNumberProtected = "",
                AccountLast4 = "",
            };
            db.BankDetails.Add(details);
        }

        details.BankName = request.BankName.Trim();
        details.Branch = request.Branch.Trim();
        details.AccountName = request.AccountName.Trim();
        details.AccountNumberProtected = protector.Protect(request.AccountNumber);
        details.AccountLast4 = BankAccountProtector.Last4(request.AccountNumber);
        details.UpdatedAt = time.UtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MaskedBankDetails.From(details));
    }

    private static async Task<NoContent> Delete(CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        await db.BankDetails.Where(b => b.CustomerId == customer.Id).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }
}
