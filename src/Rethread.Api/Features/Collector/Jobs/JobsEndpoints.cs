using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Collector.Jobs;

public sealed record PhotoUploaded(string PhotoId);

public sealed record CollectionRequest(
    decimal WeightKg,
    WeightSource WeightSource,
    int BagCount,
    IReadOnlyList<string>? CheckedItemIds,
    IReadOnlyList<string>? PhotoIds);

public sealed record CollectionResult(
    string JobId,
    string CustomerFirstName,
    decimal WeightKg,
    long RatePerKgCents,
    long PayoutCents,
    LoadSummary Load);

public sealed record LoadSummary(decimal WeightKg, int CapacityKg, int JobCount);

public sealed class CollectionRequestValidator : AbstractValidator<CollectionRequest>
{
    public CollectionRequestValidator()
    {
        RuleFor(x => x.WeightKg).GreaterThan(0).LessThanOrEqualTo(1000);
        RuleFor(x => x.BagCount).InclusiveBetween(0, 100);
    }
}

public static class JobsEndpoints
{
    public const string JobNotFound = "JOB_NOT_FOUND";
    private const long MaxPhotoBytes = 2 * 1024 * 1024;

    public static void MapJobs(this RouteGroupBuilder api)
    {
        var jobs = api.MapGroup("/jobs");
        jobs.MapGet("", Board);
        jobs.MapGet("/active", Active);
        jobs.MapGet("/{id}", Get);
        jobs.MapPost("/{id}/accept", Accept);
        jobs.MapPost("/{id}/start", Start);
        jobs.MapPost("/{id}/arrive", Arrive);
        jobs.MapPost("/{id}/photos", UploadPhoto).DisableAntiforgery();
        jobs.MapPost("/{id}/collection", Collect).Validate<CollectionRequest>();
    }

    /// <summary>
    /// Open jobs for today and tomorrow within the radius, closest first. A job the call centre has
    /// offered to someone is still open: whoever answers first gets it.
    /// </summary>
    public static async Task<List<JobSummary>> OpenJobsAsync(AppDbContext db, Data.Entities.Collector collector,
        TimeProvider time, int radiusKm, CancellationToken ct)
    {
        var today = time.Today();
        var tomorrow = today.AddDays(1);
        var pending = await db.Pickups.Include(p => p.Customer)
            .Where(p => p.Status == PickupStatus.Pending && p.ScheduledDate >= today && p.ScheduledDate <= tomorrow)
            .ToListAsync(ct);
        return pending.Select(p => JobMapper.Summary(p, collector))
            .Where(j => j.DistanceKm <= radiusKm)
            .OrderBy(j => j.DistanceKm).ThenBy(j => j.WindowStart).ToList();
    }

    private static async Task<Ok<ItemList<JobSummary>>> Board(int? radiusKm, string? slot, string? size,
        CurrentUser user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        var slotFilter = slot is null ? (Slot?)null
            : CamelCaseEnumConverter<Slot>.TryParse(slot) ?? throw ApiException.Validation("slot", "Use morning, afternoon or evening.");
        var jobs = await OpenJobsAsync(db, collector, time, Math.Clamp(radiusKm ?? collector.RadiusKm, 1, 25), ct);
        if (slotFilter is { } s) jobs = jobs.Where(j => j.Slot == s).ToList();
        jobs = size switch
        {
            null => jobs,
            "small" => jobs.Where(j => j.EstimatedKg < 5).ToList(),
            "medium" => jobs.Where(j => j.EstimatedKg is >= 5 and < 10).ToList(),
            "large" => jobs.Where(j => j.EstimatedKg >= 10).ToList(),
            _ => throw ApiException.Validation("size", "Use small, medium or large."),
        };
        return TypedResults.Ok(new ItemList<JobSummary>(jobs));
    }

    /// <summary>The job the collector is working on: arrived first, then on the way, then accepted.</summary>
    public static async Task<Pickup?> ActiveJobAsync(AppDbContext db, string collectorId, CancellationToken ct)
    {
        var mine = await WithDetails(db.Pickups)
            .Where(p => p.CollectorId == collectorId &&
                        (p.Status == PickupStatus.Assigned || p.Status == PickupStatus.EnRoute || p.Status == PickupStatus.Arrived))
            .ToListAsync(ct);
        return mine.OrderByDescending(p => p.Status).ThenBy(p => p.WindowStart).FirstOrDefault();
    }

    private static async Task<Results<Ok<JobDetail>, NoContent>> Active(CurrentUser user, AppDbContext db,
        CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        var job = await ActiveJobAsync(db, collector.Id, ct);
        return job is null ? TypedResults.NoContent() : TypedResults.Ok(JobMapper.Detail(job, collector));
    }

    private static async Task<Ok<JobDetail>> Get(string id, CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        return TypedResults.Ok(JobMapper.Detail(await VisibleJobAsync(db, id, collector, ct), collector));
    }

