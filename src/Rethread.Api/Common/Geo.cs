namespace Rethread.Api.Common;

public static class Geo
{
    /// <summary>Roads are longer than a straight line; 1.3 is a common urban detour factor.</summary>
    private const double RoadFactor = 1.3;

    /// <summary>Average speed in Colombo traffic, for ETAs.</summary>
    private const double AverageKmPerHour = 20;

    /// <summary>Road distance estimate in km, to 1 decimal.</summary>
    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthKm = 6371;
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var straight = 2 * earthKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
        return Math.Round(straight * RoadFactor, 1);
    }

    public static int EtaMinutes(double distanceKm) =>
        Math.Max(1, (int)Math.Ceiling(distanceKm / AverageKmPerHour * 60));

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
