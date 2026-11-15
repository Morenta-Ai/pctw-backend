namespace Rethread.Api.Data.Entities;

/// <summary>A one-time code sent by SMS or email. Only the code's hash is stored.</summary>
public sealed class OtpChallenge
{
    public required string Id { get; set; }
    public Audience Audience { get; set; }
    /// <summary><c>signIn</c>, or <c>contactChange</c> for a code sent to a new mobile or email.</summary>
    public string Purpose { get; set; } = "signIn";
    /// <summary><c>mobile</c> or <c>email</c>.</summary>
    public required string Channel { get; set; }
    public required string Destination { get; set; }
    /// <summary>The collector the code is for; customers are found or created on verification.</summary>
    public string? SubjectId { get; set; }
    public required string CodeHash { get; set; }
    public int Attempts { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime ResendAfter { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A refresh token, stored as a SHA-256 hash and rotated on every use.</summary>
public sealed class RefreshToken
{
    public required string Id { get; set; }
    public required string TokenHash { get; set; }
    public Audience Audience { get; set; }
    public required string SubjectId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedById { get; set; }
}
