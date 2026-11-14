using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Customer.Profile;

/// <summary><c>GET /me</c> in the user contract. Names and contacts are null until sign-up finishes.</summary>
public sealed record CustomerMe(
    string Id,
    string? FirstName,
    string? LastName,
    string? Mobile,
    string? Email,
    Language Language,
    bool HasBankDetails,
    DateTime CreatedAt)
{
    public static CustomerMe From(Data.Entities.Customer c, bool hasBankDetails) =>
        new(c.Id, c.FirstName, c.LastName, c.Mobile, c.Email, c.Language, hasBankDetails, c.CreatedAt);
}
