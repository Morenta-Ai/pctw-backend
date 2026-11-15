using Rethread.Api.Data.Entities;
using Rethread.Api.Domain;

namespace Rethread.Api.Features.Customer.Profile;

/// <summary><c>GET /me</c> in the user contract. <c>fullName</c> is null until the profile step is saved.</summary>
public sealed record CustomerMe(
    string Id,
    string? FullName,
    string? Mobile,
    string? Email,
    Language PreferredLanguage,
    bool HasBankDetails,
    DateTime CreatedAt,
    string? AvatarUrl)
{
    public static CustomerMe From(Data.Entities.Customer c, bool hasBankDetails, PublicUrls urls) =>
        new(c.Id, c.FirstName is null ? null : c.FullName, c.Mobile, c.Email, c.Language, hasBankDetails, c.CreatedAt,
            urls.File(c.AvatarFileId));
}
