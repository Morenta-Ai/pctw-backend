using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Customer.Auth;

namespace Rethread.Api.Features.Customer.Profile;

public sealed record UpdateProfileRequest(string FullName, string? Email, string? Mobile, Language? PreferredLanguage);

public sealed record ContactChangeRequest(string Channel, string NewDestination);

public sealed record ContactChangeVerify(string RequestId, string Code);

public sealed record DeleteAccountRequest(string Reason, string OtpRequestId, string OtpCode);

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).Must(n => n is not null && n.Trim().Length is >= 2 and <= 80)
            .WithMessage("Enter your name.");
        RuleFor(x => x.PreferredLanguage).NotNull().WithMessage("Choose a language.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Mobile!).Mobile().When(x => !string.IsNullOrWhiteSpace(x.Mobile));
    }
}

public sealed class ContactChangeRequestValidator : AbstractValidator<ContactChangeRequest>
{
    public ContactChangeRequestValidator()
    {
        RuleFor(x => x.Channel).Must(c => c is "sms" or "email").WithMessage("Use sms or email.");
        RuleFor(x => x.NewDestination).Mobile().When(x => x.Channel == "sms");
        RuleFor(x => x.NewDestination).NotEmpty().EmailAddress().When(x => x.Channel == "email");
    }
}

public sealed class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    private static readonly string[] Reasons = ["notUseful", "tooFewPickups", "privacy", "movingAway", "otherApp", "other"];

    public DeleteAccountRequestValidator()
    {
        RuleFor(x => x.Reason).Must(r => Reasons.Contains(r)).WithMessage("Choose a reason.");
        RuleFor(x => x.OtpRequestId).NotEmpty();
        RuleFor(x => x.OtpCode).NotEmpty();
    }
}

public static class ProfileEndpoints
{
    private const long MaxAvatarBytes = 1024 * 1024;

    public static void MapCustomerProfile(this RouteGroupBuilder api)
    {
        api.MapGet("/me", Get);
        api.MapPut("/me", Update).Validate<UpdateProfileRequest>();
        api.MapDelete("/me", Delete).Validate<DeleteAccountRequest>();
        api.MapPost("/me/contact-change/request", RequestContactChange).Validate<ContactChangeRequest>();
        api.MapPost("/me/contact-change/verify", VerifyContactChange);
        api.MapPost("/me/avatar", UploadAvatar).DisableAntiforgery();
        api.MapDelete("/me/avatar", RemoveAvatar);
    }

    public static async Task<CustomerMe> MeAsync(Data.Entities.Customer customer, AppDbContext db, PublicUrls urls,
        CancellationToken ct) =>
        CustomerMe.From(customer, await db.BankDetails.AnyAsync(b => b.CustomerId == customer.Id, ct), urls);

    private static async Task<Ok<CustomerMe>> Get(CurrentUser user, AppDbContext db, PublicUrls urls,
        CancellationToken ct) =>
        TypedResults.Ok(await MeAsync(await user.CustomerAsync(ct), db, urls, ct));

    /// <summary>
    /// Saves the name and language. Email and mobile can only be filled in here while empty (the
    /// complete-profile step); afterwards they change through the verified contact change. The first save
    /// also creates the customer's own family profile.
    /// </summary>
    private static async Task<Ok<CustomerMe>> Update(UpdateProfileRequest request, CurrentUser user, AppDbContext db,
        PublicUrls urls, TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var completing = customer.FirstName is null;
        var errors = new Dictionary<string, string>();
        await ApplyContactAsync(db, customer, "email", request.Email?.Trim().ToLowerInvariant(), errors, ct);
        await ApplyContactAsync(db, customer, "mobile", request.Mobile?.Trim(), errors, ct);
        if (errors.Count > 0) throw ApiException.Validation(errors);

        var (first, last) = SplitName(request.FullName);
        customer.FirstName = first;
        customer.LastName = last;
        customer.Language = request.PreferredLanguage!.Value;
        if (completing && !await db.FamilyMembers.AnyAsync(m => m.CustomerId == customer.Id && m.Relation == FamilyRelation.Self, ct))
        {
            db.FamilyMembers.Add(new FamilyMember
            {
                Id = Ids.New("fam"),
                CustomerId = customer.Id,
                Name = customer.FullName,
                Relation = FamilyRelation.Self,
                Gender = Gender.Other,
                AgeGroup = AgeGroup.Adult,
                CreatedAt = time.UtcNow(),
            });
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await MeAsync(customer, db, urls, ct));
    }

    private static async Task ApplyContactAsync(AppDbContext db, Data.Entities.Customer customer, string field,
        string? value, Dictionary<string, string> errors, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(value)) return;
        var current = field == "email" ? customer.Email : customer.Mobile;
        if (current == value) return;
        if (current is not null)
        {
            errors[field] = "Change it through the verified contact change.";
            return;
        }

