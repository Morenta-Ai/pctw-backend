using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Collector.Profile;

/// <summary><c>GET /me</c> in the collector contract.</summary>
public sealed record CollectorMe(
    string Id,
    string CollectorCode,
    string FirstName,
    string LastName,
    string Mobile,
    DateOnly MemberSince,
    Language Language,
    bool Online,
    int RadiusKm,
    CollectorMe.VehicleInfo Vehicle,
    CollectorMe.ServiceArea Area,
    CollectorMe.ScaleInfo? Scale,
    long CustomerRatePerKgCents,
    long CollectorRatePerKgCents)
{
    public sealed record VehicleInfo(VehicleType Type, string Plate, int CapacityKg);

    public sealed record ServiceArea(string Name, IReadOnlyList<string> Suburbs);

    public sealed record ScaleInfo(string Name, bool Paired, bool AutoConnect);

    public static CollectorMe From(Data.Entities.Collector c, RateSet rates) => new(
        c.Id, c.Code, c.FirstName, c.LastName, c.Mobile, c.MemberSince, c.Language, c.Online, c.RadiusKm,
        new VehicleInfo(c.VehicleType, c.VehiclePlate, c.CapacityKg),
        new ServiceArea(c.Region, c.Suburbs),
        c.ScaleName is null ? null : new ScaleInfo(c.ScaleName, c.ScalePaired, c.ScaleAutoConnect),
        rates.CustomerRatePerKgCents, rates.CollectorRatePerKgCents);
}
