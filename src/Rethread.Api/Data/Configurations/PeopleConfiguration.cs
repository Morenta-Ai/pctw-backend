using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Data.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.Property(x => x.FirstName).HasMaxLength(60);
        b.Property(x => x.LastName).HasMaxLength(60);
        b.Property(x => x.Mobile).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(254);
        b.HasIndex(x => x.Mobile).IsUnique().HasFilter("mobile IS NOT NULL");
        b.HasIndex(x => x.Email).IsUnique().HasFilter("email IS NOT NULL");
        b.HasIndex(x => x.CreatedAt);
        b.Ignore(x => x.FullName);
        b.HasMany(x => x.Addresses).WithOne().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.FamilyMembers).WithOne().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.BankDetails).WithOne().HasForeignKey<BankDetails>(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.Property(x => x.Line).HasMaxLength(200);
        b.Property(x => x.Area).HasMaxLength(80);
        b.HasIndex(x => x.CustomerId).IsUnique().HasFilter("is_default").HasDatabaseName("ix_addresses_one_default");
    }
}

public sealed class FamilyMemberConfiguration : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.Property(x => x.Name).HasMaxLength(80);
        b.Property(x => x.ShoeSize).HasMaxLength(12);
        b.Property(x => x.HeightCm).HasPrecision(5, 1);
        b.Property(x => x.ChestCm).HasPrecision(5, 1);
        b.Property(x => x.WaistCm).HasPrecision(5, 1);
        b.Property(x => x.HipCm).HasPrecision(5, 1);
        b.HasIndex(x => x.CustomerId);
    }
}

public sealed class BankDetailsConfiguration : IEntityTypeConfiguration<BankDetails>
{
    public void Configure(EntityTypeBuilder<BankDetails> b)
    {
        b.HasKey(x => x.CustomerId);
        b.Property(x => x.AccountNumberProtected).HasMaxLength(1000);
        b.Property(x => x.AccountLast4).HasMaxLength(4);
    }
}

public sealed class CollectorConfiguration : IEntityTypeConfiguration<Collector>
{
    public void Configure(EntityTypeBuilder<Collector> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.Property(x => x.Code).HasMaxLength(12);
        b.Property(x => x.Mobile).HasMaxLength(20);
        b.Property(x => x.Rating).HasPrecision(2, 1);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.Mobile).IsUnique();
        b.Ignore(x => x.FullName);
        b.HasOne(x => x.Hub).WithMany().HasForeignKey(x => x.HubId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> b)
    {
        b.ToTable("staff");
        b.Property(x => x.Id).HasMaxLength(40);
        b.Property(x => x.Email).HasMaxLength(254);
        b.HasIndex(x => x.Email).IsUnique();
        b.HasIndex(x => x.InviteTokenHash).IsUnique().HasFilter("invite_token_hash IS NOT NULL");
    }
}

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.HasIndex(x => new { x.Audience, x.Destination, x.CreatedAt });
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.Audience, x.SubjectId });
    }
}
