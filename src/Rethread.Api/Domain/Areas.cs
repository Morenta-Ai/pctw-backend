using Rethread.Api.Common;

namespace Rethread.Api.Domain;

/// <summary>Colombo-area suburbs ReThread serves, with real coordinates and the collector regions.</summary>
public static class Areas
{
    public sealed record Area(string Name, double Latitude, double Longitude, string Region);

    public static readonly IReadOnlyList<Area> All =
    [
        new("Dehiwala", 6.8511, 79.8655, "Colombo South"),
        new("Wellawatte", 6.8742, 79.8606, "Colombo South"),
        new("Bambalapitiya", 6.8935, 79.8553, "Colombo Central"),
        new("Kollupitiya", 6.91, 79.85, "Colombo Central"),
        new("Nugegoda", 6.8649, 79.8997, "Colombo East"),
        new("Rajagiriya", 6.9094, 79.894, "Colombo East"),
        new("Battaramulla", 6.899, 79.918, "Colombo East"),
        new("Mount Lavinia", 6.839, 79.865, "Colombo South"),
        new("Kotte", 6.8868, 79.9187, "Colombo East"),
        new("Borella", 6.9147, 79.8778, "Colombo Central"),
        new("Maharagama", 6.848, 79.926, "Colombo East"),
        new("Wattala", 6.989, 79.892, "Colombo North"),
    ];

    public static Area? Find(string name) =>
        All.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The suburb for a pinned location: the nearest known suburb within 4 km, otherwise the last part of
    /// the address line (<c>…, Kandy</c> → <c>Kandy</c>).
    /// </summary>
    public static string NameFor(double latitude, double longitude, string addressLine)
    {
        var nearest = All.MinBy(a => Geo.DistanceKm(a.Latitude, a.Longitude, latitude, longitude))!;
        if (Geo.DistanceKm(nearest.Latitude, nearest.Longitude, latitude, longitude) <= 4) return nearest.Name;
        var last = addressLine.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        return string.IsNullOrEmpty(last) ? nearest.Name : last;
    }

    public static string RegionFor(string areaName) => Find(areaName)?.Region ?? "Colombo";

    /// <summary>The suburbs a collector based in <paramref name="areaName"/> covers: their region's.</summary>
    public static List<string> SuburbsFor(string areaName)
    {
        var region = RegionFor(areaName);
        var suburbs = All.Where(a => a.Region == region).Select(a => a.Name).ToList();
        return suburbs.Count > 0 ? suburbs : [areaName];
    }
}
