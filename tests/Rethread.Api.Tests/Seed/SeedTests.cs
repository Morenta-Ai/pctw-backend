using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Data.Seed;
using Rethread.Api.Domain;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Seed;

public sealed class SeedTests(PostgresServer server)
{
    /// <summary>
    /// <c>portal-mock-world.json</c> is the admin portal's MockWorld (pctw-admin <c>src/mock</c>) at
    /// <see cref="TestClock.Start"/>. The portal's e2e tests were written against it, so the real seed has to
    /// produce the same records. Regenerate the file if the portal's seed changes.
    /// </summary>
    [Fact]
    public async Task Seed_matches_the_portal_mock_world()
    {
        await using var api = await TestApi.StartAsync(server);
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Seed", "portal-mock-world.json")))!;
        var actual = await api.DbAsync(db => DumpAsync(db, api.Factory.Services));
        // The mock lists today's pickups first; compare both by ID.
        expected["pickups"] = new JsonArray(expected["pickups"]!.AsArray()
            .OrderBy(p => p!["id"]!.GetValue<string>(), StringComparer.Ordinal)
            .Select(p => p!.DeepClone()).ToArray());

        JsonDiff.AssertSame(expected, actual);
    }

    [Fact]
    public async Task Reset_starts_the_demo_again_and_keeps_the_database()
    {
        await using var api = await TestApi.StartAsync(server);
        // What a day of demos does: collectors go quiet, pickups move on.
        await api.DbAsync(async db =>
        {
            await db.Collectors.ExecuteUpdateAsync(s => s.SetProperty(c => c.Online, false));
            await db.Pickups.Where(p => p.Id == "RT-24841")
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PickupStatus.Cancelled));
        });

        await using (var scope = api.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DevSeeder>().ResetAsync();

        await api.DbAsync(async db =>
        {
            Assert.Equal(["col_312", "col_388", "col_421", "col_457", "col_466", "col_480", "col_515"],
                await db.Collectors.Where(c => c.Online).OrderBy(c => c.Id).Select(c => c.Id).ToListAsync());
            Assert.True(await db.Pickups.AnyAsync(p => p.Id == "RT-24841" && p.Status == PickupStatus.Pending));
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        });
        var staff = await api.Client().PostJsonAsync("/api/v1/admin/auth/login",
            new { email = "admin@rethread.lk", password = "rethread-admin" });
        Assert.Equal("admin", staff.GetProperty("staff").Str("role"));
    }

    [Fact]
    public void The_hosted_demo_keeps_its_collectors_online()
    {
        using var staging = new TestApiFactory("Host=unused", TestClock.Start)
            .WithWebHostBuilder(b => b.UseEnvironment("Staging"));
        var config = staging.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(0, config.GetValue<int>("Maintenance:StaleCollectorMinutes"));
    }

    [Fact]
    public async Task Seed_holds_the_ids_the_clients_use()
    {
        await using var api = await TestApi.StartAsync(server);
        await api.DbAsync(async db =>
        {
            var ruwan = await db.Collectors.SingleAsync(c => c.Code == "COL-0457");
            Assert.Equal(("col_457", "+94771234567"), (ruwan.Id, ruwan.Mobile));

            var load = await db.Pickups.Where(p => p.CollectorId == ruwan.Id && p.Status == PickupStatus.Collected)
                .SumAsync(p => p.ActualWeightKg);
            Assert.Equal(20.5m, load);

            Assert.True(await db.Pickups.AnyAsync(p => p.Id == "RT-24841" && p.Status == PickupStatus.Pending));
            Assert.True(await db.Orders.AnyAsync(o => o.Id == "ORD-5134"));
            Assert.Equal(3, await db.Staff.CountAsync());

            var monaragala = await db.DonationProjects.SingleAsync(p => p.Id == "dp_monaragala");
            Assert.Equal((34_000_000L, 812), (monaragala.RaisedCents, monaragala.DonorCount));
        });
    }

    [Fact]
    public async Task Every_customer_balance_is_covered_by_earnings()
    {
        await using var api = await TestApi.StartAsync(server);
        var balances = await api.DbAsync(db => db.WalletTransactions
            .Where(t => t.Status != TransactionStatus.Failed)
            .GroupBy(t => t.CustomerId)
            .Select(g => g.Sum(t => t.AmountCents)).ToListAsync());
        Assert.All(balances, b => Assert.True(b >= 0));
    }

    [Fact]
    public async Task Seeding_twice_does_nothing_the_second_time()
    {
        await using var api = await TestApi.StartAsync(server);
        await using var scope = api.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<Rethread.Api.Data.Seed.DevSeeder>().SeedAsync());
    }

    private static async Task<JsonNode> DumpAsync(AppDbContext db, IServiceProvider services)
    {
        static string T(DateTime t) => t.ToString(UtcDateTimeConverter.Format, CultureInfo.InvariantCulture);
        static string? Tn(DateTime? t) => t is null ? null : T(t.Value);
        static string E<TEnum>(TEnum value) where TEnum : struct, Enum => CamelCaseEnumConverter<TEnum>.ToText(value);
        var protector = services.GetRequiredService<BankAccountProtector>();

        var collectors = await db.Collectors.OrderBy(c => c.Code == "COL-0457" ? 0 : 1).ToListAsync();
        var codeOrder = new[] { "COL-0457", "COL-0312", "COL-0388", "COL-0421", "COL-0466", "COL-0473", "COL-0480", "COL-0502", "COL-0515", "COL-0528", "COL-0533", "COL-0541" };
        collectors = [.. collectors.OrderBy(c => Array.IndexOf(codeOrder, c.Code))];
        var customers = await db.Customers.Include(c => c.Addresses).Include(c => c.FamilyMembers).ToListAsync();
        customers = [.. customers.OrderBy(c => int.Parse(c.Id[4..], CultureInfo.InvariantCulture))];
        var pickups = await db.Pickups.Include(p => p.Items).Include(p => p.Events).ToListAsync();
        var products = await db.Products.ToListAsync();
        var orders = await db.Orders.Include(o => o.Items).ToListAsync();
        var redemptions = await db.Redemptions.ToListAsync();

        object Dump() => new
        {
            collectors = collectors.Select(c => new
            {
                id = c.Id,
                code = c.Code,
                mobile = c.Mobile,
                status = E(c.Status),
                online = c.Online,
                latitude = c.Latitude,
                longitude = c.Longitude,
                lastSeenAt = Tn(c.LastSeenAt),
                hubId = c.HubId,
                rating = c.Rating,
                memberSince = c.MemberSince.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }),
            customers = customers.Select(c => new
            {
                id = c.Id,
                mobile = c.Mobile,
                email = c.Email,
                area = c.Addresses[0].Area,
                address = c.Addresses[0].FullLine,
                latitude = c.Addresses[0].Latitude,
                longitude = c.Addresses[0].Longitude,
                joinedAt = T(c.CreatedAt),
                status = E(c.Status),
                familyMemberCount = c.FamilyMembers.Count,
            }),
            pickups = pickups.OrderBy(p => p.Id).Select(p => new
            {
                id = p.Id,
                customerId = p.CustomerId,
                collectorId = p.CollectorId,
                status = E(p.Status),
                source = E(p.Source),
                method = E(p.Method),
                estimatedKg = p.EstimatedKg,
                itemCount = p.ItemCount,
                bagCount = p.BagCount,
                actualWeightKg = p.ActualWeightKg,
                scheduledDate = p.ScheduledDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                slot = E(p.Slot),
                windowStart = T(p.WindowStart),
                createdAt = T(p.CreatedAt),
                customerPayoutCents = p.CustomerPayoutCents,
                hubId = p.HubId,
                cancelReason = p.CancelReason,
                items = p.Items.OrderBy(i => i.SortOrder).Select(i => new
                {
                    name = i.Name,
                    quantity = i.Quantity,
                    size = i.Size,
                    condition = E(i.Condition),
                }),
                timeline = p.Events.OrderBy(e => e.Id).Select(e => new
                {
                    status = E(e.Status),
                    occurredAt = T(e.OccurredAt),
                    actor = e.Actor,
                }),
            }),
            products = products.OrderBy(p => int.Parse(p.Id[4..], CultureInfo.InvariantCulture))
                .Select(p => new { id = p.Id, createdAt = T(p.CreatedAt), published = p.Published }),
            orders = orders.OrderByDescending(o => o.Id).Select(o => new
            {
                id = o.Id,
                customerId = o.CustomerId,
                status = E(o.Status),
                fulfilmentType = E(o.FulfilmentType),
                vendorId = o.VendorId,
                subtotalCents = o.SubtotalCents,
                totalCents = o.TotalCents,
                waybillNumber = o.WaybillNumber,
                createdAt = T(o.CreatedAt),
                items = o.Items.OrderBy(i => i.Id).Select(i => new { productId = i.ProductId, size = i.Size, quantity = i.Quantity }),
            }),
            redemptions = redemptions.OrderBy(r => int.Parse(r.Id[4..], CultureInfo.InvariantCulture)).Select(r => new
            {
                id = r.Id,
                customerId = r.CustomerId,
                type = E(r.Type),
                status = E(r.Status),
                amountCents = r.AmountCents,
                accountNumber = r.AccountNumberProtected is null ? null : protector.Unprotect(r.AccountNumberProtected),
                requestedAt = T(r.RequestedAt),
            }),
        };

        return JsonSerializer.SerializeToNode(Dump())!;
    }
}
