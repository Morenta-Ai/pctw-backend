using System.Net;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Data.Entities;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Collector;

public sealed class LoadTests(PostgresServer server)
{
    [Fact]
    public async Task The_load_is_what_has_been_collected_but_not_dropped_off()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CollectorAsync();
        var load = await client.GetJsonAsync("/api/v1/collector/load");
        load.MatchesContract("collector", "GET /load");
        Assert.Equal((20.5m, 120, "WP ABC-1234"), (load.GetProperty("weightKg").GetDecimal(),
            load.GetProperty("capacityKg").GetInt32(), load.Str("vehiclePlate")));
        Assert.Equal(["RT-24790", "RT-24796", "RT-24803"], load.GetProperty("items").EnumerateArray().Select(i => i.Str("jobId")));

        var hubs = await client.GetJsonAsync("/api/v1/collector/hubs");
        hubs.MatchesContract("collector", "GET /hubs");
        Assert.Equal("hub_mrt", hubs.GetProperty("items")[0].Str("id")); // Moratuwa is nearer Dehiwala.
    }

    [Fact]
    public async Task A_hub_drop_issues_a_receipt_and_tells_each_customer()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CollectorAsync();

        var receipt = await client.PostJsonAsync("/api/v1/collector/hub-deliveries",
            new { hubId = "hub_plg", jobIds = new[] { "RT-24790", "RT-24796" } });
        receipt.MatchesContract("collector", "POST /hub-deliveries");
        Assert.Equal("HUB-PLG-071126-0457", receipt.Str("receiptId"));
        Assert.Equal((2, 13.3m, 2), (receipt.GetProperty("jobCount").GetInt32(), receipt.GetProperty("weightKg").GetDecimal(),
            receipt.GetProperty("customersNotified").GetInt32()));

        await api.DbAsync(async db =>
        {
            var delivered = await db.Pickups.Where(p => p.HubDeliveryId == "HUB-PLG-071126-0457").ToListAsync();
            Assert.All(delivered, p => Assert.Equal((PickupStatus.DeliveredToHub, "hub_plg"), (p.Status, p.HubId)));
            Assert.Equal(2, await db.Notifications.CountAsync(n => n.Type == "hubArrival" && n.CreatedAt == TestClock.Start.UtcDateTime));
        });

        var load = await client.GetJsonAsync("/api/v1/collector/load");
        Assert.Equal(7.2m, load.GetProperty("weightKg").GetDecimal());

        await (await client.PostAsJsonAsync("/api/v1/collector/hub-deliveries", new { hubId = "hub_plg", jobIds = new[] { "RT-24790", "RT-24803" } }))
            .ErrorAsync(HttpStatusCode.Conflict, "NOTHING_TO_DELIVER");
        await (await client.PostAsJsonAsync("/api/v1/collector/hub-deliveries", new { hubId = "hub_plg", jobIds = new[] { "RT-24811" } }))
            .ErrorAsync(HttpStatusCode.Conflict, "NOTHING_TO_DELIVER");
        await (await client.PostAsJsonAsync("/api/v1/collector/hub-deliveries", new { hubId = "hub_x", jobIds = new[] { "RT-24803" } }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");

        var second = await client.PostJsonAsync("/api/v1/collector/hub-deliveries", new { hubId = "hub_plg", jobIds = new[] { "RT-24803" } });
        Assert.Equal("HUB-PLG-071126-0457-2", second.Str("receiptId"));
    }

    [Fact]
    public async Task Earnings_cover_today_or_the_week()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CollectorAsync();

        var today = await client.GetJsonAsync("/api/v1/collector/earnings?period=today");
        today.MatchesContract("collector", "GET /earnings");
        Assert.Equal((82000L, 4000L, 3, 20.5m), (today.GetProperty("totalCents").GetInt64(), today.GetProperty("ratePerKgCents").GetInt64(),
            today.GetProperty("jobCount").GetInt32(), today.GetProperty("collectedKg").GetDecimal()));
        Assert.All(today.GetProperty("jobs").EnumerateArray(), j => Assert.Equal("collected", j.Str("status")));

        await client.PostJsonAsync("/api/v1/collector/hub-deliveries", new { hubId = "hub_plg", jobIds = new[] { "RT-24790" } });
        today = await client.GetJsonAsync("/api/v1/collector/earnings");
        Assert.Contains(today.GetProperty("jobs").EnumerateArray(), j => j.Str("jobId") == "RT-24790" && j.Str("status") == "atHub");

        var week = await client.GetJsonAsync("/api/v1/collector/earnings?period=week");
        Assert.Equal("week", week.Str("period"));
        Assert.True(week.GetProperty("totalCents").GetInt64() >= 82000);
        await (await client.GetAsync("/api/v1/collector/earnings?period=year")).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }
}
