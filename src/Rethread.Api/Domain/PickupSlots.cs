using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// The three daily pickup slots and whether each can still be chosen: it must start at least an hour from
/// now, fall within the booking window, and not be full (<c>Pickups:SlotCapacity</c> requests per slot).
/// </summary>
public sealed class PickupSlots(AppDbContext db, IConfiguration config, TimeProvider time)
{
    public sealed record SlotInfo(Slot Id, string Label, string Start, string End);

    public static readonly IReadOnlyList<SlotInfo> All =
    [
        new(Slot.Morning, "Morning", "08:00", "12:00"),
        new(Slot.Afternoon, "Afternoon", "12:00", "16:00"),
        new(Slot.Evening, "Evening", "16:00", "19:00"),
    ];

    public const int BookingDays = 14;
    private static readonly TimeSpan LeadTime = TimeSpan.FromHours(1);

    public static SlotInfo Info(Slot slot) => All.Single(s => s.Id == slot);

    private int Capacity => config.GetValue("Pickups:SlotCapacity", 40);

    /// <summary>Whether each slot from <paramref name="from"/> for <paramref name="days"/> days can be chosen.</summary>
    public async Task<Dictionary<(DateOnly Date, Slot Slot), bool>> AvailabilityAsync(DateOnly from, int days,
        CancellationToken ct)
    {
        var to = from.AddDays(days - 1);
        var booked = await db.Pickups
            .Where(p => p.ScheduledDate >= from && p.ScheduledDate <= to && p.Status != PickupStatus.Cancelled)
            .GroupBy(p => new { p.ScheduledDate, p.Slot }).Select(g => new { g.Key.ScheduledDate, g.Key.Slot, Count = g.Count() })
            .ToListAsync(ct);
        var today = time.Today();
        var now = time.UtcNow();
        var result = new Dictionary<(DateOnly, Slot), bool>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            foreach (var slot in All)
            {
                var start = Colombo.At(date, int.Parse(slot.Start[..2], System.Globalization.CultureInfo.InvariantCulture));
                var count = booked.SingleOrDefault(b => b.ScheduledDate == date && b.Slot == slot.Id)?.Count ?? 0;
                result[(date, slot.Id)] = date >= today && date < today.AddDays(BookingDays) &&
                                          start - LeadTime > now && count < Capacity;
            }
        }

        return result;
    }
}
