using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary><c>Idempotency-Key</c> handling for the user app's create calls.</summary>
public static class Idempotency
{
    /// <summary>
    /// The ID created by an earlier request with this key, or null for a new one. The same key with a different
    /// body is 409 <c>DUPLICATE_REQUEST</c>.
    /// </summary>
    public static async Task<string?> FindAsync(AppDbContext db, string customerId, string scope, string key,
        string requestHash, CancellationToken ct)
    {
        var earlier = await db.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.CustomerId == customerId && r.Scope == scope && r.Key == key, ct);
        if (earlier is null) return null;
        if (earlier.RequestHash != requestHash)
            throw ApiException.Conflict("DUPLICATE_REQUEST", "That Idempotency-Key was used for a different request.");
        return earlier.ResourceId;
    }

    public static async Task SaveAsync(AppDbContext db, string customerId, string scope, string key, string requestHash,
        string resourceId, TimeProvider time, CancellationToken ct)
    {
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            CustomerId = customerId,
            Scope = scope,
            Key = key,
            RequestHash = requestHash,
            ResourceId = resourceId,
            CreatedAt = time.UtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }
}
