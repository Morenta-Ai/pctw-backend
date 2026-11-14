using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Data.Configurations;

public sealed class HubConfiguration : IEntityTypeConfiguration<Hub>
{
    public void Configure(EntityTypeBuilder<Hub> b)
    {
        b.Property(x => x.Code).HasMaxLength(8);
        b.Property(x => x.ClosesAt).HasMaxLength(5);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class GarmentTypeConfiguration : IEntityTypeConfiguration<GarmentType>
{
    public void Configure(EntityTypeBuilder<GarmentType> b) => b.Property(x => x.EstimatedKg).HasPrecision(6, 2);
}

public sealed class VoucherBrandConfiguration : IEntityTypeConfiguration<VoucherBrand>
{
    public void Configure(EntityTypeBuilder<VoucherBrand> b) => b.Property(x => x.CodePrefix).HasMaxLength(4);
}

public sealed class RateSetConfiguration : IEntityTypeConfiguration<RateSet>
{
    public void Configure(EntityTypeBuilder<RateSet> b) => b.HasIndex(x => x.EffectiveFrom).IsUnique();
}

public sealed class ImpactMultiplierConfiguration : IEntityTypeConfiguration<ImpactMultiplier>
{
    public void Configure(EntityTypeBuilder<ImpactMultiplier> b)
    {
        b.HasKey(x => x.Key);
        b.Property(x => x.PerKg).HasPrecision(14, 4);
    }
}

public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b) => b.ToTable("files");
}
