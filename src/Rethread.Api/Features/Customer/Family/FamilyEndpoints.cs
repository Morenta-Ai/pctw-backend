using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Customer.Family;

public sealed record FamilyMemberDto(
    string Id,
    string Name,
    FamilyRelation Relation,
    Gender Gender,
    AgeGroup AgeGroup,
    DateOnly? DateOfBirth,
    decimal? HeightCm,
    decimal? ChestCm,
    decimal? WaistCm,
    decimal? HipCm,
    string? ShoeSize)
{
    public static FamilyMemberDto From(FamilyMember m) => new(m.Id, m.Name, m.Relation, m.Gender, m.AgeGroup,
        m.DateOfBirth, m.HeightCm, m.ChestCm, m.WaistCm, m.HipCm, m.ShoeSize);
}

/// <summary>The create and replace body.</summary>
public sealed record FamilyMemberRequest(
    string Name,
    FamilyRelation Relation,
    Gender Gender,
    AgeGroup AgeGroup,
    DateOnly? DateOfBirth,
    decimal? HeightCm,
    decimal? ChestCm,
    decimal? WaistCm,
    decimal? HipCm,
    string? ShoeSize);

/// <summary>Rejects only impossible values, and never echoes a measurement back.</summary>
public sealed class FamilyMemberRequestValidator : AbstractValidator<FamilyMemberRequest>
{
    public FamilyMemberRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => n is not null && n.Trim().Length is >= 1 and <= 80).WithMessage("Enter a name.");
        RuleFor(x => x.HeightCm).InclusiveBetween(30, 250).When(x => x.HeightCm is not null).WithMessage("That height isn't possible.");
        RuleFor(x => x.ChestCm).InclusiveBetween(20, 250).When(x => x.ChestCm is not null).WithMessage("That chest size isn't possible.");
        RuleFor(x => x.WaistCm).InclusiveBetween(20, 250).When(x => x.WaistCm is not null).WithMessage("That waist size isn't possible.");
        RuleFor(x => x.HipCm).InclusiveBetween(20, 250).When(x => x.HipCm is not null).WithMessage("That hip size isn't possible.");
        RuleFor(x => x.ShoeSize).Matches(@"^\d{1,2}(\.5)?$").When(x => !string.IsNullOrEmpty(x.ShoeSize))
            .WithMessage("Use an EU shoe size, such as 38 or 38.5.");
    }
}

/// <summary>
/// Family profiles for sizing. Every customer has one <c>self</c> member, made from their name; it comes first,
/// can't be deleted, and nobody else can be <c>self</c>.
/// </summary>
public static class FamilyEndpoints
{
    private const string NotFoundCode = "FAMILY_MEMBER_NOT_FOUND";

    public static void MapFamily(this RouteGroupBuilder api)
    {
        var family = api.MapGroup("/family-members");
        family.MapGet("", List);
        family.MapPost("", Create).Validate<FamilyMemberRequest>();
        family.MapPut("/{id}", Replace).Validate<FamilyMemberRequest>();
        family.MapDelete("/{id}", Delete);
    }

    private static async Task<Ok<ItemList<FamilyMemberDto>>> List(CurrentUser user, AppDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var members = await db.FamilyMembers.Where(m => m.CustomerId == customer.Id).ToListAsync(ct);
        // Accounts from before family profiles existed get their own profile the first time they look.
        if (customer.FirstName is not null && members.All(m => m.Relation != FamilyRelation.Self))
        {
            var self = new FamilyMember
            {
                Id = Ids.New("fam"),
                CustomerId = customer.Id,
                Name = customer.FullName,
                Relation = FamilyRelation.Self,
                Gender = Gender.Other,
                AgeGroup = AgeGroup.Adult,
                CreatedAt = time.UtcNow(),
            };
            db.FamilyMembers.Add(self);
            await db.SaveChangesAsync(ct);
            members.Add(self);
        }

        return TypedResults.Ok(new ItemList<FamilyMemberDto>(members
            .OrderBy(m => m.Relation != FamilyRelation.Self).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(FamilyMemberDto.From).ToList()));
    }

    private static async Task<Created<FamilyMemberDto>> Create(FamilyMemberRequest request, CurrentUser user,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        if (request.Relation == FamilyRelation.Self)
            throw ApiException.Validation("relation", "Your own profile is already there.");
        var member = new FamilyMember { Id = Ids.New("fam"), CustomerId = customer.Id, Name = "", CreatedAt = time.UtcNow() };
        Apply(member, request, time);
        db.FamilyMembers.Add(member);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/family-members/{member.Id}", FamilyMemberDto.From(member));
    }

    private static async Task<Ok<FamilyMemberDto>> Replace(string id, FamilyMemberRequest request, CurrentUser user,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var member = await FindAsync(id, user, db, ct);
        if ((member.Relation == FamilyRelation.Self) != (request.Relation == FamilyRelation.Self))
            throw ApiException.Validation("relation", "Your own profile stays yours, and nobody else can be you.");
        Apply(member, request, time);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FamilyMemberDto.From(member));
    }

    private static async Task<NoContent> Delete(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var member = await FindAsync(id, user, db, ct);
        if (member.Relation == FamilyRelation.Self)
            throw ApiException.Forbidden("CANNOT_DELETE_SELF", "You can't delete your own profile.");
        db.FamilyMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static void Apply(FamilyMember member, FamilyMemberRequest request, TimeProvider time)
    {
        if (request.DateOfBirth is { } dob && dob > time.Today())
            throw ApiException.Validation("dateOfBirth", "That date is in the future.");
        member.Name = request.Name.Trim();
        member.Relation = request.Relation;
        member.Gender = request.Gender;
        member.AgeGroup = request.AgeGroup;
        member.DateOfBirth = request.DateOfBirth;
        member.HeightCm = request.HeightCm;
        member.ChestCm = request.ChestCm;
        member.WaistCm = request.WaistCm;
        member.HipCm = request.HipCm;
        member.ShoeSize = string.IsNullOrWhiteSpace(request.ShoeSize) ? null : request.ShoeSize.Trim();
    }

    private static async Task<FamilyMember> FindAsync(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        return await db.FamilyMembers.SingleOrDefaultAsync(m => m.Id == id && m.CustomerId == customer.Id, ct)
               ?? throw ApiException.NotFound(NotFoundCode, "Family member not found.");
    }
}
