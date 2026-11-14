namespace Rethread.Api.Data.Entities;

/// <summary>A user-app account. Names and contacts are null until sign-up finishes.</summary>
public sealed class Customer
{
    public required string Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public Language Language { get; set; } = Language.En;
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public string? StatusReason { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Address> Addresses { get; set; } = [];
    public List<FamilyMember> FamilyMembers { get; set; } = [];
    public BankDetails? BankDetails { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public sealed class Address
{
    public required string Id { get; set; }
    public required string CustomerId { get; set; }
    public AddressLabel Label { get; set; }
    public required string Line { get; set; }
    /// <summary>The suburb, for the portal's tables and the collector's job card.</summary>
    public required string Area { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class FamilyMember
{
    public required string Id { get; set; }
    public required string CustomerId { get; set; }
    public required string Name { get; set; }
    public FamilyRelation Relation { get; set; }
    public Gender Gender { get; set; }
    public AgeGroup AgeGroup { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? ChestCm { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? HipCm { get; set; }
    public string? ShoeSize { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Where cash redemptions go. The account number is encrypted with Data Protection.</summary>
public sealed class BankDetails
{
    public required string CustomerId { get; set; }
    public required string BankName { get; set; }
    public required string Branch { get; set; }
    public required string AccountName { get; set; }
    public required string AccountNumberProtected { get; set; }
    public required string AccountLast4 { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class Collector
{
    public required string Id { get; set; }
    /// <summary><c>COL-0457</c>: with the mobile number, what the collector signs in with.</summary>
    public required string Code { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Mobile { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public string? StatusReason { get; set; }
    public bool Online { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public int RadiusKm { get; set; } = 5;
    public VehicleType VehicleType { get; set; }
    public required string VehiclePlate { get; set; }
    public int CapacityKg { get; set; }
    /// <summary>Home suburb, as the portal shows it (<c>Dehiwala</c>).</summary>
    public required string Area { get; set; }
    /// <summary>Service region, as the collector app shows it (<c>Colombo South</c>).</summary>
    public required string Region { get; set; }
    public List<string> Suburbs { get; set; } = [];
    public required string HubId { get; set; }
    public string? ScaleName { get; set; }
    public bool ScalePaired { get; set; }
    public bool ScaleAutoConnect { get; set; } = true;
    public Language Language { get; set; } = Language.En;
    public decimal Rating { get; set; } = 5.0m;
    public DateOnly MemberSince { get; set; }
    public DateTime CreatedAt { get; set; }

    public Hub? Hub { get; set; }

    public string FullName => $"{FirstName} {LastName}";
}

public sealed class Staff
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public StaffRole Role { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public string? PasswordHash { get; set; }
    public string? InviteTokenHash { get; set; }
    public DateTime? InviteExpiresAt { get; set; }
    public DateTime? LastSignInAt { get; set; }
    /// <summary>"Mark all read": every staff notification up to this time counts as read.</summary>
    public DateTime? NotificationsReadBefore { get; set; }
    public DateTime CreatedAt { get; set; }
}
