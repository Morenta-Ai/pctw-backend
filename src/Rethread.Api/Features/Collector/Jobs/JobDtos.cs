using Rethread.Api.Common;
using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Collector.Jobs;

/// <summary>The collector app's names for a pickup's status.</summary>
public enum JobStatus { Open, Accepted, OnTheWay, Arrived, Collected, DeliveredToHub, Cancelled }

/// <summary>One job on the board (<c>GET /jobs</c> in the collector contract).</summary>
public sealed record JobSummary(
    string Id,
    string CustomerFirstName,
    string Area,
    double DistanceKm,
    int EtaMinutes,
    DateTime WindowStart,
    DateTime WindowEnd,
    Slot Slot,
    PickupMethod Type,
    decimal EstimatedKg,
    int? ItemCount,
    int BagCount,
    PickupSource Source,
    double Latitude,
    double Longitude);

/// <summary>A job in full (<c>GET /jobs/{id}</c>). Contact details only once it's this collector's.</summary>
public sealed record JobDetail(
    string Id,
    JobStatus Status,
    string CustomerFirstName,
    string? CustomerName,
    string? CustomerPhone,
    string Area,
    string Address,
    double DistanceKm,
    int EtaMinutes,
    DateTime WindowStart,
    DateTime WindowEnd,
    Slot Slot,
    PickupMethod Type,
    decimal EstimatedKg,
    int? ItemCount,
    int BagCount,
    PickupSource Source,
    double Latitude,
    double Longitude,
    string? Note,
    IReadOnlyList<JobDetail.Item> Items)
{
    public sealed record Item(string Id, string Name, string? Size, int? Quantity, string? BagLabel,
        ItemCondition Condition);
}

public static class JobMapper
{
    public static JobStatus StatusFor(PickupStatus status) => status switch
    {
        PickupStatus.Pending => JobStatus.Open,
        PickupStatus.Assigned => JobStatus.Accepted,
        PickupStatus.EnRoute => JobStatus.OnTheWay,
        PickupStatus.Arrived => JobStatus.Arrived,
        PickupStatus.Collected => JobStatus.Collected,
        PickupStatus.DeliveredToHub => JobStatus.DeliveredToHub,
        _ => JobStatus.Cancelled,
    };

    /// <summary>Where the collector is, for distances: their last reported position, else their home suburb.</summary>
    public static (double Latitude, double Longitude) Position(Data.Entities.Collector c)
    {
        if (c is { Latitude: { } lat, Longitude: { } lng }) return (lat, lng);
        var area = Areas.Find(c.Area);
        return area is null ? (6.9271, 79.8612) : (area.Latitude, area.Longitude);
    }

    public static double DistanceKm(Data.Entities.Collector c, Pickup p)
    {
        var (lat, lng) = Position(c);
        return Geo.DistanceKm(lat, lng, p.Latitude, p.Longitude);
    }

    public static JobSummary Summary(Pickup p, Data.Entities.Collector c)
    {
        var km = DistanceKm(c, p);
        return new JobSummary(p.Id, FirstName(p), p.Area, km, Geo.EtaMinutes(km), p.WindowStart, p.WindowEnd,
            p.Slot, p.Method, p.EstimatedKg, p.ItemCount, p.BagCount, p.Source, p.Latitude, p.Longitude);
    }

    public static JobDetail Detail(Pickup p, Data.Entities.Collector c)
    {
        var km = DistanceKm(c, p);
        var mine = p.CollectorId == c.Id;
        return new JobDetail(p.Id, StatusFor(p.Status), FirstName(p),
            mine ? p.Customer?.FullName : null, mine ? p.Customer?.Mobile : null,
            p.Area, p.AddressLine, km, Geo.EtaMinutes(km), p.WindowStart, p.WindowEnd, p.Slot, p.Method,
            p.EstimatedKg, p.ItemCount, p.BagCount, p.Source, p.Latitude, p.Longitude, p.Note,
            p.Items.OrderBy(i => i.SortOrder)
                .Select(i => new JobDetail.Item(i.Id, i.Name, i.Size, i.Quantity, i.BagLabel, i.Condition))
                .ToList());
    }

    private static string FirstName(Pickup p) => p.Customer?.FirstName ?? "Customer";
}
