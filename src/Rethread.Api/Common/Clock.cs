namespace Rethread.Api.Common;

public static class Clock
{
    /// <summary>Now in UTC, to the microsecond (what PostgreSQL keeps).</summary>
    public static DateTime UtcNow(this TimeProvider time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }
}

/// <summary>Calendar days and times in Asia/Colombo (UTC+05:30, no daylight saving).</summary>
public static class Colombo
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

    public static DateOnly Today(this TimeProvider time) => DateOf(time.UtcNow());

    public static DateOnly DateOf(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(UtcDateTimeConverter.ToUtc(utc), Zone));

    public static DateTime LocalTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(UtcDateTimeConverter.ToUtc(utc), Zone);

    /// <summary>The UTC instant of <paramref name="hour"/>:<paramref name="minute"/> in Colombo on a day.</summary>
    public static DateTime At(DateOnly date, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), Zone);

    public static DateTime StartOfDay(DateOnly date) => At(date, 0);

    /// <summary>Monday of the week <paramref name="date"/> falls in.</summary>
    public static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
