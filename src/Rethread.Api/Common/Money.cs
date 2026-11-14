using System.Globalization;

namespace Rethread.Api.Common;

public static class Money
{
    /// <summary>Weight × rate in cents, rounded half away from zero. Money never goes through a float.</summary>
    public static long Payout(decimal weightKg, long ratePerKgCents) =>
        (long)Math.Round(weightKg * ratePerKgCents, MidpointRounding.AwayFromZero);

    /// <summary><c>Rs. 1,234.00</c>, for server-written text such as notification bodies.</summary>
    public static string Format(long cents) =>
        string.Create(CultureInfo.InvariantCulture, $"Rs. {cents / 100m:#,##0.00}");
}

public static class Weight
{
    public static decimal Round(decimal kg) => Math.Round(kg, 2, MidpointRounding.AwayFromZero);

    /// <summary><c>8.0 kg</c>: one decimal, for server-written text.</summary>
    public static string Format(decimal kg) => string.Create(CultureInfo.InvariantCulture, $"{kg:0.0} kg");
}
