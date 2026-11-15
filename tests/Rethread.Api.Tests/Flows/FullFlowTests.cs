using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Data.Entities;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Flows;

/// <summary>One pickup through all three apps, from booking to the customer's bank.</summary>
public sealed class FullFlowTests(PostgresServer server)
{
    [Fact]
    public async Task A_pickup_from_booking_to_a_bank_transfer()
    {
        await using var api = await TestApi.StartAsync(server);
        var customer = await api.CustomerAsync("+94700000100");
        var staff = await api.CallCentreAsync();
        var ruwan = await api.CollectorAsync();
        var lahiruMobile = await api.DbAsync(db => db.Collectors.Where(c => c.Code == "COL-0515").Select(c => c.Mobile).SingleAsync());
        var lahiru = await api.CollectorAsync("COL-0515", lahiruMobile);

        // 1. A new customer signs up, adds their home and books a pickup in the app.
        await customer.PutAsJsonAsync("/api/v1/me", new { fullName = "Tharushi Silva", preferredLanguage = "en" });
        var home = await customer.PostJsonAsync("/api/v1/addresses", new
        {
            label = "home",
            addressLine = "14 Station Road",
            city = "Dehiwala",
            lat = 6.8521,
            lng = 79.8661,
            isDefault = true,
        }, HttpStatusCode.Created);
        var booking = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pickups")
        {
            Content = JsonContent.Create(new
            {
                mode = "bulk",
                pieceCount = 30,
                bagCount = 3,
                addressId = home.Str("id"),
                notes = "Blue gate",
                fallbackToCallCentre = false,
                preferredDates = new[]
                {
                    new { priority = 1, date = "2026-11-08", slotId = "morning" },
                    new { priority = 2, date = "2026-11-09", slotId = "morning" },
                    new { priority = 3, date = "2026-11-10", slotId = "evening" },
                },
            }),
        };
        booking.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var pickup = await (await customer.SendAsync(booking)).JsonAsync(HttpStatusCode.Created);
        var id = pickup.Str("id");

        // 2. The call centre sees it and offers it to Ruwan, who declines; then to Lahiru, who accepts.
        Assert.Contains((await staff.GetJsonAsync("/api/v1/admin/dispatch")).GetProperty("pickups").EnumerateArray(), p => p.Str("id") == id);
        var first = await staff.PostJsonAsync($"/api/v1/admin/pickups/{id}/assignments", new { collectorId = "col_457" }, HttpStatusCode.Created);
        Assert.Equal(first.Str("id"), (await ruwan.GetJsonAsync("/api/v1/collector/assignments/current")).Str("id"));
        await ruwan.PostAsJsonAsync($"/api/v1/collector/assignments/{first.Str("id")}/decline", new { reason = "vehicleFull" });
        var second = await staff.PostJsonAsync($"/api/v1/admin/pickups/{id}/assignments", new { collectorId = "col_515", note = "Blue gate" }, HttpStatusCode.Created);
        Assert.Equal("accepted", (await lahiru.PostJsonAsync($"/api/v1/collector/assignments/{second.Str("id")}/accept", null)).Str("status"));
        Assert.Equal("confirmed", (await customer.GetJsonAsync($"/api/v1/pickups/{id}")).Str("status"));

        // 3. Lahiru drives there, weighs 9.25 kg and pays the customer.
        api.Clock.Advance(TimeSpan.FromMinutes(20));
        await lahiru.PostJsonAsync($"/api/v1/collector/jobs/{id}/start", null);
        Assert.Equal("enRoute", (await customer.GetJsonAsync($"/api/v1/pickups/{id}")).Str("status"));
        api.Clock.Advance(TimeSpan.FromMinutes(15));
        await lahiru.PostJsonAsync($"/api/v1/collector/jobs/{id}/arrive", null);
        var collection = await lahiru.PostJsonAsync($"/api/v1/collector/jobs/{id}/collection",
            new { weightKg = 9.25, weightSource = "scale", bagCount = 3 });
        Assert.Equal(111000, collection.GetProperty("payoutCents").GetInt64());

        // 4. The customer sees the weight and the money; the portal sees the payouts. (Staff tokens last 15
        // minutes; the portal refreshes them, the test signs in again.)
        staff = await api.CallCentreAsync();
        var tracked = await customer.GetJsonAsync($"/api/v1/pickups/{id}");
        Assert.Equal(("collected", 9.25m, 111000L), (tracked.Str("status"), tracked.GetProperty("weighing").GetProperty("weightKg").GetDecimal(),
            tracked.GetProperty("weighing").GetProperty("earningsCents").GetInt64()));
        Assert.Equal(111000, (await customer.GetJsonAsync("/api/v1/wallet")).GetProperty("balanceCents").GetInt64());
        var detail = await staff.GetJsonAsync($"/api/v1/admin/pickups/{id}");
        Assert.Equal((111000L, 37000L), (detail.GetProperty("customerPayoutCents").GetInt64(), detail.GetProperty("collectorPayoutCents").GetInt64()));

        // 5. Lahiru drops it at the hub; the customer hears it arrived.
        var receipt = await lahiru.PostJsonAsync("/api/v1/collector/hub-deliveries", new { hubId = "hub_plg", jobIds = new[] { id } });
        Assert.Equal(1, receipt.GetProperty("customersNotified").GetInt32());
        Assert.Equal("deliveredToHub", (await customer.GetJsonAsync($"/api/v1/pickups/{id}")).Str("status"));

        // 6. The customer cashes out to their bank; the call centre pays it.
        await customer.PutAsJsonAsync("/api/v1/wallet/bank-details",
            new { bankName = "Sampath Bank", branch = "Dehiwala", accountName = "Tharushi Silva", accountNumber = "1029384756" });
        var cash = await customer.PostJsonAsync("/api/v1/wallet/redemptions", new { type = "cash", amountCents = 100000 }, HttpStatusCode.Created);
        Assert.Equal(11000, cash.GetProperty("balanceCents").GetInt64());
        var queue = await staff.GetJsonAsync("/api/v1/admin/redemptions?type=cash&status=pending");
        var mine = queue.GetProperty("items").EnumerateArray().Single(r => r.Str("id") == cash.Str("id"));
        Assert.Equal("1029384756", mine.GetProperty("bank").Str("accountNumber"));
        await staff.PostJsonAsync($"/api/v1/admin/redemptions/{cash.Str("id")}/pay", new { reference = "CEFT 1107-00001" });
        var wallet = await customer.GetJsonAsync("/api/v1/wallet");
        Assert.Equal((11000L, 0L), (wallet.GetProperty("balanceCents").GetInt64(), wallet.GetProperty("pendingCents").GetInt64()));

        // Everyone was told what they needed to know, and the timeline tells the story.
        var customerTypes = (await customer.GetJsonAsync("/api/v1/notifications")).GetProperty("items").EnumerateArray().Select(n => n.Str("type")).ToHashSet();
        Assert.Superset(new HashSet<string> { "pickupDateConfirmed", "pickupStatus", "paymentCredited", "system" }, customerTypes);
        var lahiruTypes = (await lahiru.GetJsonAsync("/api/v1/collector/notifications")).GetProperty("items").EnumerateArray().Select(n => n.Str("type"));
        Assert.Contains("assignment", lahiruTypes);
        var staffAlerts = await api.DbAsync(db => db.Notifications.Where(n => n.Audience == Audience.Staff && n.CreatedAt >= TestClock.Start.UtcDateTime)
            .Select(n => n.Type).ToListAsync());
        Assert.Superset(new HashSet<string> { "pickupRequested", "offerDeclined", "transferRequested" }, staffAlerts.ToHashSet());

        var timeline = (await staff.GetJsonAsync($"/api/v1/admin/pickups/{id}")).GetProperty("timeline").EnumerateArray()
            .Select(s => (s.Str("status"), s.GetProperty("actor").ValueKind == System.Text.Json.JsonValueKind.Null ? null : s.Str("actor"))).ToList();
        Assert.Equal([("pending", null), ("assigned", "Lahiru Gunasekara"), ("enRoute", "Lahiru Gunasekara"), ("arrived", "Lahiru Gunasekara"),
            ("collected", "Lahiru Gunasekara"), ("deliveredToHub", "Lahiru Gunasekara")], timeline);
    }

    [Fact]
    public async Task Production_ignores_the_development_switches()
    {
        await using var api = await TestApi.StartAsync(server, new Dictionary<string, string>
        {
            ["Simulation:AutoAnswerOffers"] = "true",
        });
        await using var production = api.Factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var client = production.CreateClient();
        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = SignIn.NadeeshaMobile });
        await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { requestId = challenge.Str("requestId"), code = SignIn.Code }))
            .ErrorAsync(HttpStatusCode.BadRequest, "OTP_INVALID");
    }
}