    private static async Task<Ok<JobDetail>> Accept(string id, CurrentUser user, AppDbContext db,
        PickupWorkflow workflow, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        // Not VisibleJobAsync: a job someone else just took must answer JOB_TAKEN, not "not found".
        if (!await db.Pickups.AnyAsync(p => p.Id == id, ct)) throw ApiException.NotFound(JobNotFound, "Job not found.");
        await workflow.AcceptAsync(id, collector, ct);
        return await Reload(db, id, collector, ct);
    }

    private static async Task<Ok<JobDetail>> Start(string id, CurrentUser user, AppDbContext db,
        PickupWorkflow workflow, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        await workflow.StartAsync(await VisibleJobAsync(db, id, collector, ct), collector, ct);
        return await Reload(db, id, collector, ct);
    }

    private static async Task<Ok<JobDetail>> Arrive(string id, CurrentUser user, AppDbContext db,
        PickupWorkflow workflow, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        await workflow.ArriveAsync(await VisibleJobAsync(db, id, collector, ct), collector, ct);
        return await Reload(db, id, collector, ct);
    }

    private static async Task<Ok<PhotoUploaded>> UploadPhoto(string id, IFormFile file,
        [Microsoft.AspNetCore.Mvc.FromForm] string kind, CurrentUser user, AppDbContext db, IFileStore files,
        TimeProvider time, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        var photoKind = CamelCaseEnumConverter<PhotoKind>.TryParse(kind)
                        ?? throw ApiException.Validation("kind", "Use customer or bag.");
        var job = await VisibleJobAsync(db, id, collector, ct);
        if (job.CollectorId != collector.Id)
            throw ApiException.Forbidden(ErrorCodes.Forbidden, "This job belongs to another collector.");
        if (job.Status is not (PickupStatus.EnRoute or PickupStatus.Arrived))
            throw ApiException.Conflict(PickupWorkflow.JobNotActive, "Photos are taken at the customer's door.");
        if (file.Length > MaxPhotoBytes)
            throw new ApiException(StatusCodes.Status413PayloadTooLarge, "FILE_TOO_LARGE", "Photos can be up to 2 MB.");
        if (await ImageSniffer.DetectAsync(file, ct) != "image/jpeg")
            throw new ApiException(StatusCodes.Status415UnsupportedMediaType, "UNSUPPORTED_FILE_TYPE", "Send a JPEG photo.");

        await using var stream = file.OpenReadStream();
        var stored = await files.SaveAsync("pho", stream, "image/jpeg", collector.Id, ct);
        db.Files.Add(stored);
        db.PickupPhotos.Add(new PickupPhoto
        {
            Id = stored.Id,
            PickupId = job.Id,
            CollectorId = collector.Id,
            Kind = photoKind,
            FileId = stored.Id,
            CreatedAt = time.UtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new PhotoUploaded(stored.Id));
    }

    private static async Task<Ok<CollectionResult>> Collect(string id, CollectionRequest request, CurrentUser user,
        AppDbContext db, PickupWorkflow workflow, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        var job = await VisibleJobAsync(db, id, collector, ct);
        if (request.PhotoIds is { Count: > 0 } photoIds)
        {
            var known = await db.PickupPhotos.CountAsync(p => p.PickupId == id && photoIds.Contains(p.Id), ct);
            if (known != photoIds.Distinct().Count())
                throw ApiException.Validation("photoIds", "Those photos weren't taken for this job.");
        }

        await workflow.CollectAsync(job, collector,
            new PickupWorkflow.Collection(request.WeightKg, request.WeightSource, request.BagCount), ct);
        var (loadKg, jobCount) = await workflow.LoadAsync(collector.Id, ct);
        return TypedResults.Ok(new CollectionResult(job.Id, job.Customer?.FirstName ?? "Customer",
            job.ActualWeightKg!.Value, job.CustomerRatePerKgCents, job.CustomerPayoutCents!.Value,
            new LoadSummary(loadKg, collector.CapacityKg, jobCount)));
    }

    public static IQueryable<Pickup> WithDetails(IQueryable<Pickup> pickups) =>
        pickups.Include(p => p.Customer).Include(p => p.Items).AsSplitQuery();

    /// <summary>Open jobs and the collector's own jobs. Anyone else's is not found.</summary>
    public static async Task<Pickup> VisibleJobAsync(AppDbContext db, string id, Data.Entities.Collector collector,
        CancellationToken ct)
    {
        var job = await WithDetails(db.Pickups).SingleOrDefaultAsync(p => p.Id == id, ct);
        if (job is null || (job.Status != PickupStatus.Pending && job.CollectorId != collector.Id))
            throw ApiException.NotFound(JobNotFound, "Job not found.");
        return job;
    }

    private static async Task<Ok<JobDetail>> Reload(AppDbContext db, string id, Data.Entities.Collector collector,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var job = await WithDetails(db.Pickups).AsNoTracking().SingleAsync(p => p.Id == id, ct);
        return TypedResults.Ok(JobMapper.Detail(job, collector));
    }
}
