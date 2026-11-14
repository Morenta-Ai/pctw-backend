using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;
using Rethread.Api.Features.Collector.Jobs;

namespace Rethread.Api.Features.Collector.Load;

public sealed record LoadDto(decimal WeightKg, int CapacityKg, string VehiclePlate, IReadOnlyList<LoadDto.Item> Items)
{
    public sealed record Item(string JobId, string Area, DateTime CollectedAt, decimal WeightKg);
}

public sealed record HubDto(string Id, string Code, string Name, string ClosesAt);

public sealed record HubDeliveryRequest(string HubId, IReadOnlyList<string> JobIds);

public sealed record HubReceipt(string ReceiptId, string HubName, int JobCount, decimal WeightKg, int CustomersNotified);

public sealed class HubDeliveryRequestValidator : AbstractValidator<HubDeliveryRequest>
{
    public HubDeliveryRequestValidator()
    {
        RuleFor(x => x.HubId).NotEmpty();
        RuleFor(x => x.JobIds).NotEmpty().WithMessage("Choose the jobs you're dropping off.");
    }
}

public static class LoadEndpoints
{
    public static void MapLoad(this RouteGroupBuilder api)
    {
        api.MapGet("/load", GetLoad);
        api.MapGet("/hubs", Hubs);
        api.MapPost("/hub-deliveries", Deliver).Validate<HubDeliveryRequest>();
    }

    private static async Task<Ok<LoadDto>> GetLoad(CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        var jobs = await db.Pickups.Where(p => p.CollectorId == collector.Id && p.Status == PickupStatus.Collected)
            .OrderBy(p => p.CollectedAt).ToListAsync(ct);
        return TypedResults.Ok(new LoadDto(jobs.Sum(j => j.ActualWeightKg ?? 0), collector.CapacityKg,
            collector.VehiclePlate,
            jobs.Select(j => new LoadDto.Item(j.Id, j.Area, j.CollectedAt!.Value, j.ActualWeightKg ?? 0)).ToList()));
    }

    /// <summary>Hubs, nearest first.</summary>
    private static async Task<Ok<ItemList<HubDto>>> Hubs(CurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        var (lat, lng) = JobMapper.Position(collector);
        var hubs = await db.Hubs.ToListAsync(ct);
        return TypedResults.Ok(new ItemList<HubDto>(hubs
            .OrderBy(h => Geo.DistanceKm(lat, lng, h.Latitude, h.Longitude))
            .Select(h => new HubDto(h.Id, h.Code, h.Name, h.ClosesAt)).ToList()));
    }

    private static async Task<Ok<HubReceipt>> Deliver(HubDeliveryRequest request, CurrentUser user, AppDbContext db,
        PickupWorkflow workflow, CancellationToken ct)
    {
        var collector = await user.CollectorAsync(ct);
        var hub = await db.Hubs.SingleOrDefaultAsync(h => h.Id == request.HubId, ct)
                  ?? throw ApiException.Validation("hubId", "Choose one of the hubs.");
        var delivery = await workflow.DeliverToHubAsync(collector, hub, request.JobIds, ct);
        return TypedResults.Ok(new HubReceipt(delivery.Receipt.Id, hub.Name, delivery.Receipt.JobCount,
            delivery.Receipt.WeightKg, delivery.CustomersNotified));
    }
}
