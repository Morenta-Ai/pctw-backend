using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Admin.Redemptions;

namespace Rethread.Api.Features.Admin.Pickups;

public sealed record CollectorRef(string Id, string Code, string Name);

public sealed record PickupRow(
    string Id,
    PickupStatus Status,
    PickupSource Source,
    PickupMethod Method,
    string CustomerName,
    string Area,
    DateOnly ScheduledDate,
    Slot Slot,
    DateTime WindowStart,
    DateTime WindowEnd,
    decimal EstimatedKg,
    decimal? ActualWeightKg,
    CollectorRef? Collector,
    DateTime CreatedAt)
{
    public static PickupRow From(Pickup p) => new(p.Id, p.Status, p.Source, p.Method, p.Customer!.FullName, p.Area,
        p.ScheduledDate, p.Slot, p.WindowStart, p.WindowEnd, p.EstimatedKg, p.ActualWeightKg,
        p.Collector is null ? null : new CollectorRef(p.Collector.Id, p.Collector.Code, p.Collector.FullName),
        p.CreatedAt);
}

public sealed record PickupDetail(
    string Id,
    PickupStatus Status,
    PickupSource Source,
    PickupMethod Method,
    PersonRef Customer,
    string Area,
    string Address,
    double Latitude,
    double Longitude,
    DateOnly ScheduledDate,
    Slot Slot,
    DateTime WindowStart,
    DateTime WindowEnd,
    decimal EstimatedKg,
    int? ItemCount,
    int BagCount,
    decimal? ActualWeightKg,
    string? Note,
    IReadOnlyList<PickupDetail.Item> Items,
    PickupDetail.CollectorInfo? Collector,
    long CustomerRatePerKgCents,
    long CollectorRatePerKgCents,
    long? CustomerPayoutCents,
    long? CollectorPayoutCents,
    PickupDetail.HubRef? Hub,
    string? CancelReason,
    DateTime CreatedAt,
    IReadOnlyList<PickupDetail.Step> Timeline,
    IReadOnlyList<PickupDetail.PreferredDate> PreferredDates)
{
    public sealed record PreferredDate(int Priority, DateOnly Date, Slot Slot);

    public sealed record Item(string Name, int? Quantity, string? Size, ItemCondition Condition);

    public sealed record CollectorInfo(string Id, string Code, string Name, string Mobile, string VehiclePlate);

    public sealed record HubRef(string Id, string Name);

    public sealed record Step(PickupStatus Status, DateTime OccurredAt, string? Actor);

    public static PickupDetail From(Pickup p) => new(p.Id, p.Status, p.Source, p.Method,
        new PersonRef(p.CustomerId, p.Customer!.FullName, p.Customer.Mobile), p.Area, p.AddressLine, p.Latitude,
        p.Longitude, p.ScheduledDate, p.Slot, p.WindowStart, p.WindowEnd, p.EstimatedKg, p.ItemCount, p.BagCount,
        p.ActualWeightKg, p.Note,
        p.Items.OrderBy(i => i.SortOrder).Select(i => new Item(i.Name, i.Quantity, i.Size, i.Condition)).ToList(),
        p.Collector is null ? null : new CollectorInfo(p.Collector.Id, p.Collector.Code, p.Collector.FullName,
            p.Collector.Mobile, p.Collector.VehiclePlate),
        p.CustomerRatePerKgCents, p.CollectorRatePerKgCents, p.CustomerPayoutCents, p.CollectorPayoutCents,
        p.Hub is null ? null : new HubRef(p.Hub.Id, p.Hub.Name), p.CancelReason, p.CreatedAt,
        p.Events.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).Select(e => new Step(e.Status, e.OccurredAt, e.Actor)).ToList(),
        p.PreferredDates.OrderBy(d => d.Priority).Select(d => new PreferredDate(d.Priority, d.Date, d.Slot)).ToList());
}

public sealed record CancelRequest(string Reason);

public sealed record ScheduleRequest(DateOnly Date, Slot Slot);

public sealed class CancelRequestValidator : AbstractValidator<CancelRequest>
{
    public CancelRequestValidator() =>
        RuleFor(x => x.Reason).Must(r => r.Trim().Length is >= 3 and <= 300).WithMessage("Give a reason (3 to 300 characters).");
}

public static class AdminPickupEndpoints
{
    public static void MapAdminPickups(this RouteGroupBuilder admin)
    {
        var pickups = admin.MapGroup("/pickups");
        pickups.MapGet("", List);
        pickups.MapGet("/{id}", Get);
        pickups.MapPost("/{id}/cancel", Cancel).Validate<CancelRequest>();
        pickups.MapPost("/{id}/schedule", Schedule);
    }

