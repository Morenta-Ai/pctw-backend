using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Data.Entities;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Customer;

public sealed class PickupTests(PostgresServer server)
{
    private const string Today = "2026-11-07";

    /// <summary>The contract's request sample, for today's afternoon at the customer's home.</summary>
    private static JsonNode Booking(string addressId = "adr_1", string date = Today, string slot = "afternoon")
    {
        var body = JsonNode.Parse(ContractSamples.Request("user", "POST /pickups").GetRawText())!;
        body["addressId"] = addressId;
        body["scheduledDate"] = date;
        body["slot"] = slot;
        return body;
    }

    [Fact]
    public async Task Garment_types_come_from_the_server()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var types = await client.GetJsonAsync("/api/v1/pickups/garment-types");
        types.MatchesContract("user", "GET /pickups/garment-types");
        Assert.Equal(8, types.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_pickup_on_its_way_shows_the_collector_and_the_tracker()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();

        var pickup = await client.GetJsonAsync("/api/v1/pickups/RT-24817");
        pickup.MatchesContract("user", "GET /pickups/{id}");
        Assert.Equal("enRoute", pickup.Str("status"));
        var collector = pickup.GetProperty("collector");
        Assert.Equal(("Ruwan Silva", "Three-wheeler", "+94771234567"),
            (collector.Str("name"), collector.Str("vehicleType"), collector.Str("phone")));
        Assert.True(pickup.GetProperty("etaMinutes").GetInt32() > 0);
        Assert.Equal(12000, pickup.GetProperty("ratePerKgCents").GetInt64());

        var timeline = pickup.GetProperty("timeline").EnumerateArray().ToList();
        Assert.Equal(["pending", "assigned", "enRoute", "collected", "deliveredToHub"], timeline.Select(s => s.Str("status")));
        Assert.Equal(JsonValueKind.Null, timeline[3].GetProperty("occurredAt").ValueKind);
        Assert.Equal("2026-11-07T10:30:00Z", timeline[4].Str("expectedAt"));

        var upcoming = await client.GetJsonAsync("/api/v1/pickups?group=upcoming");
        Assert.Contains(upcoming.GetProperty("items").EnumerateArray(), p => p.Str("id") == "RT-24817");
        foreach (var item in upcoming.GetProperty("items").EnumerateArray()) item.MatchesContract("user", "GET /pickups/{id}");
    }

    [Fact]
    public async Task The_user_app_sees_arrived_as_en_route()
    {
        await using var api = await TestApi.StartAsync(server);
        var mobile = await api.DbAsync(db => db.Customers.Where(c => c.Id == "cus_8").Select(c => c.Mobile!).SingleAsync());
        var client = await api.CustomerAsync(mobile);
        Assert.Equal("enRoute", (await client.GetJsonAsync("/api/v1/pickups/RT-24819")).Str("status"));
    }

    [Fact]
    public async Task Booking_a_bulk_pickup_snapshots_the_rates_and_alerts_the_call_centre()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();

        var pickup = await (await client.PostAsJsonAsync("/api/v1/pickups", Booking())).JsonAsync(HttpStatusCode.Created);
        pickup.MatchesContract("user", "GET /pickups/{id}");
        Assert.StartsWith("RT-248", pickup.Str("id"), StringComparison.Ordinal);
        Assert.Equal(("pending", 8.0m), (pickup.Str("status"), pickup.GetProperty("estimatedWeightKg").GetDecimal()));
        Assert.Equal("Gate code 4412", pickup.Str("notes"));

