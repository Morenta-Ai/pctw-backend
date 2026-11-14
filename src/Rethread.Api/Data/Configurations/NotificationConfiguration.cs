using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Data.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x => x.Id).HasMaxLength(80);
        b.Property(x => x.Type).HasMaxLength(40);
        b.Property(x => x.TargetType).HasMaxLength(40);
        b.HasIndex(x => new { x.Audience, x.RecipientId, x.CreatedAt });
    }
}

public sealed class StaffNotificationReadConfiguration : IEntityTypeConfiguration<StaffNotificationRead>
{
    public void Configure(EntityTypeBuilder<StaffNotificationRead> b)
    {
        b.HasKey(x => new { x.StaffId, x.NotificationId });
        b.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Staff>().WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.Cascade);
    }
}
