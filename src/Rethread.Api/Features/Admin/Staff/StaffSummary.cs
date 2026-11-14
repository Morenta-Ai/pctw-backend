using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Admin.Staff;

/// <summary>The <c>staff</c> object from login and <c>GET /admin/me</c>.</summary>
public sealed record StaffSummary(string Id, string Name, string Email, StaffRole Role)
{
    public static StaffSummary From(Data.Entities.Staff s) => new(s.Id, s.Name, s.Email, s.Role);
}
