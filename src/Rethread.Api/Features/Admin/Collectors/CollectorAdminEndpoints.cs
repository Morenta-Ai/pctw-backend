using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Admin.Customers;
using Rethread.Api.Features.Collector.Load;

namespace Rethread.Api.Features.Admin.Collectors;

public sealed record HubRef(string Id, string Name);

public sealed record VehicleDto(VehicleType Type, string Plate, int CapacityKg);

public sealed record CollectorRow(
    string Id,
    string Code,
    string Name,
    string Mobile,
    AccountStatus Status,
    bool Online,
    string Area,
    HubRef Hub,
    VehicleDto Vehicle,
    decimal Rating,
    DateOnly MemberSince,
    decimal CollectedKgThisWeek);

public sealed record CollectorDetail(
    string Id,
    string Code,
    string Name,
    string Mobile,
    AccountStatus Status,
    bool Online,
    string Area,
    HubRef Hub,
    VehicleDto Vehicle,
    decimal Rating,
    DateOnly MemberSince,
    decimal CollectedKgThisWeek,
    decimal LoadKg,
    CollectorDetail.EarningTotals Earnings,
    IReadOnlyList<CollectorDetail.RecentJob> RecentJobs)
{
    public sealed record EarningTotals(long TodayCents, long WeekCents, long MonthCents);

    public sealed record RecentJob(string Id, PickupStatus Status, DateOnly ScheduledDate, string Area,
        decimal? ActualWeightKg, long? CollectorPayoutCents);
}

public sealed record NewCollectorRequest(string FirstName, string LastName, string Mobile, string Area, string HubId,
    VehicleDto Vehicle);

public sealed class NewCollectorRequestValidator : AbstractValidator<NewCollectorRequest>
{
    public NewCollectorRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Mobile).Mobile();
        RuleFor(x => x.Area).NotEmpty().MaximumLength(80);
        RuleFor(x => x.HubId).NotEmpty();
        RuleFor(x => x.Vehicle).NotNull();
        RuleFor(x => x.Vehicle.Plate).NotEmpty().MaximumLength(20).When(x => x.Vehicle is not null);
        RuleFor(x => x.Vehicle.CapacityKg).InclusiveBetween(10, 5000).When(x => x.Vehicle is not null);
    }
}

public static class CollectorAdminEndpoints
{
    private const string NotFoundCode = "COLLECTOR_NOT_FOUND";

    public static void MapAdminCollectors(this RouteGroupBuilder admin)
    {
        var collectors = admin.MapGroup("/collectors");
        collectors.MapGet("", List);
        collectors.MapGet("/{id}", Get);
        collectors.MapPost("", Create).Validate<NewCollectorRequest>();
        collectors.MapPost("/{id}/status", SetStatus).Validate<AccountStatusRequest>();
        admin.MapGet("/hubs", async Task<Ok<ItemList<HubDto>>> (AppDbContext db, CancellationToken ct) =>
            TypedResults.Ok(new ItemList<HubDto>((await db.Hubs.AsNoTracking().OrderBy(h => h.Name).ToListAsync(ct))
                .Select(h => new HubDto(h.Id, h.Code, h.Name, h.ClosesAt)).ToList())));
    }

