using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Family;

public sealed record FamilyMemberDto(
    string Id,
    string Name,
    FamilyRelation Relation,
    Gender Gender,
    AgeGroup AgeGroup,
    decimal? HeightCm,
    decimal? ChestCm,
    decimal? WaistCm,
    decimal? HipCm,
    string? ShoeSize,
    string? TopSize,
    string? BottomSize)
{
    public static FamilyMemberDto From(FamilyMember m) => new(m.Id, m.Name, m.Relation, m.Gender, m.AgeGroup,
        m.HeightCm, m.ChestCm, m.WaistCm, m.HipCm, m.ShoeSize, SizeGuide.TopSize(m), SizeGuide.BottomSize(m));
}

/// <summary>Create and update bodies. <c>topSize</c> and <c>bottomSize</c> are derived, so they're ignored.</summary>
public sealed record FamilyMemberRequest(
    string? Name,
    FamilyRelation? Relation,
    Gender? Gender,
    AgeGroup? AgeGroup,
    decimal? HeightCm,
    decimal? ChestCm,
    decimal? WaistCm,
    decimal? HipCm,
    string? ShoeSize);

public sealed class FamilyMemberRequestValidator : AbstractValidator<FamilyMemberRequest>
{
    public FamilyMemberRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80).When(x => x.Name is not null);
        RuleFor(x => x.HeightCm).InclusiveBetween(40, 230).When(x => x.HeightCm is not null);
        RuleFor(x => x.ChestCm).InclusiveBetween(30, 200).When(x => x.ChestCm is not null);
        RuleFor(x => x.WaistCm).InclusiveBetween(30, 200).When(x => x.WaistCm is not null);
        RuleFor(x => x.HipCm).InclusiveBetween(30, 200).When(x => x.HipCm is not null);
        RuleFor(x => x.ShoeSize).MaximumLength(12);
    }
}

public static class FamilyEndpoints
{
    private const string NotFoundCode = "FAMILY_MEMBER_NOT_FOUND";

    public static void MapFamily(this RouteGroupBuilder api)
    {
        var family = api.MapGroup("/family-members");
        family.MapGet("", List);
        family.MapPost("", Create).Validate<FamilyMemberRequest>();
        family.MapPatch("/{id}", Update).Validate<FamilyMemberRequest>();
        family.MapDelete("/{id}", Delete);
    }

    private static async Task<Ok<ItemList<FamilyMemberDto>>> List(CurrentUser user, AppDbContext db,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var members = await db.FamilyMembers.Where(m => m.CustomerId == customer.Id)
            .OrderBy(m => m.Relation != FamilyRelation.Self).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(new ItemList<FamilyMemberDto>(members.Select(FamilyMemberDto.From).ToList()));
    }

    private static async Task<Created<FamilyMemberDto>> Create(FamilyMemberRequest request, CurrentUser user,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        var missing = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(request.Name)) missing["name"] = "Enter a name.";
        if (request.Relation is null) missing["relation"] = "Choose a relation.";
        if (request.Gender is null) missing["gender"] = "Choose a gender.";
        if (request.AgeGroup is null) missing["ageGroup"] = "Choose an age group.";
        if (missing.Count > 0) throw ApiException.Validation(missing);

        var member = new FamilyMember
        {
            Id = Ids.New("fam"),
            CustomerId = customer.Id,
            Name = request.Name!.Trim(),
            Relation = request.Relation!.Value,
            Gender = request.Gender!.Value,
            AgeGroup = request.AgeGroup!.Value,
            CreatedAt = time.UtcNow(),
        };
        Apply(member, request);
        db.FamilyMembers.Add(member);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/family-members/{member.Id}", FamilyMemberDto.From(member));
    }

    private static async Task<Ok<FamilyMemberDto>> Update(string id, FamilyMemberRequest request, CurrentUser user,
        AppDbContext db, CancellationToken ct)
    {
        var member = await FindAsync(id, user, db, ct);
        if (request.Name is not null) member.Name = request.Name.Trim();
        if (request.Relation is { } relation) member.Relation = relation;
        if (request.Gender is { } gender) member.Gender = gender;
        if (request.AgeGroup is { } age) member.AgeGroup = age;
        Apply(member, request);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FamilyMemberDto.From(member));
    }

    private static async Task<NoContent> Delete(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        db.FamilyMembers.Remove(await FindAsync(id, user, db, ct));
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static void Apply(FamilyMember member, FamilyMemberRequest request)
    {
        if (request.HeightCm is not null) member.HeightCm = request.HeightCm;
        if (request.ChestCm is not null) member.ChestCm = request.ChestCm;
        if (request.WaistCm is not null) member.WaistCm = request.WaistCm;
        if (request.HipCm is not null) member.HipCm = request.HipCm;
        if (request.ShoeSize is not null) member.ShoeSize = request.ShoeSize.Trim();
    }

    private static async Task<FamilyMember> FindAsync(string id, CurrentUser user, AppDbContext db,
        CancellationToken ct)
    {
        var customer = await user.CustomerAsync(ct);
        return await db.FamilyMembers.SingleOrDefaultAsync(m => m.Id == id && m.CustomerId == customer.Id, ct)
               ?? throw ApiException.NotFound(NotFoundCode, "Family member not found.");
    }
}
