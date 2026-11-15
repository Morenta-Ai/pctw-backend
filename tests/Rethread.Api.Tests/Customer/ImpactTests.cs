using System.Net;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Customer;

public sealed class ImpactTests(PostgresServer server)
{
    private const string Contract = "user-next";

    private static async Task<(HttpClient Client, decimal TotalKg, int Count)> BusiestAsync(TestApi api)
    {
        var (id, kg, count) = await api.DbAsync(async db =>
        {
            var top = await db.Pickups.Where(p => p.CollectedAt != null)
                .GroupBy(p => p.CustomerId).OrderByDescending(g => g.Sum(p => p.ActualWeightKg))
                .Select(g => new { g.Key, Kg = g.Sum(p => p.ActualWeightKg!.Value), Count = g.Count() }).FirstAsync();
            return (top.Key, top.Kg, top.Count);
        });
        var mobile = await api.DbAsync(db => db.Customers.Where(c => c.Id == id).Select(c => c.Mobile!).SingleAsync());
        return (await api.CustomerAsync(mobile), kg, count);
    }

    [Fact]
    public async Task The_summary_adds_up_every_weighing()
    {
        await using var api = await TestApi.StartAsync(server);
        var (client, kg, count) = await BusiestAsync(api);
        var all = await client.GetJsonAsync("/api/v1/impact/summary?period=all");
        all.MatchesContract(Contract, "GET /impact/summary");
        Assert.Equal((kg, count), (all.GetProperty("totalKg").GetDecimal(), all.GetProperty("pickupCount").GetInt32()));
        var metrics = all.GetProperty("metrics");
        Assert.Equal((long)Math.Round(kg * 2500, 0, MidpointRounding.AwayFromZero), metrics.GetProperty("taxSavedCents").GetInt64());
        Assert.Equal(Math.Round(kg * 0.27m, 2, MidpointRounding.AwayFromZero), metrics.GetProperty("jobsGenerated").GetDecimal());

        var month = await client.GetJsonAsync("/api/v1/impact/summary?period=month");
        Assert.True(month.GetProperty("totalKg").GetDecimal() <= kg);
        await (await client.GetAsync("/api/v1/impact/summary?period=decade")).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");

        var fresh = await api.CustomerAsync("+94700000020");
        var none = await fresh.GetJsonAsync("/api/v1/impact/summary");
        Assert.Equal((0m, 0, 0m), (none.GetProperty("totalKg").GetDecimal(), none.GetProperty("pickupCount").GetInt32(),
            none.GetProperty("metrics").GetProperty("ghgSavedKg").GetDecimal()));
    }

    [Fact]
    public async Task History_has_one_entry_per_month_ending_now()
    {
        await using var api = await TestApi.StartAsync(server);
        var (client, kg, _) = await BusiestAsync(api);
        var history = await client.GetJsonAsync("/api/v1/impact/history?months=12");
        history.MatchesContract(Contract, "GET /impact/history");
        var items = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(12, items.Count);
        Assert.Equal(("2025-12", "2026-11"), (items[0].Str("month"), items[^1].Str("month")));
        Assert.Equal(kg, items.Sum(i => i.GetProperty("totalKg").GetDecimal()));
        Assert.Equal(24, (await client.GetJsonAsync("/api/v1/impact/history?months=99")).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Factors_use_the_apps_names()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var factors = await client.GetJsonAsync("/api/v1/impact/factors");
        factors.MatchesContract(Contract, "GET /impact/factors");
        Assert.Equal(["ghgSavedKg", "resourcesSavedKg", "taxSavedCents", "forexSavedUsdCents", "jobsGenerated",
            "entrepreneursCreated", "clothesDonatedPieces", "donationValueCents"],
            factors.GetProperty("factors").EnumerateArray().Select(f => f.Str("key")));
    }

    [Fact]
    public async Task Milestones_are_earned_when_a_weighing_crosses_them()
    {
        await using var api = await TestApi.StartAsync(server);
        var (client, kg, _) = await BusiestAsync(api);
        var milestones = await client.GetJsonAsync("/api/v1/impact/milestones");
        milestones.MatchesContract(Contract, "GET /impact/milestones");
        var earned = milestones.GetProperty("earned").EnumerateArray().Select(m => m.Str("id")).ToList();
        Assert.Contains("ms_10kg", earned);
        var next = milestones.GetProperty("next");
        Assert.Equal(kg, next.GetProperty("currentKg").GetDecimal());
        Assert.True(next.GetProperty("targetKg").GetInt32() > kg);

        var fresh = await api.CustomerAsync("+94700000021");
        var none = await fresh.GetJsonAsync("/api/v1/impact/milestones");
        Assert.Empty(none.GetProperty("earned").EnumerateArray());
        Assert.Equal(("ms_10kg", 0m), (none.GetProperty("next").Str("id"), none.GetProperty("next").GetProperty("currentKg").GetDecimal()));
    }
}