        var taken = field == "email"
            ? await db.Customers.AnyAsync(c => c.Email == value && c.Id != customer.Id, ct)
            : await db.Customers.AnyAsync(c => c.Mobile == value && c.Id != customer.Id, ct);
        if (taken) throw ApiException.Conflict("CONTACT_IN_USE", "Another account uses this.", new { field });
        if (field == "email") customer.Email = value;
        else customer.Mobile = value;
    }

    public static (string First, string? Last) SplitName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (parts[0], parts.Length > 1 ? parts[1] : null);
    }

    private static async Task<Ok<CustomerOtpChallenge>> RequestContactChange(ContactChangeRequest request,
        CurrentUser user, AppDbContext db, OtpService otp, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var destination = CustomerAuthEndpoints.Normalise(request.Channel, request.NewDestination);
        var taken = request.Channel == "email"
            ? await db.Customers.AnyAsync(c => c.Email == destination && c.Id != customer.Id, ct)
            : await db.Customers.AnyAsync(c => c.Mobile == destination && c.Id != customer.Id, ct);
        if (taken) throw ApiException.Conflict("CONTACT_IN_USE", "Another account uses this.");
        var challenge = await otp.SendAsync(Audience.Customer, CustomerAuthEndpoints.Channel(request.Channel), destination,
            customer.Id, ct, OtpService.ContactChange, CustomerAuthEndpoints.ResendAfterSeconds);
        return TypedResults.Ok(new CustomerOtpChallenge(challenge.ChallengeId, challenge.ExpiresInSeconds,
            challenge.ResendAfterSeconds));
    }

    private static async Task<Ok<CustomerMe>> VerifyContactChange(ContactChangeVerify request, CurrentUser user,
        AppDbContext db, OtpService otp, PublicUrls urls, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var challenge = await otp.VerifyAsync(Audience.Customer, request.RequestId, request.Code, ct, OtpService.ContactChange);
        if (challenge.SubjectId != customer.Id)
            throw ApiException.BadRequest(ErrorDialect.OtpUnknownRequest, "That code has expired. Ask for a new one.");
        if (challenge.Channel == "email") customer.Email = challenge.Destination;
        else customer.Mobile = challenge.Destination;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await MeAsync(customer, db, urls, ct));
    }

    /// <summary>A square JPEG the app has already cropped and stripped of EXIF data.</summary>
    private static async Task<Ok<CustomerMe>> UploadAvatar(IFormFile file, CurrentUser user, AppDbContext db,
        IFileStore files, PublicUrls urls, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        if (file.Length > MaxAvatarBytes)
            throw new ApiException(StatusCodes.Status413PayloadTooLarge, "FILE_TOO_LARGE", "The photo is too large.");
        if (await ImageSniffer.DetectAsync(file, ct) != "image/jpeg")
            throw new ApiException(StatusCodes.Status415UnsupportedMediaType, "UNSUPPORTED_FILE_TYPE", "Send a JPEG photo.");
        await using var stream = file.OpenReadStream();
        var stored = await files.SaveAsync("avt", stream, "image/jpeg", customer.Id, ct);
        db.Files.Add(stored);
        customer.AvatarFileId = stored.Id;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await MeAsync(customer, db, urls, ct));
    }

    private static async Task<Ok<CustomerMe>> RemoveAvatar(CurrentUser user, AppDbContext db, PublicUrls urls,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        customer.AvatarFileId = null;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await MeAsync(customer, db, urls, ct));
    }

    /// <summary>
    /// Deletes the account, confirmed with a code sent to the account's own mobile (or email). Completed
    /// pickups, payments and orders stay, without personal details.
    /// </summary>
    private static async Task<NoContent> Delete([Microsoft.AspNetCore.Mvc.FromBody] DeleteAccountRequest request,
        CurrentUser user, AppDbContext db,
        OtpService otp, TokenService tokens, WalletLedger wallet, TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var challenge = await otp.VerifyAsync(Audience.Customer, request.OtpRequestId, request.OtpCode, ct);
        if (challenge.Destination != customer.Mobile && challenge.Destination != customer.Email)
            throw ApiException.BadRequest(ErrorDialect.OtpUnknownRequest, "That code wasn't sent to this account.");

        var active = await db.Pickups.CountAsync(p => p.CustomerId == customer.Id &&
            (PickupWorkflow.Open.Contains(p.Status) || p.Status == PickupStatus.Collected), ct);
        if (active > 0)
            throw ApiException.Conflict("ACTIVE_PICKUPS", "Finish or cancel your pickups first.", new { count = active });
        var balance = (await wallet.TotalsAsync(customer.Id, ct)).BalanceCents;
        if (balance > 0)
            throw ApiException.Conflict("PENDING_BALANCE", "Your wallet still holds money.", new { balanceCents = balance });

        customer.FirstName = "Deleted";
        customer.LastName = "account";
        customer.Mobile = null;
        customer.Email = null;
        customer.AvatarFileId = null;
        customer.Status = AccountStatus.Suspended;
        customer.StatusReason = $"Account deleted ({request.Reason}).";
        customer.DeletedAt = time.UtcNow();
        await db.Addresses.Where(a => a.CustomerId == customer.Id).ExecuteDeleteAsync(ct);
        await db.FamilyMembers.Where(m => m.CustomerId == customer.Id).ExecuteDeleteAsync(ct);
        await db.BankDetails.Where(b => b.CustomerId == customer.Id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await tokens.RevokeAllAsync(Audience.Customer, customer.Id, ct);
        return TypedResults.NoContent();
    }
}
