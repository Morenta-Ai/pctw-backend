using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>
/// Which rates apply when. The rate set in force is the latest whose <c>effectiveFrom</c> is today or
/// earlier (Colombo); at most one later set is waiting as the scheduled change.
/// </summary>
public sealed class RateBook(AppDbContext db, TimeProvider time)
{
    public async Task<RateSet> CurrentAsync(CancellationToken ct)
    {
        var today = time.Today();
        return await db.RateSets.Where(r => r.EffectiveFrom <= today)
                   .OrderByDescending(r => r.EffectiveFrom).FirstOrDefaultAsync(ct)
               ?? throw new InvalidOperationException("No rate set is in force. Seed the rates first.");
    }

    public async Task<RateSet?> ScheduledAsync(CancellationToken ct)
    {
        var today = time.Today();
        return await db.RateSets.Where(r => r.EffectiveFrom > today)
            .OrderBy(r => r.EffectiveFrom).FirstOrDefaultAsync(ct);
    }

    /// <summary>Earlier rate sets, newest first, not including the current one.</summary>
    public async Task<List<RateSet>> HistoryAsync(RateSet current, CancellationToken ct) =>
        await db.RateSets.Where(r => r.EffectiveFrom < current.EffectiveFrom)
            .OrderByDescending(r => r.EffectiveFrom).ToListAsync(ct);
}
