using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<BankDetails> BankDetails => Set<BankDetails>();
    public DbSet<Collector> Collectors => Set<Collector>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Hub> Hubs => Set<Hub>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<GarmentType> GarmentTypes => Set<GarmentType>();
    public DbSet<VoucherBrand> VoucherBrands => Set<VoucherBrand>();
    public DbSet<RateSet> RateSets => Set<RateSet>();
    public DbSet<ImpactMultiplier> ImpactMultipliers => Set<ImpactMultiplier>();
    public DbSet<StoredFile> Files => Set<StoredFile>();

    public DbSet<Pickup> Pickups => Set<Pickup>();
    public DbSet<PickupItem> PickupItems => Set<PickupItem>();
    public DbSet<PickupEvent> PickupEvents => Set<PickupEvent>();
    public DbSet<PickupPhoto> PickupPhotos => Set<PickupPhoto>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<HubDelivery> HubDeliveries => Set<HubDelivery>();

    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<Redemption> Redemptions => Set<Redemption>();
    public DbSet<DonationProject> DonationProjects => Set<DonationProject>();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<StaffNotificationRead> StaffNotificationReads => Set<StaffNotificationRead>();

    /// <summary>Next value of one of the ID sequences in <see cref="Sequences"/>.</summary>
    public async Task<long> NextAsync(string sequence, CancellationToken ct) =>
        await Database.SqlQuery<long>($"SELECT nextval({sequence}::regclass) AS \"Value\"").SingleAsync(ct);

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        foreach (var type in typeof(AccountStatus).Assembly.GetTypes()
                     .Where(t => t.IsEnum && t.Namespace == typeof(AccountStatus).Namespace))
        {
            builder.Properties(type)
                .HaveConversion(typeof(CamelCaseEnumConverter<>).MakeGenericType(type))
                .HaveMaxLength(32);
        }

        builder.Properties<decimal>().HavePrecision(12, 2);
        builder.Properties<string>().HaveMaxLength(500);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(Sequences.PickupNumber).StartsAt(24_850);
        modelBuilder.HasSequence<long>(Sequences.OrderNumber).StartsAt(5_135);
        modelBuilder.HasSequence<long>(Sequences.CollectorNumber).StartsAt(542);
        modelBuilder.HasSequence<long>(Sequences.WaybillNumber).StartsAt(100);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

public static class Sequences
{
    public const string PickupNumber = "pickup_number_seq";
    public const string OrderNumber = "order_number_seq";
    public const string CollectorNumber = "collector_number_seq";
    public const string WaybillNumber = "waybill_number_seq";
}
