using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Data.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.HasKey(x => x.Token);
        b.Property(x => x.Token).HasMaxLength(512);
        b.HasIndex(x => x.CustomerId);
    }
}

public sealed class NotificationPreferencesConfiguration : IEntityTypeConfiguration<NotificationPreferences>
{
    public void Configure(EntityTypeBuilder<NotificationPreferences> b) => b.HasKey(x => x.CustomerId);
}

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.Property(x => x.Message).HasMaxLength(4000);
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => x.CustomerId);
    }
}
