using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Data.Configurations;

public sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.HasIndex(x => new { x.CustomerId, x.OccurredAt });
        b.HasIndex(x => x.RedemptionId);
    }
}

public sealed class RedemptionConfiguration : IEntityTypeConfiguration<Redemption>
{
    public void Configure(EntityTypeBuilder<Redemption> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.Property(x => x.AccountNumberProtected).HasMaxLength(1000);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => new { x.Type, x.Status, x.RequestedAt });
        b.HasIndex(x => x.CustomerId);
        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DonationProjectConfiguration : IEntityTypeConfiguration<DonationProject>
{
    public void Configure(EntityTypeBuilder<DonationProject> b) => b.Property(x => x.Summary).HasMaxLength(80);
}
