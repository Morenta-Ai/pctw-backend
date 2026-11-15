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
    private const string Contract = "user-next";

    /// <summary>The contract's bulk sample, on Sunday to Tuesday after the test clock's Saturday.</summary>
    private static JsonNode Booking(string addressId = "adr_1")
    {
        var body = JsonNode.Parse(ContractSamples.Request(Contract, "POST /pickups").GetRawText())!;
        body["addressId"] = addressId;
        body["preferredDates"] = Dates(("2026-11-08", "morning"), ("2026-11-09", "afternoon"), ("2026-11-10", "evening"));
        return body;
    }

    private static JsonArray Dates(params (string Date, string Slot)[] dates) =>
        new(dates.Select((d, i) => (JsonNode)new JsonObject { ["priority"] = i + 1, ["date"] = d.Date, ["slotId"] = d.Slot }).ToArray());

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, JsonNode body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pickups") { Content = JsonContent.Create(body) };
        if (key != "") request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private static async Task<HttpClient> CustomerAsync(TestApi api, string id)
    {
        var mobile = await api.DbAsync(db => db.Customers.Where(c => c.Id == id).Select(c => c.Mobile!).SingleAsync());
        return await api.CustomerAsync(mobile);
    }

    [Fact]
    public async Task Config_and_availability_come_from_the_server()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var config = await client.GetJsonAsync("/api/v1/pickups/config");
        config.MatchesContract(Contract, "GET /pickups/config");
        Assert.Equal((5, 300, 12000L, 3), (config.GetProperty("minPieces").GetInt32(), config.GetProperty("maxPieces").GetInt32(),
            config.GetProperty("ratePerKgCents").GetInt64(), config.GetProperty("preferredDatesRequired").GetInt32()));

        var availability = await client.GetJsonAsync("/api/v1/pickups/availability?from=2026-11-07&days=14");
        availability.MatchesContract(Contract, "GET /pickups/availability");
        Assert.Equal(14, availability.GetProperty("items").GetArrayLength());
        var today = availability.GetProperty("items")[0].GetProperty("slots").EnumerateArray()
            .ToDictionary(s => s.Str("id"), s => s.GetProperty("available").GetBoolean());
        Assert.Equal((false, false, true), (today["morning"], today["afternoon"], today["evening"]));
        Assert.True(availability.GetProperty("items")[1].GetProperty("slots")[0].GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Booking_offers_three_dates_and_a_retry_returns_the_same_pickup()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var key = Guid.NewGuid().ToString();

        var pickup = await (await PostAsync(client, Booking(), key)).JsonAsync(HttpStatusCode.Created);
        pickup.MatchesContract(Contract, "GET /pickups/{id}");
        Assert.Equal(("awaitingConfirmation", 25, 3), (pickup.Str("status"), pickup.GetProperty("pieceCount").GetInt32(), pickup.GetProperty("bagCount").GetInt32()));
        Assert.Equal(JsonValueKind.Null, pickup.GetProperty("confirmedDate").ValueKind);
        Assert.Equal(["Morning", "Afternoon", "Evening"], pickup.GetProperty("preferredDates").EnumerateArray().Select(d => d.GetProperty("slot").Str("label")));
        Assert.Equal(("home", "Gate code 4412"), (pickup.Str("addressLabel"), pickup.Str("notes")));

        var again = await (await PostAsync(client, Booking(), key)).JsonAsync(HttpStatusCode.Created);
        Assert.Equal(pickup.Str("id"), again.Str("id"));
        var changed = Booking();
        changed["pieceCount"] = 40;
        await (await PostAsync(client, changed, key)).ErrorAsync(HttpStatusCode.Conflict, "DUPLICATE_REQUEST");
        await (await PostAsync(client, Booking(), "")).ErrorAsync(HttpStatusCode.BadRequest, "BAD_REQUEST");

        var stored = await api.DbAsync(db => db.Pickups.SingleAsync(p => p.Id == pickup.Str("id")));
        Assert.Equal((new DateOnly(2026, 11, 8), Slot.Morning, 7.5m, PickupSource.App), (stored.ScheduledDate, stored.Slot, stored.EstimatedKg, stored.Source));
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.Id == $"ntf_pickupRequested_{stored.Id}")));
    }

    [Fact]
    public async Task Itemised_requests_need_five_known_garments()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var body = JsonNode.Parse(ContractSamples.Section(Contract, "POST /pickups")[1].Json.GetRawText())!;
        body["addressId"] = "adr_1";
        body["preferredDates"] = Dates(("2026-11-08", "morning"), ("2026-11-09", "afternoon"), ("2026-11-10", "evening"));
        foreach (var item in body["items"]!.AsArray()) item!["familyMemberId"] = null;

        var pickup = await (await PostAsync(client, body)).JsonAsync(HttpStatusCode.Created);
        Assert.Equal(("itemised", 6), (pickup.Str("mode"), pickup.GetProperty("items").EnumerateArray().Sum(i => i.GetProperty("quantity").GetInt32())));
        Assert.Equal("worn", pickup.GetProperty("items")[1].Str("condition"));
        Assert.Equal(2.2m, await api.DbAsync(db => db.Pickups.Where(p => p.Id == pickup.Str("id")).Select(p => p.EstimatedKg).SingleAsync()));

        body["items"]![0]!["quantity"] = 1;
        var error = await (await PostAsync(client, body)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "BELOW_MINIMUM");
        Assert.Equal(5, error.GetProperty("details").GetProperty("minItems").GetInt32());
        body["items"]![0]!["quantity"] = 4;
        body["items"]![0]!["garmentTypeId"] = "spacesuit";
        await (await PostAsync(client, body)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Bad_dates_slots_counts_and_addresses_are_refused()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();

        async Task Refused(JsonNode body, HttpStatusCode status, string code) =>
            await (await PostAsync(client, body)).ErrorAsync(status, code);

        var two = Booking();
        two["preferredDates"] = Dates(("2026-11-08", "morning"), ("2026-11-09", "morning"));
        await Refused(two, HttpStatusCode.UnprocessableEntity, "INVALID_PREFERRED_DATES");
        var sameDay = Booking();
        sameDay["preferredDates"] = Dates(("2026-11-08", "morning"), ("2026-11-08", "evening"), ("2026-11-09", "morning"));
        await Refused(sameDay, HttpStatusCode.UnprocessableEntity, "INVALID_PREFERRED_DATES");
        var tooFar = Booking();
        tooFar["preferredDates"] = Dates(("2026-11-08", "morning"), ("2026-11-09", "morning"), ("2026-12-01", "morning"));
        await Refused(tooFar, HttpStatusCode.UnprocessableEntity, "INVALID_PREFERRED_DATES");
        var closedSlot = Booking();
        closedSlot["preferredDates"] = Dates(("2026-11-07", "afternoon"), ("2026-11-09", "morning"), ("2026-11-10", "morning"));
        var error = await (await PostAsync(client, closedSlot)).ErrorAsync(HttpStatusCode.Conflict, "SLOT_UNAVAILABLE");
        Assert.Equal("afternoon", error.GetProperty("details").Str("slotId"));

        var few = Booking();
        few["pieceCount"] = 3;
        await Refused(few, HttpStatusCode.UnprocessableEntity, "BELOW_MINIMUM");
        few["pieceCount"] = 400;
        await Refused(few, HttpStatusCode.UnprocessableEntity, "BELOW_MINIMUM");
        await Refused(Booking("adr_2"), HttpStatusCode.UnprocessableEntity, "ADDRESS_REQUIRED");
    }

    [Fact]
    public async Task Without_collectors_nearby_the_call_centre_can_take_it()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var kandy = await client.PostJsonAsync("/api/v1/addresses",
            new { label = "other", addressLine = "5 Temple Road", city = "Kandy", lat = 7.29, lng = 80.63, isDefault = false }, HttpStatusCode.Created);
        await (await PostAsync(client, Booking(kandy.Str("id")))).ErrorAsync(HttpStatusCode.Conflict, "NO_COLLECTORS_AVAILABLE");
        var rescue = Booking(kandy.Str("id"));
        rescue["fallbackToCallCentre"] = true;
        var pickup = await (await PostAsync(client, rescue)).JsonAsync(HttpStatusCode.Created);
        Assert.Equal(PickupSource.CallCentre, await api.DbAsync(db => db.Pickups.Where(p => p.Id == pickup.Str("id")).Select(p => p.Source).SingleAsync()));
    }

    [Fact]
    public async Task A_pickup_on_its_way_is_confirmed_and_trackable()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var pickup = await client.GetJsonAsync("/api/v1/pickups/RT-24817");
        pickup.MatchesContract(Contract, "GET /pickups/{id}");
        Assert.Equal(("enRoute", "2026-11-07", "morning"), (pickup.Str("status"), pickup.GetProperty("confirmedDate").Str("date"),
            pickup.GetProperty("confirmedDate").Str("slotId")));
        Assert.Equal(["awaitingConfirmation", "confirmed", "enRoute"], pickup.GetProperty("statusHistory").EnumerateArray().Select(s => s.Str("status")));
        Assert.Equal(("Ruwan Silva", "Three-wheeler"), (pickup.GetProperty("collector").Str("name"), pickup.GetProperty("collector").Str("vehicleType")));

        var tracking = await client.GetJsonAsync("/api/v1/pickups/RT-24817/tracking");
        tracking.MatchesContract(Contract, "GET /pickups/{id}/tracking");
        Assert.True(tracking.GetProperty("etaMinutes").GetInt32() > 0);

        var cus10 = await CustomerAsync(api, "cus_10");
        var collected = await cus10.GetJsonAsync("/api/v1/pickups/RT-24790");
        Assert.Equal(("collected", 6.2m, 74400L, "bluetoothScale"), (collected.Str("status"), collected.GetProperty("weighing").GetProperty("weightKg").GetDecimal(),
            collected.GetProperty("weighing").GetProperty("earningsCents").GetInt64(), collected.GetProperty("weighing").Str("method")));
        var still = await cus10.GetJsonAsync("/api/v1/pickups/RT-24790/tracking");
        Assert.Equal(JsonValueKind.Null, still.GetProperty("collectorLocation").ValueKind);
    }

    [Fact]
    public async Task Lists_are_paged_by_tab()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var upcoming = await client.GetJsonAsync("/api/v1/pickups?tab=upcoming&page=1&pageSize=20");
        upcoming.MatchesContract(Contract, "GET /pickups");
        Assert.Contains(upcoming.GetProperty("items").EnumerateArray(), p => p.Str("id") == "RT-24817");
        await (await client.GetAsync("/api/v1/pickups?tab=lost")).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");

        var (id, count) = await api.DbAsync(async db =>
        {
            var top = await db.Pickups.Where(p => p.Status == PickupStatus.DeliveredToHub).GroupBy(p => p.CustomerId)
                .OrderByDescending(g => g.Count()).Select(g => new { g.Key, Count = g.Count() }).FirstAsync();
            return (top.Key, top.Count);
        });
        api.Clock.Advance(TimeSpan.FromSeconds(61)); // In case the busiest customer is the one signed in above.
        var rich = await CustomerAsync(api, id);
        var seen = new List<string>();
        for (var page = 1; ; page++)
        {
            var result = await rich.GetJsonAsync($"/api/v1/pickups?tab=completed&page={page}&pageSize=2");
            seen.AddRange(result.GetProperty("items").EnumerateArray().Select(p => p.Str("id")));
            if (!result.GetProperty("hasMore").GetBoolean()) break;
        }

        Assert.Equal(count, seen.Distinct().Count());
    }

    [Fact]
    public async Task Customers_cancel_until_a_collector_sets_off()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var pickup = await (await PostAsync(client, Booking())).JsonAsync(HttpStatusCode.Created);

        var cancelled = await client.PostJsonAsync($"/api/v1/pickups/{pickup.Str("id")}/cancel", new { reason = "changedMind" });
        Assert.Equal(("cancelled", "changedMind"), (cancelled.Str("status"), cancelled.Str("cancelReason")));
        await (await client.PostAsJsonAsync($"/api/v1/pickups/{pickup.Str("id")}/cancel", new { reason = "other" })).ErrorAsync(HttpStatusCode.Conflict, "CANNOT_CANCEL");
        await (await client.PostAsJsonAsync("/api/v1/pickups/RT-24817/cancel", new { reason = "other" })).ErrorAsync(HttpStatusCode.Conflict, "CANNOT_CANCEL");
        await (await client.PostAsJsonAsync("/api/v1/pickups/RT-24822/cancel", new { reason = "other" })).ErrorAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Contains((await client.GetJsonAsync("/api/v1/pickups?tab=cancelled")).GetProperty("items").EnumerateArray(), p => p.Str("id") == pickup.Str("id"));

        var confirmed = await CustomerAsync(api, "cus_9");
        await (await confirmed.PostAsJsonAsync("/api/v1/pickups/RT-24826/cancel", new { reason = "silly" })).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        await confirmed.PostJsonAsync("/api/v1/pickups/RT-24826/cancel", new { reason = "notReady" });
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.RecipientId == "col_466" && n.Type == "jobCancelled")));
    }

    [Fact]
    public async Task A_collector_taking_it_confirms_the_date_staff_agreed()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        var detail = await staff.GetJsonAsync("/api/v1/admin/pickups/RT-24844");
        Assert.Equal(3, detail.GetProperty("preferredDates").GetArrayLength());
        var moved = await staff.PostJsonAsync("/api/v1/admin/pickups/RT-24844/schedule", new { date = "2026-11-08", slot = "afternoon" });
        Assert.Equal(("2026-11-08", "afternoon"), (moved.Str("scheduledDate"), moved.Str("slot")));
        await (await staff.PostAsJsonAsync("/api/v1/admin/pickups/RT-24817/schedule", new { date = "2026-11-08", slot = "afternoon" }))
            .ErrorAsync(HttpStatusCode.Conflict, "PICKUP_ALREADY_ASSIGNED");

        var offer = await staff.PostJsonAsync("/api/v1/admin/pickups/RT-24844/assignments", new { collectorId = "col_515" }, HttpStatusCode.Created);
        var mobile = await api.DbAsync(db => db.Collectors.Where(c => c.Code == "COL-0515").Select(c => c.Mobile).SingleAsync());
        await (await api.CollectorAsync("COL-0515", mobile)).PostJsonAsync($"/api/v1/collector/assignments/{offer.Str("id")}/accept", null);

        var customer = await CustomerAsync(api, "cus_7");
        var pickup = await customer.GetJsonAsync("/api/v1/pickups/RT-24844");
        Assert.Equal(("confirmed", "2026-11-08", "Afternoon"), (pickup.Str("status"), pickup.GetProperty("confirmedDate").Str("date"),
            pickup.GetProperty("confirmedDate").Str("slotLabel")));
        var note = await api.DbAsync(db => db.Notifications.SingleAsync(n => n.RecipientId == "cus_7" && n.Type == "pickupDateConfirmed"));
        Assert.Equal("Your pickup is confirmed for Sun, 8 Nov · Afternoon.", note.Body);
    }
}
