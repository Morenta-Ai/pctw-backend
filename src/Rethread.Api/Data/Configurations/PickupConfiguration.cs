using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Data.Configurations;

public sealed class PickupConfiguration : IEntityTypeConfiguration<Pickup>
{
    public void Configure(EntityTypeBuilder<Pickup> b)
    {
        b.Property(x => x.Id).HasMaxLength(20);
        b.Property(x => x.AddressLine).HasMaxLength(200);
        b.Property(x => x.Area).HasMaxLength(80);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => new { x.Status, x.ScheduledDate });
        b.HasIndex(x => new { x.CustomerId, x.CreatedAt });
        b.HasIndex(x => new { x.CollectorId, x.Status });
        b.HasIndex(x => x.CreatedAt);
        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Collector).WithMany().HasForeignKey(x => x.CollectorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Hub).WithMany().HasForeignKey(x => x.HubId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PickupId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.PickupId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.PreferredDates).WithOne().HasForeignKey(x => x.PickupId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.CustomerCancelReason).HasMaxLength(20);
    }
}

public sealed class PickupPreferredDateConfiguration : IEntityTypeConfiguration<PickupPreferredDate>
{
    public void Configure(EntityTypeBuilder<PickupPreferredDate> b)
    {
        b.HasKey(x => new { x.PickupId, x.Priority });
        b.HasIndex(x => new { x.Date, x.Slot });
    }
}

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records");
        b.HasKey(x => new { x.CustomerId, x.Scope, x.Key });
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.RequestHash).HasMaxLength(64);
    }
}

public sealed class PickupItemConfiguration : IEntityTypeConfiguration<PickupItem>
{
    public void Configure(EntityTypeBuilder<PickupItem> b) => b.Property(x => x.Id).HasMaxLength(40);
}

public sealed class PickupEventConfiguration : IEntityTypeConfiguration<PickupEvent>
{
    public void Configure(EntityTypeBuilder<PickupEvent> b)
    {
        b.HasIndex(x => new { x.PickupId, x.OccurredAt });
        b.HasIndex(x => new { x.Status, x.OccurredAt });
    }
}

public sealed class PickupPhotoConfiguration : IEntityTypeConfiguration<PickupPhoto>
{
    public void Configure(EntityTypeBuilder<PickupPhoto> b) => b.HasIndex(x => x.PickupId);
}

public sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.Property(x => x.Version).IsRowVersion();
        // Only one open offer per pickup, enforced by the database as well as the service.
        b.HasIndex(x => x.PickupId).IsUnique().HasFilter("status = 'offered'")
            .HasDatabaseName("ix_assignments_one_open_offer");
        b.HasIndex(x => new { x.CollectorId, x.Status });
        b.HasIndex(x => new { x.Status, x.ExpiresAt });
        b.HasOne(x => x.Pickup).WithMany().HasForeignKey(x => x.PickupId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Collector).WithMany().HasForeignKey(x => x.CollectorId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class HubDeliveryConfiguration : IEntityTypeConfiguration<HubDelivery>
{
    public void Configure(EntityTypeBuilder<HubDelivery> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.HasIndex(x => new { x.CollectorId, x.CreatedAt });
    }
}
