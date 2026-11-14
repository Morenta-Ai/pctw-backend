using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Admin.Assignments;

public sealed record AssignmentDto(
    string Id,
    string PickupId,
    AssignmentDto.CollectorRef Collector,
    AssignmentStatus Status,
    string? Note,
    DateTime OfferedAt,
    DateTime ExpiresAt,
    DateTime? RespondedAt,
    string? DeclineReason,
    string OfferedBy)
{
    public sealed record CollectorRef(string Id, string Code, string Name);

    public static AssignmentDto From(Assignment a) => new(a.Id, a.PickupId,
        new CollectorRef(a.Collector!.Id, a.Collector.Code, a.Collector.FullName), a.Status, a.Note, a.OfferedAt,
        a.ExpiresAt, a.RespondedAt, a.DeclineReason, a.OfferedBy);
}

public sealed record OfferRequest(string CollectorId, string? Note);

public sealed class OfferRequestValidator : AbstractValidator<OfferRequest>
{
    public OfferRequestValidator()
    {
        RuleFor(x => x.CollectorId).NotEmpty().WithMessage("Choose a collector.");
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

/// <summary>The call centre offering pickups to collectors. The portal polls an open offer every 2 seconds.</summary>
public static class AssignmentEndpoints
{
    public static void MapAdminAssignments(this RouteGroupBuilder admin)
    {
        admin.MapPost("/pickups/{id}/assignments", Offer).Validate<OfferRequest>();
        admin.MapGet("/pickups/{id}/assignments", ListForPickup);
        admin.MapGet("/assignments/{id}", Get);
        admin.MapPost("/assignments/{id}/withdraw", Withdraw);
    }

    private static async Task<Created<AssignmentDto>> Offer(string id, OfferRequest request, CurrentUser user,
        AppDbContext db, AssignmentService assignments, CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        var pickup = await db.Pickups.SingleOrDefaultAsync(p => p.Id == id, ct)
                     ?? throw ApiException.NotFound(PickupWorkflow.PickupNotFound, "Pickup not found.");
        var offer = await assignments.OfferAsync(pickup, request.CollectorId, request.Note, staff, ct);
        return TypedResults.Created($"/api/v1/admin/assignments/{offer.Id}", AssignmentDto.From(offer));
    }

    private static async Task<Ok<ItemList<AssignmentDto>>> ListForPickup(string id, AppDbContext db,
        Maintenance maintenance, CancellationToken ct)
    {
        if (!await db.Pickups.AnyAsync(p => p.Id == id, ct))
            throw ApiException.NotFound(PickupWorkflow.PickupNotFound, "Pickup not found.");
        await maintenance.RunAsync(ct);
        var offers = await db.Assignments.Include(a => a.Collector).AsNoTracking()
            .Where(a => a.PickupId == id).OrderByDescending(a => a.OfferedAt).ToListAsync(ct);
        return TypedResults.Ok(new ItemList<AssignmentDto>(offers.Select(AssignmentDto.From).ToList()));
    }

    private static async Task<Ok<AssignmentDto>> Get(string id, AppDbContext db, Maintenance maintenance,
        CancellationToken ct)
    {
        await maintenance.RunAsync(ct);
        return TypedResults.Ok(AssignmentDto.From(await FindAsync(db, id, ct)));
    }

    private static async Task<Ok<AssignmentDto>> Withdraw(string id, AppDbContext db, AssignmentService assignments,
        CancellationToken ct) =>
        TypedResults.Ok(AssignmentDto.From(await assignments.WithdrawAsync(await FindAsync(db, id, ct), ct)));

    private static async Task<Assignment> FindAsync(AppDbContext db, string id, CancellationToken ct) =>
        await db.Assignments.Include(a => a.Collector).SingleOrDefaultAsync(a => a.Id == id, ct)
        ?? throw ApiException.NotFound(AssignmentService.AssignmentNotFound, "Offer not found.");
}
