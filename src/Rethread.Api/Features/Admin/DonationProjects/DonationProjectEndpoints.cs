using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Admin.DonationProjects;

public sealed record DonationProjectDto(
    string Id,
    string Name,
    string Summary,
    long GoalCents,
    long RaisedCents,
    int DonorCount,
    bool Active,
    DateOnly? EndsOn,
    DateTime CreatedAt)
{
    public static DonationProjectDto From(DonationProject p) => new(p.Id, p.Name, p.Summary, p.GoalCents,
        p.RaisedCents, p.DonorCount, p.Active, p.EndsOn, p.CreatedAt);
}

/// <summary>Create and update bodies; on update every field is optional.</summary>
public sealed record DonationProjectRequest(string? Name, string? Summary, long? GoalCents, bool? Active,
    DateOnly? EndsOn);

public sealed class DonationProjectRequestValidator : AbstractValidator<DonationProjectRequest>
{
    public DonationProjectRequestValidator()
    {
        RuleFor(x => x.Name).MinimumLength(3).MaximumLength(80).When(x => x.Name is not null);
        RuleFor(x => x.Summary).NotEmpty().MaximumLength(80).When(x => x.Summary is not null);
        RuleFor(x => x.GoalCents).GreaterThanOrEqualTo(100_000).When(x => x.GoalCents is not null)
            .WithMessage("The goal must be at least Rs. 1,000.");
    }
}

/// <summary>Campaigns customers can donate wallet earnings to (Admin). Totals come from donations.</summary>
public static class DonationProjectEndpoints
{
    private const string NotFoundCode = "PROJECT_NOT_FOUND";

    public static void MapAdminDonationProjects(this RouteGroupBuilder admin)
    {
        var projects = admin.MapGroup("/donation-projects").AdminOnly();
        projects.MapGet("", List);
        projects.MapPost("", Create).Validate<DonationProjectRequest>();
        projects.MapPatch("/{id}", Update).Validate<DonationProjectRequest>();
    }

    private static async Task<Ok<ItemList<DonationProjectDto>>> List(AppDbContext db, CancellationToken ct)
    {
        var projects = await db.DonationProjects.OrderByDescending(p => p.Active).ThenByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        return TypedResults.Ok(new ItemList<DonationProjectDto>(projects.Select(DonationProjectDto.From).ToList()));
    }

    private static async Task<Created<DonationProjectDto>> Create(DonationProjectRequest request, AppDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var missing = new Dictionary<string, string>();
        if (request.Name is null) missing["name"] = "Enter a name.";
        if (request.Summary is null) missing["summary"] = "Enter a short summary.";
        if (request.GoalCents is null) missing["goalCents"] = "Enter a goal.";
        if (missing.Count > 0) throw ApiException.Validation(missing);
        RequireFuture(request.EndsOn, time);

        var project = new DonationProject
        {
            Id = Ids.New("dp"),
            Name = request.Name!.Trim(),
            Summary = request.Summary!.Trim(),
            GoalCents = request.GoalCents!.Value,
            Active = request.Active ?? true,
            EndsOn = request.EndsOn,
            CreatedAt = time.UtcNow(),
        };
        db.DonationProjects.Add(project);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/admin/donation-projects/{project.Id}", DonationProjectDto.From(project));
    }

    private static async Task<Ok<DonationProjectDto>> Update(string id, DonationProjectRequest request,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var project = await db.DonationProjects.SingleOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw ApiException.NotFound(NotFoundCode, "Project not found.");
        if (request.EndsOn is not null && request.EndsOn != project.EndsOn) RequireFuture(request.EndsOn, time);
        if (request.Name is not null) project.Name = request.Name.Trim();
        if (request.Summary is not null) project.Summary = request.Summary.Trim();
        if (request.GoalCents is { } goal) project.GoalCents = goal;
        if (request.Active is { } active) project.Active = active;
        if (request.EndsOn is not null) project.EndsOn = request.EndsOn;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(DonationProjectDto.From(project));
    }

    private static void RequireFuture(DateOnly? endsOn, TimeProvider time)
    {
        if (endsOn is { } date && date < time.Today())
            throw ApiException.Validation("endsOn", "The end date can't be in the past.");
    }
}