    private static async Task<Ok<PagedList<PickupRow>>> List(string? status, DateOnly? date, string? source, string? q,
        string? sort, int? page, int? pageSize, AppDbContext db, Maintenance maintenance, CancellationToken ct)
    {
        await maintenance.RunAsync(ct);
        var query = db.Pickups.Include(p => p.Customer).Include(p => p.Collector).AsNoTracking();
        if (status is not null)
        {
            var s = CamelCaseEnumConverter<PickupStatus>.TryParse(status)
                    ?? throw ApiException.Validation("status", "That isn't a pickup status.");
            query = query.Where(p => p.Status == s);
        }

        if (source is not null)
        {
            var s = CamelCaseEnumConverter<PickupSource>.TryParse(source)
                    ?? throw ApiException.Validation("source", "Use app or callCentre.");
            query = query.Where(p => p.Source == s);
        }

        if (date is { } d) query = query.Where(p => p.ScheduledDate == d);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Id, term) || EF.Functions.ILike(p.Area, term) ||
                                     EF.Functions.ILike(p.Customer!.FirstName + " " + p.Customer.LastName, term) ||
                                     EF.Functions.ILike(p.Customer.Mobile ?? "", term) ||
                                     (p.Collector != null && EF.Functions.ILike(p.Collector.Code, term)));
        }

        query = (sort ?? "-createdAt") switch
        {
            "createdAt" => query.OrderBy(p => p.CreatedAt).ThenByDescending(p => p.Id),
            "-createdAt" => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
            "scheduledDate" => query.OrderBy(p => p.WindowStart).ThenByDescending(p => p.Id),
            "-scheduledDate" => query.OrderByDescending(p => p.WindowStart).ThenByDescending(p => p.Id),
            "estimatedKg" => query.OrderBy(p => p.EstimatedKg).ThenByDescending(p => p.Id),
            "-estimatedKg" => query.OrderByDescending(p => p.EstimatedKg).ThenByDescending(p => p.Id),
            _ => throw ApiException.Validation("sort", "Sort by createdAt, scheduledDate or estimatedKg."),
        };
        return TypedResults.Ok(await query.ToPageAsync(PageRequest.From(page, pageSize), PickupRow.From, ct));
    }

    private static async Task<Ok<PickupDetail>> Get(string id, AppDbContext db, Maintenance maintenance,
        CancellationToken ct)
    {
        await maintenance.RunAsync(ct);
        return TypedResults.Ok(PickupDetail.From(await FindAsync(db, id, ct)));
    }

    private static async Task<Ok<PickupDetail>> Cancel(string id, CancelRequest request, CurrentUser user,
        AppDbContext db, PickupWorkflow workflow, CancellationToken ct)
    {
        var staff = await user.StaffAsync(ct);
        await workflow.CancelAsync(await FindAsync(db, id, ct), request.Reason.Trim(), staff.Name, ct);
        db.ChangeTracker.Clear();
        return TypedResults.Ok(PickupDetail.From(await FindAsync(db, id, ct)));
    }

    /// <summary>
    /// Moves a pickup that's still waiting for a collector to another day or slot: the call centre agreed it with
    /// the customer. A collector taking it then confirms this date.
    /// </summary>
    private static async Task<Ok<PickupDetail>> Schedule(string id, ScheduleRequest request, AppDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var pickup = await FindAsync(db, id, ct);
        if (pickup.Status != PickupStatus.Pending)
            throw ApiException.Conflict(AssignmentService.PickupAlreadyAssigned, "This pickup already has a collector.");
        var (start, end) = PickupWorkflow.SlotHours(request.Slot);
        if (Colombo.At(request.Date, end) <= time.UtcNow())
            throw ApiException.Validation("date", "Choose a day and slot that haven't passed.");
        pickup.ScheduledDate = request.Date;
        pickup.Slot = request.Slot;
        pickup.WindowStart = Colombo.At(request.Date, start);
        pickup.WindowEnd = Colombo.At(request.Date, end);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(PickupDetail.From(pickup));
    }

    public static async Task<Pickup> FindAsync(AppDbContext db, string id, CancellationToken ct) =>
        await db.Pickups.Include(p => p.Customer).Include(p => p.Collector).Include(p => p.Hub)
            .Include(p => p.Items).Include(p => p.Events).Include(p => p.PreferredDates).AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == id, ct)
        ?? throw ApiException.NotFound(PickupWorkflow.PickupNotFound, "Pickup not found.");
}
