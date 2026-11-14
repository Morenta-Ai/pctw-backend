using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Admin.Dispatch;

public sealed record DispatchDto(
    DateTime GeneratedAt,
    IReadOnlyList<DispatchDto.CollectorPin> Collectors,
    IReadOnlyList<DispatchDto.PickupPin> Pickups,
    IReadOnlyList<DispatchDto.HubPin> Hubs)
{
    public sealed record Vehicle(VehicleType Type, string Plate, int CapacityKg);

    public sealed record CollectorPin(string Id, string Code, string Name, string Mobile, bool Online,
        double? Latitude, double? Longitude, DateTime? LastSeenAt, string Area, Vehicle Vehicle, decimal LoadKg,
        string? ActivePickupId);

    public sealed record Offer(string AssignmentId, string CollectorId, string CollectorName, DateTime ExpiresAt);

    public sealed record PickupPin(string Id, PickupStatus Status, PickupSource Source, string CustomerName,
        string Area, double Latitude, double Longitude, decimal EstimatedKg, DateTime WindowStart, DateTime WindowEnd,
        DateTime CreatedAt, string? CollectorId, Offer? Offer);

    public sealed record HubPin(string Id, string Code, string Name, double Latitude, double Longitude);
}

/// <summary>Everything the live map shows, in one call. The portal polls it every 10 seconds.</summary>
public static class DispatchEndpoints
{
    public static void MapDispatch(this RouteGroupBuilder admin) => admin.MapGet("/dispatch", Get);

    private static async Task<Ok<DispatchDto>> Get(AppDbContext db, Maintenance maintenance, TimeProvider time,
        CancellationToken ct)
    {
        await maintenance.RunAsync(ct);
        var now = time.UtcNow();
        var today = Colombo.DateOf(now);

        var collectors = await db.Collectors.AsNoTracking().Where(c => c.Status == AccountStatus.Active)
            .OrderBy(c => c.Code).ToListAsync(ct);
        var working = await db.Pickups.AsNoTracking()
            .Where(p => p.CollectorId != null && (p.Status == PickupStatus.Assigned || p.Status == PickupStatus.EnRoute ||
                                                  p.Status == PickupStatus.Arrived || p.Status == PickupStatus.Collected))
            .Select(p => new { p.CollectorId, p.Id, p.Status, Kg = p.ActualWeightKg ?? 0, p.WindowStart })
            .ToListAsync(ct);
        var open = await db.Pickups.Include(p => p.Customer).AsNoTracking()
            .Where(p => PickupWorkflow.Open.Contains(p.Status) && p.ScheduledDate >= today)
            .OrderBy(p => p.WindowStart).ThenBy(p => p.Id).ToListAsync(ct);
        var offers = await db.Assignments.Include(a => a.Collector).AsNoTracking()
            .Where(a => a.Status == AssignmentStatus.Offered).ToListAsync(ct);
        var hubs = await db.Hubs.AsNoTracking().OrderBy(h => h.Name).ToListAsync(ct);

        return TypedResults.Ok(new DispatchDto(
            now,
            collectors.Select(c =>
            {
                var mine = working.Where(w => w.CollectorId == c.Id).ToList();
                var active = mine.Where(w => w.Status != PickupStatus.Collected)
                    .OrderByDescending(w => w.Status).ThenBy(w => w.WindowStart).FirstOrDefault();
                return new DispatchDto.CollectorPin(c.Id, c.Code, c.FullName, c.Mobile, c.Online, c.Latitude,
                    c.Longitude, c.LastSeenAt, c.Area, new DispatchDto.Vehicle(c.VehicleType, c.VehiclePlate, c.CapacityKg),
                    mine.Where(w => w.Status == PickupStatus.Collected).Sum(w => w.Kg), active?.Id);
            }).ToList(),
            open.Select(p =>
            {
                var offer = offers.FirstOrDefault(o => o.PickupId == p.Id);
                return new DispatchDto.PickupPin(p.Id, p.Status, p.Source, p.Customer!.FullName, p.Area, p.Latitude,
                    p.Longitude, p.EstimatedKg, p.WindowStart, p.WindowEnd, p.CreatedAt, p.CollectorId,
                    offer is null ? null : new DispatchDto.Offer(offer.Id, offer.CollectorId, offer.Collector!.FullName,
                        offer.ExpiresAt));
            }).ToList(),
            hubs.Select(h => new DispatchDto.HubPin(h.Id, h.Code, h.Name, h.Latitude, h.Longitude)).ToList()));
    }
}