        var stored = await api.DbAsync(db => db.Pickups.SingleAsync(p => p.Id == pickup.Str("id")));
        Assert.Equal((PickupSource.App, 12000L, 4000L, 2), (stored.Source, stored.CustomerRatePerKgCents, stored.CollectorRatePerKgCents, stored.BagCount));
        Assert.Equal(new DateTime(2026, 11, 7, 6, 30, 0, DateTimeKind.Utc), stored.WindowStart);
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.Id == $"ntf_pickupRequested_{stored.Id}")));
    }

    [Fact]
    public async Task Itemised_and_piece_counts_are_estimated_from_reference_weights()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();

        var itemised = Booking();
        itemised["method"] = "itemised";
        itemised["bulkMode"] = null;
        itemised["declaredWeightKg"] = null;
        itemised["items"] = new JsonArray(
            JsonNode.Parse("""{ "garmentType": "shirt", "memberId": null, "size": "M", "condition": "good", "quantity": 3 }"""),
            JsonNode.Parse("""{ "garmentType": "saree", "memberId": null, "size": null, "condition": "fair", "quantity": 2 }"""));
        var pickup = await (await client.PostAsJsonAsync("/api/v1/pickups", itemised)).JsonAsync(HttpStatusCode.Created);
        Assert.Equal(1.95m, pickup.GetProperty("estimatedWeightKg").GetDecimal());
        ShapeAssert.Matches(ContractSamples.Section("user", "POST /pickups")[1].Json, pickup.GetProperty("items")[0]);
        Assert.Equal("shirt", pickup.GetProperty("items")[0].Str("garmentType"));

        var pieces = Booking();
        pieces["bulkMode"] = "pieces";
        pieces["declaredWeightKg"] = null;
        pieces["declaredPieces"] = 20;
        Assert.Equal(6.0m, (await (await client.PostAsJsonAsync("/api/v1/pickups", pieces)).JsonAsync(HttpStatusCode.Created))
            .GetProperty("estimatedWeightKg").GetDecimal());

        var unknown = Booking();
        unknown["method"] = "itemised";
        unknown["items"] = new JsonArray(JsonNode.Parse("""{ "garmentType": "spacesuit", "memberId": null, "size": null, "condition": "good", "quantity": 1 }"""));
        var error = await (await client.PostAsJsonAsync("/api/v1/pickups", unknown)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        Assert.True(error.GetProperty("details").TryGetProperty("items[0].garmentType", out _));
    }

    [Fact]
    public async Task Without_collectors_nearby_the_call_centre_can_take_it()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var kandy = await client.PostJsonAsync("/api/v1/addresses",
            new { label = "other", line = "5 Temple Road, Kandy", latitude = 7.29, longitude = 80.63, isDefault = false }, HttpStatusCode.Created);

        await (await client.PostAsJsonAsync("/api/v1/pickups", Booking(kandy.Str("id"))))
            .ErrorAsync(HttpStatusCode.Conflict, "NO_COLLECTORS_AVAILABLE");

        var rescue = Booking(kandy.Str("id"));
        rescue["fallbackToCallCentre"] = true;
        var pickup = await (await client.PostAsJsonAsync("/api/v1/pickups", rescue)).JsonAsync(HttpStatusCode.Created);
        Assert.Equal(PickupSource.CallCentre, await api.DbAsync(db => db.Pickups.Where(p => p.Id == pickup.Str("id")).Select(p => p.Source).SingleAsync()));

        // Later days aren't checked: who's online now says nothing about next week.
        await (await client.PostAsJsonAsync("/api/v1/pickups", Booking(kandy.Str("id"), "2026-11-10"))).JsonAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Bookings_need_a_valid_day_slot_and_address()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        api.Clock.Advance(TimeSpan.FromHours(1)); // 12:30 in Colombo: the morning has gone.

        async Task Field(JsonNode body, string field)
        {
            var error = await (await client.PostAsJsonAsync("/api/v1/pickups", body)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
            Assert.True(error.GetProperty("details").TryGetProperty(field, out _), $"no error for {field}: {error}");
        }

        await Field(Booking(slot: "morning"), "slot");
        await Field(Booking(date: "2026-11-30"), "scheduledDate");
        await Field(Booking(date: "2026-11-06"), "scheduledDate");
        await Field(Booking(addressId: "adr_2"), "addressId");
        var noWeight = Booking();
        noWeight["declaredWeightKg"] = null;
        await Field(noWeight, "declaredWeightKg");
    }

    [Fact]
    public async Task Customers_cancel_until_the_clothes_are_collected()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var pickup = await (await client.PostAsJsonAsync("/api/v1/pickups", Booking())).JsonAsync(HttpStatusCode.Created);

        var cancelled = await client.PostJsonAsync($"/api/v1/pickups/{pickup.Str("id")}/cancel", null);
        cancelled.MatchesContract("user", "GET /pickups/{id}");
        Assert.Equal("cancelled", cancelled.Str("status"));
        Assert.Equal("cancelled", cancelled.GetProperty("timeline").EnumerateArray().Last().Str("status"));
        await (await client.PostAsync($"/api/v1/pickups/{pickup.Str("id")}/cancel", null))
            .ErrorAsync(HttpStatusCode.Conflict, "PICKUP_NOT_CANCELLABLE");

        var cancelledList = await client.GetJsonAsync("/api/v1/pickups?group=cancelled");
        Assert.Contains(cancelledList.GetProperty("items").EnumerateArray(), p => p.Str("id") == pickup.Str("id"));
        await (await client.GetAsync("/api/v1/pickups?group=lost")).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Cancelling_an_assigned_pickup_tells_the_collector()
    {
        await using var api = await TestApi.StartAsync(server);
        var mobile = await api.DbAsync(db => db.Customers.Where(c => c.Id == "cus_9").Select(c => c.Mobile!).SingleAsync());
        var client = await api.CustomerAsync(mobile);
        await client.PostJsonAsync("/api/v1/pickups/RT-24826/cancel", null);
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n =>
            n.RecipientId == "col_466" && n.Type == "jobCancelled" && n.TargetId == "RT-24826")));

        var collectedOwner = await api.DbAsync(db => db.Customers.Where(c => c.Id == "cus_10").Select(c => c.Mobile!).SingleAsync());
        await (await (await api.CustomerAsync(collectedOwner)).PostAsync("/api/v1/pickups/RT-24790/cancel", null))
            .ErrorAsync(HttpStatusCode.Conflict, "PICKUP_NOT_CANCELLABLE");
    }

    [Fact]
    public async Task Other_customers_pickups_are_not_found()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        await (await client.GetAsync("/api/v1/pickups/RT-24822")).ErrorAsync(HttpStatusCode.NotFound, "PICKUP_NOT_FOUND");
        await (await client.PostAsync("/api/v1/pickups/RT-24822/cancel", null)).ErrorAsync(HttpStatusCode.NotFound, "PICKUP_NOT_FOUND");
    }

    [Fact]
    public async Task History_pages_with_a_cursor()
    {
        await using var api = await TestApi.StartAsync(server);
        var (customerId, mobile, count) = await api.DbAsync(async db =>
        {
            var top = await db.Pickups.Where(p => p.Status == PickupStatus.DeliveredToHub || p.Status == PickupStatus.Collected)
                .GroupBy(p => p.CustomerId).OrderByDescending(g => g.Count()).Select(g => new { g.Key, Count = g.Count() }).FirstAsync();
            var phone = await db.Customers.Where(c => c.Id == top.Key).Select(c => c.Mobile!).SingleAsync();
            return (top.Key, phone, top.Count);
        });
        Assert.True(count > 0, customerId);
        var client = await api.CustomerAsync(mobile);

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await client.GetJsonAsync($"/api/v1/pickups?group=completed{(cursor is null ? "" : $"&cursor={cursor}")}");
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(p => p.Str("id")));
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);

        Assert.Equal(count, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }
}
