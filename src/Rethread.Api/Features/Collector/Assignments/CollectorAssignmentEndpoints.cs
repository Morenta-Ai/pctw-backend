using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Collector.Jobs;

namespace Rethread.Api.Features.Collector.Assignments;

public sealed record CurrentOffer(string Id, DateTime ExpiresAt, JobDetail Job);

public sealed record DeclineRequest(DeclineReason Reason);

/// <summary>Offers from the call centre. Until push arrives, the app polls <c>current</c> every 15 seconds.</summary>
public static class CollectorAssignmentEndpoints
{
    public static void MapCollectorAssignments(this RouteGroupBuilder api)
    {
        var assignments = api.MapGroup("/assignments");
        assignments.MapGet("/current", Current);
        assignments.MapPost("/{id}/accept", Accept);
        assignments.MapPost("/{id}/decline", Decline);
    }

    private static async Task<Results<Ok<CurrentOffer>, NoContent>> Current(CurrentUser user, AppDbContext db,
        Maintenance maintenance, TimeProvider time, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        await maintenance.RunAsync(ct);
        var now = time.UtcNow();
        var offer = await db.Assignments.AsNoTracking()
            .Where(a => a.CollectorId == collector.Id && a.Status == AssignmentStatus.Offered && a.ExpiresAt > now)
            .OrderBy(a => a.ExpiresAt).FirstOrDefaultAsync(ct);
        if (offer is null) return TypedResults.NoContent();

        var job = await JobsEndpoints.WithDetails(db.Pickups).AsNoTracking().SingleAsync(p => p.Id == offer.PickupId, ct);
        return TypedResults.Ok(new CurrentOffer(offer.Id, offer.ExpiresAt, JobMapper.Detail(job, collector)));
    }

    private static async Task<Ok<JobDetail>> Accept(string id, CurrentUser user, AppDbContext db,
        AssignmentService assignments, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        var offer = await FindAsync(db, id, collector, ct);
        await assignments.AcceptAsync(offer, collector, ct);
        db.ChangeTracker.Clear();
        var job = await JobsEndpoints.WithDetails(db.Pickups).AsNoTracking().SingleAsync(p => p.Id == offer.PickupId, ct);
        return TypedResults.Ok(JobMapper.Detail(job, collector));
    }

    private static async Task<NoContent> Decline(string id, DeclineRequest request, CurrentUser user, AppDbContext db,
        AssignmentService assignments, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        await assignments.DeclineAsync(await FindAsync(db, id, collector, ct), request.Reason, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Assignment> FindAsync(AppDbContext db, string id, Data.Entities.Collector collector,
        CancellationToken ct) =>
        await db.Assignments.Include(a => a.Collector)
            .SingleOrDefaultAsync(a => a.Id == id && a.CollectorId == collector.Id, ct)
        ?? throw ApiException.NotFound(AssignmentService.AssignmentNotFound, "Offer not found.");
}
