using System.Net;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Admin;

public sealed class RatesTests(PostgresServer server)
{
    private static object Rates(long customer, long collector, string from) => new
    {
        customerRatePerKgCents = customer,
        collectorRatePerKgCents = collector,
        homeDeliveryChargeCents = 35000,
        clickAndCollectChargeCents = 25000,
        effectiveFrom = from,
    };

    [Fact]
    public async Task Admins_see_current_scheduled_and_past_rates()
    {
        await using var api = await TestApi.StartAsync(server);
        await (await (await api.CallCentreAsync()).GetAsync("/api/v1/admin/rates")).ErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        var rates = await (await api.AdminAsync()).GetJsonAsync("/api/v1/admin/rates");
        rates.MatchesContract("admin", "GET /admin/rates");
        Assert.Equal("rate_3", rates.GetProperty("current").Str("id"));
        Assert.Equal(["rate_2", "rate_1"], rates.GetProperty("history").EnumerateArray().Select(r => r.Str("id")));
    }

    [Fact]
    public async Task A_change_for_today_applies_to_new_bookings_only()
    {
        await using var api = await TestApi.StartAsync(server);
        var admin = await api.AdminAsync();
        var rates = await admin.PostJsonAsync("/api/v1/admin/rates", Rates(12500, 4200, "2026-11-07"), HttpStatusCode.Created);
        Assert.Equal((12500L, "Nadeesha Perera"), (rates.GetProperty("current").GetProperty("customerRatePerKgCents").GetInt64(),
            rates.GetProperty("current").Str("createdBy")));
        Assert.Equal("rate_3", rates.GetProperty("history")[0].Str("id"));

        var customer = await api.CustomerAsync();
        Assert.Equal(12500, (await customer.GetJsonAsync("/api/v1/wallet")).GetProperty("ratePerKgCents").GetInt64());
        Assert.Equal(12000, (await customer.GetJsonAsync("/api/v1/pickups/RT-24817")).GetProperty("ratePerKgCents").GetInt64());
        Assert.Equal(4200, (await (await api.CollectorAsync()).GetJsonAsync("/api/v1/collector/me")).GetProperty("collectorRatePerKgCents").GetInt64());

        // A second change the same day wins.
        var again = await admin.PostJsonAsync("/api/v1/admin/rates", Rates(13000, 4200, "2026-11-07"), HttpStatusCode.Created);
        Assert.Equal(13000, again.GetProperty("current").GetProperty("customerRatePerKgCents").GetInt64());
    }

    [Fact]
    public async Task Scheduled_changes_replace_each_other_and_can_be_cancelled()
    {
        await using var api = await TestApi.StartAsync(server);
        var admin = await api.AdminAsync();
        await admin.PostJsonAsync("/api/v1/admin/rates", Rates(12500, 4200, "2026-12-01"), HttpStatusCode.Created);
        var replaced = await admin.PostJsonAsync("/api/v1/admin/rates", Rates(13000, 4500, "2099-01-01"), HttpStatusCode.Created);
        Assert.Equal(("rate_3", "2099-01-01"), (replaced.GetProperty("current").Str("id"), replaced.GetProperty("scheduled").Str("effectiveFrom")));
        Assert.Equal(4, await api.DbAsync(db => db.RateSets.CountAsync()));

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        var cancelled = await (await admin.DeleteAsync("/api/v1/admin/rates/scheduled")).JsonAsync();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cancelled.GetProperty("scheduled").ValueKind);
        await (await admin.DeleteAsync("/api/v1/admin/rates/scheduled")).ErrorAsync(HttpStatusCode.NotFound, "NO_SCHEDULED_RATES");
    }

    [Fact]
    public async Task Bad_rates_are_refused_with_the_portals_messages()
    {
        await using var api = await TestApi.StartAsync(server);
        var admin = await api.AdminAsync();
        var error = await (await admin.PostAsJsonAsync("/api/v1/admin/rates", Rates(12500, 13000, "2026-11-07")))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        Assert.Equal("The collector rate can't be more than the customer rate.", error.GetProperty("details").Str("collectorRatePerKgCents"));
        error = await (await admin.PostAsJsonAsync("/api/v1/admin/rates", Rates(0, 0, "2026-11-01")))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        Assert.Equal("Choose today or a later date.", error.GetProperty("details").Str("effectiveFrom"));
        Assert.True(error.GetProperty("details").TryGetProperty("customerRatePerKgCents", out _));
    }

    [Fact]
    public async Task Multipliers_change_everyones_impact()
    {
        await using var api = await TestApi.StartAsync(server);
        var admin = await api.AdminAsync();
        var multipliers = await admin.GetJsonAsync("/api/v1/admin/impact-multipliers");
        multipliers.MatchesContract("admin", "GET /admin/impact-multipliers");

        var customer = await api.CustomerAsync("+94764853329"); // Dinesh: plenty of history.
        var impact = await customer.GetJsonAsync("/api/v1/impact");
        impact.MatchesContract("user", "GET /impact");
        var total = impact.GetProperty("totalWeightKg").GetDecimal();
        Assert.True(total > 0);
        Assert.Equal(8, impact.GetProperty("metrics").GetArrayLength());
        var ghg = impact.GetProperty("metrics").EnumerateArray().Single(m => m.Str("key") == "ghgSaved");
        Assert.Equal(Math.Round(total * 3.5m, 1, MidpointRounding.AwayFromZero), ghg.GetProperty("value").GetDecimal());
        Assert.Equal(6, ghg.GetProperty("series").GetArrayLength());
        Assert.Equal("2026-11", impact.GetProperty("monthlyWeights").EnumerateArray().Last().Str("month"));

        var updated = await (await admin.PutAsJsonAsync("/api/v1/admin/impact-multipliers",
            ContractSamples.Request("admin", "PUT /admin/impact-multipliers"))).JsonAsync();
        Assert.Equal((50m, "Nadeesha Perera"), (updated.GetProperty("items").EnumerateArray().Single(m => m.Str("key") == "forexSaved").GetProperty("perKg").GetDecimal(),
            updated.Str("updatedBy")));
        var forex = (await customer.GetJsonAsync("/api/v1/impact")).GetProperty("metrics").EnumerateArray().Single(m => m.Str("key") == "forexSaved");
        Assert.Equal(Math.Round(total * 50, 0, MidpointRounding.AwayFromZero), forex.GetProperty("value").GetDecimal());

        async Task Refused(object body, string key)
        {
            var error = await (await admin.PutAsJsonAsync("/api/v1/admin/impact-multipliers", body)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
            Assert.True(error.GetProperty("details").TryGetProperty(key, out _), error.ToString());
        }

        await Refused(new { items = new[] { new { key = "ghgSaved", perKg = -1m } } }, "ghgSaved");
        await Refused(new { items = new[] { new { key = "forexSaved", perKg = 50.5m } } }, "forexSaved");
        await Refused(new { items = new[] { new { key = "jobsGenerated", perKg = 0.27123m } } }, "jobsGenerated");
        await Refused(new { items = new[] { new { key = "happiness", perKg = 1m } } }, "happiness");
    }
}
