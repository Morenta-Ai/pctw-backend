namespace Rethread.Api.Data.Entities;

/// <summary>
/// A create request the app may retry with the same <c>Idempotency-Key</c>: the repeat gets the first
/// result instead of a second pickup or order.
/// </summary>
public sealed class IdempotencyRecord
{
    public required string CustomerId { get; set; }
    /// <summary>What was created: <c>pickup</c> or <c>order</c>.</summary>
    public required string Scope { get; set; }
    public required string Key { get; set; }
    public required string RequestHash { get; set; }
    public required string ResourceId { get; set; }
    public DateTime CreatedAt { get; set; }
}