    /// <summary>Online first, then by code.</summary>
    private static async Task<Ok<PagedList<CollectorRow>>> List(string? q, string? status, bool? online, int? page,
        int? pageSize, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var query = db.Collectors.Include(c => c.Hub).AsNoTracking();
        if (status is not null)
        {
            var s = CamelCaseEnumConverter<AccountStatus>.TryParse(status)
                    ?? throw ApiException.Validation("status", "Use active or suspended.");
            query = query.Where(c => c.Status == s);
        }

        if (online is { } o) query = query.Where(c => c.Online == o);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.Code, term) || EF.Functions.ILike(c.FirstName + " " + c.LastName, term) ||
                                     EF.Functions.ILike(c.Mobile, term) || EF.Functions.ILike(c.Area, term) ||
                                     EF.Functions.ILike(c.VehiclePlate, term));
        }

        var paging = PageRequest.From(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(c => c.Online).ThenBy(c => c.Code)
            .Skip(paging.Skip).Take(paging.PageSize).ToListAsync(ct);
        var weekStart = Colombo.StartOfDay(Colombo.StartOfWeek(time.Today()));
        var ids = rows.Select(c => c.Id).ToList();
        var week = await db.Pickups.Where(p => ids.Contains(p.CollectorId!) && p.CollectedAt >= weekStart)
            .GroupBy(p => p.CollectorId).Select(g => new { g.Key, Kg = g.Sum(p => p.ActualWeightKg ?? 0) }).ToListAsync(ct);
        return TypedResults.Ok(new PagedList<CollectorRow>(rows.Select(c =>
            Row(c, week.SingleOrDefault(w => w.Key == c.Id)?.Kg ?? 0)).ToList(), paging.Page, paging.PageSize, total));
    }

    private static async Task<Ok<CollectorDetail>> Get(string id, AppDbContext db, TimeProvider time,
        CancellationToken ct) => TypedResults.Ok(await DetailAsync(db, id, time, ct));

    private static async Task<Created<CollectorDetail>> Create(NewCollectorRequest request, AppDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var mobile = request.Mobile.Trim();
        if (await db.Collectors.AnyAsync(c => c.Mobile == mobile, ct))
        {
            throw ApiException.Conflict("MOBILE_IN_USE", "Another collector has this mobile number.",
                new Dictionary<string, string> { ["mobile"] = "Another collector has this mobile number." });
        }

        var hub = await db.Hubs.SingleOrDefaultAsync(h => h.Id == request.HubId, ct)
                  ?? throw ApiException.Validation("hubId", "Choose one of the hubs.");
        var area = Areas.Find(request.Area);
        var areaName = area?.Name ?? request.Area.Trim();
        var number = await db.NextAsync(Sequences.CollectorNumber, ct);
        var today = time.Today();
        var collector = new Data.Entities.Collector
        {
            Id = $"col_{number}",
            Code = $"COL-{number:0000}",
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Mobile = mobile,
            Area = areaName,
            Region = Areas.RegionFor(areaName),
            Suburbs = Areas.SuburbsFor(areaName),
            HubId = hub.Id,
            VehicleType = request.Vehicle.Type,
            VehiclePlate = request.Vehicle.Plate.Trim().ToUpperInvariant(),
            CapacityKg = request.Vehicle.CapacityKg,
            Latitude = area?.Latitude,
            Longitude = area?.Longitude,
            MemberSince = today,
            CreatedAt = time.UtcNow(),
        };
        db.Collectors.Add(collector);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/admin/collectors/{collector.Id}", await DetailAsync(db, collector.Id, time, ct));
    }

    /// <summary>Suspending takes them offline, withdraws their open offers and signs them out.</summary>
    private static async Task<Ok<CollectorDetail>> SetStatus(string id, AccountStatusRequest request, AppDbContext db,
        TokenService tokens, TimeProvider time, CancellationToken ct)
    {
        var collector = await db.Collectors.SingleOrDefaultAsync(c => c.Id == id, ct)
                        ?? throw ApiException.NotFound(NotFoundCode, "Collector not found.");
        collector.Status = request.Status;
        collector.StatusReason = request.Status == AccountStatus.Suspended ? request.Reason!.Trim() : null;
        if (request.Status == AccountStatus.Suspended)
        {
            collector.Online = false;
            var now = time.UtcNow();
            await db.Assignments.Where(a => a.CollectorId == id && a.Status == AssignmentStatus.Offered)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AssignmentStatus.Withdrawn)
                    .SetProperty(a => a.RespondedAt, now), ct);
            await tokens.RevokeAllAsync(Audience.Collector, id, ct);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailAsync(db, id, time, ct));
    }

    private static CollectorRow Row(Data.Entities.Collector c, decimal weekKg) => new(c.Id, c.Code, c.FullName, c.Mobile,
        c.Status, c.Online, c.Area, new HubRef(c.HubId, c.Hub?.Name ?? c.HubId),
        new VehicleDto(c.VehicleType, c.VehiclePlate, c.CapacityKg), c.Rating, c.MemberSince, weekKg);

    /// <summary>Earnings by the Colombo day the collector weighed; weeks start on Monday.</summary>
    private static async Task<CollectorDetail> DetailAsync(AppDbContext db, string id, TimeProvider time,
        CancellationToken ct)
    {
        var c = await db.Collectors.Include(x => x.Hub).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw ApiException.NotFound(NotFoundCode, "Collector not found.");
        var today = time.Today();
        var monthStart = Colombo.StartOfDay(new DateOnly(today.Year, today.Month, 1));
        var weekStart = Colombo.StartOfDay(Colombo.StartOfWeek(today));
        var dayStart = Colombo.StartOfDay(today);
        var from = weekStart < monthStart ? weekStart : monthStart;

        var weighed = await db.Pickups.AsNoTracking().Where(p => p.CollectorId == id && p.CollectedAt >= from)
            .Select(p => new { p.CollectedAt, Kg = p.ActualWeightKg ?? 0, Paid = p.CollectorPayoutCents ?? 0 }).ToListAsync(ct);
        var load = await db.Pickups.Where(p => p.CollectorId == id && p.Status == PickupStatus.Collected)
            .SumAsync(p => p.ActualWeightKg ?? 0, ct);
        var recent = await db.Pickups.AsNoTracking().Where(p => p.CollectorId == id)
            .OrderByDescending(p => p.WindowStart).ThenBy(p => p.Id).Take(10).ToListAsync(ct);

        var row = Row(c, weighed.Where(w => w.CollectedAt >= weekStart).Sum(w => w.Kg));
        return new CollectorDetail(row.Id, row.Code, row.Name, row.Mobile, row.Status, row.Online, row.Area, row.Hub,
            row.Vehicle, row.Rating, row.MemberSince, row.CollectedKgThisWeek, load,
            new CollectorDetail.EarningTotals(
                weighed.Where(w => w.CollectedAt >= dayStart).Sum(w => w.Paid),
                weighed.Where(w => w.CollectedAt >= weekStart).Sum(w => w.Paid),
                weighed.Where(w => w.CollectedAt >= monthStart).Sum(w => w.Paid)),
            recent.Select(p => new CollectorDetail.RecentJob(p.Id, p.Status, p.ScheduledDate, p.Area, p.ActualWeightKg,
                p.CollectorPayoutCents)).ToList());
    }
}
