using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Customer;

public sealed class EngagementTests(PostgresServer server)
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1];

    [Fact]
    public async Task The_inbox_pages_counts_and_clears()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var page = await client.GetJsonAsync("/api/v1/notifications?page=1&pageSize=20");
        page.MatchesContract("user", "GET /notifications");
        var first = page.GetProperty("items")[0];
        Assert.Equal(("pickupStatus", "enRoute", "RT-24817"), (first.Str("type"), first.Str("pickupStatus"), first.Str("targetId")));

        var unread = await client.GetJsonAsync("/api/v1/notifications/unread-count");
        unread.MatchesContract("user", "GET /notifications/unread-count");
        var count = unread.GetProperty("unreadCount").GetInt32();
        Assert.True(count > 0);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/notifications/{first.Str("id")}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/notifications/{first.Str("id")}/read", null)).StatusCode);
        Assert.Equal(count - 1, (await client.GetJsonAsync("/api/v1/notifications/unread-count")).GetProperty("unreadCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/notifications/{first.Str("id")}")).StatusCode);
        Assert.DoesNotContain((await client.GetJsonAsync("/api/v1/notifications")).GetProperty("items").EnumerateArray(), n => n.Str("id") == first.Str("id"));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await client.GetJsonAsync("/api/v1/notifications/unread-count")).GetProperty("unreadCount").GetInt32());
        await (await client.DeleteAsync("/api/v1/notifications/ntf_nope")).ErrorAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Preferences_hide_categories_but_never_pickup_updates()
    {
        await using var api = await TestApi.StartAsync(server);
        var mobile = await api.DbAsync(db => db.Customers.Where(c => c.Id == "cus_10").Select(c => c.Mobile!).SingleAsync());
        var client = await api.CustomerAsync(mobile); // Has paymentCredited alerts from today's collection.
        var prefs = await client.GetJsonAsync("/api/v1/notifications/preferences");
        prefs.MatchesContract("user", "GET /notifications/preferences");
        Assert.Equal((true, true, false), (prefs.GetProperty("pickupUpdates").GetBoolean(), prefs.GetProperty("payments").GetBoolean(),
            prefs.GetProperty("offers").GetBoolean()));

        var saved = await (await client.PutAsJsonAsync("/api/v1/notifications/preferences",
            new { pickupUpdates = false, orders = true, payments = false, offers = true })).JsonAsync();
        Assert.Equal((true, false, true), (saved.GetProperty("pickupUpdates").GetBoolean(), saved.GetProperty("payments").GetBoolean(),
            saved.GetProperty("offers").GetBoolean()));
        var types = (await client.GetJsonAsync("/api/v1/notifications")).GetProperty("items").EnumerateArray().Select(n => n.Str("type")).ToList();
        Assert.DoesNotContain("paymentCredited", types);
        Assert.Contains("pickupStatus", types);
    }

    [Fact]
    public async Task Devices_register_move_between_accounts_and_go_on_account_deletion()
    {
        await using var api = await TestApi.StartAsync(server);
        var a = await api.CustomerAsync("+94700000030");
        var b = await api.CustomerAsync("+94700000031");
        var device = new { token = "fcm-token-1", platform = "android", appVersion = "0.1.0", locale = "si" };
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/v1/devices", device)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsJsonAsync("/api/v1/devices", device)).StatusCode);
        var owner = await api.DbAsync(db => db.Devices.Where(d => d.Token == "fcm-token-1").Select(d => d.CustomerId).SingleAsync());
        Assert.Equal(await api.DbAsync(db => db.Customers.Where(c => c.Mobile == "+94700000031").Select(c => c.Id).SingleAsync()), owner);

        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/v1/devices/fcm-token-1")).StatusCode);
        Assert.True(await api.DbAsync(db => db.Devices.AnyAsync(d => d.Token == "fcm-token-1")));
        Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync("/api/v1/devices/unknown")).StatusCode);
        await (await b.PostAsJsonAsync("/api/v1/devices", new { token = "x", platform = "windows" })).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");

        var staff = await api.CallCentreAsync();
        await staff.PostJsonAsync($"/api/v1/admin/customers/{owner}/status", new { status = "suspended", reason = "Testing device cleanup." });
        Assert.False(await api.DbAsync(db => db.Devices.AnyAsync(d => d.Token == "fcm-token-1")));
    }

    [Fact]
    public async Task Support_lists_faqs_and_takes_tickets()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        (await client.GetJsonAsync("/api/v1/support/faqs")).MatchesContract("user", "GET /support/faqs");

        var form = new MultipartFormDataContent
        {
            { new StringContent("pickupWeight"), "category" },
            { new StringContent("RT-24817"), "pickupId" },
            { new StringContent("The weight looked lower than I expected."), "message" },
            { new ByteArrayContent(Jpeg), "screenshot", "screen.jpg" },
        };
        var ticket = await (await client.PostAsync("/api/v1/support/tickets", form)).JsonAsync(HttpStatusCode.Created);
        ShapeAssert.Matches(ContractSamples.Section("user", "POST /support/tickets")[0].Json, ticket);
        Assert.Equal("TCK-1000", ticket.Str("reference"));

        var short_ = new MultipartFormDataContent { { new StringContent("app"), "category" }, { new StringContent("Broken"), "message" } };
        var error = await (await client.PostAsync("/api/v1/support/tickets", short_)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        Assert.True(error.GetProperty("details").TryGetProperty("message", out _));
    }

    [Fact]
    public async Task App_config_needs_no_sign_in()
    {
        await using var api = await TestApi.StartAsync(server);
        var config = await api.Client().GetJsonAsync("/api/v1/app/config");
        config.MatchesContract("user", "GET /app/config");
        Assert.False(config.GetProperty("features").GetProperty("walletEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("features").GetProperty("pushEnabled").ValueKind);
    }

    [Fact]
    public async Task Home_summarises_wallet_pickups_impact_and_new_pieces()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var home = await client.GetJsonAsync("/api/v1/home/summary");
        home.MatchesContract("user", "GET /home/summary");
        Assert.Equal(("Nadeesha", "Perera"), (home.GetProperty("user").Str("firstName"), home.GetProperty("user").Str("lastName")));
        var active = home.GetProperty("activePickups")[0];
        Assert.Equal(("RT-24817", "enRoute", "Ruwan Silva"), (active.Str("id"), active.Str("status"), active.GetProperty("collector").Str("name")));
        Assert.Equal(["prd_1", "prd_2"], home.GetProperty("newProducts").EnumerateArray().Take(2).Select(p => p.Str("id")).Order());
        var summary = await client.GetJsonAsync("/api/v1/impact/summary?period=all");
        Assert.Equal(summary.GetProperty("totalKg").GetDecimal(), home.GetProperty("impact").GetProperty("totalWeightKg").GetDecimal());

        var fresh = await api.CustomerAsync("+94700000040");
        var empty = await fresh.GetJsonAsync("/api/v1/home/summary");
        Assert.Equal((0L, 0, 0m, JsonValueKind.Null), (empty.GetProperty("wallet").GetProperty("availableBalanceCents").GetInt64(),
            empty.GetProperty("activePickupCount").GetInt32(), empty.GetProperty("impact").GetProperty("totalWeightKg").GetDecimal(),
            empty.GetProperty("milestone").ValueKind));
    }
}
