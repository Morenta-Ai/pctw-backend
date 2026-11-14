using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Data.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Id).HasMaxLength(40);
        b.Property(x => x.Name).HasMaxLength(80);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => x.CreatedAt);
    }
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.Property(x => x.Id).HasMaxLength(20);
        b.Property(x => x.Destination).HasMaxLength(200);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => x.WaybillNumber).IsUnique().HasFilter("waybill_number IS NOT NULL");
        b.HasIndex(x => new { x.CustomerId, x.CreatedAt });
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
