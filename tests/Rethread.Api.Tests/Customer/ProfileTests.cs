using System.Net;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Customer;

public sealed class ProfileTests(PostgresServer server)
{
    [Fact]
    public async Task Customers_read_and_update_their_profile()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync("+94700000001");

        var me = await client.GetJsonAsync("/api/v1/me");
        me.MatchesContract("user", "GET /me");
        Assert.False(me.GetProperty("hasBankDetails").GetBoolean());

        var updated = await (await client.PatchAsJsonAsync("/api/v1/me",
            new { firstName = " Nimali ", lastName = "Perera", language = "si" })).JsonAsync();
        updated.MatchesContract("user", "GET /me");
        Assert.Equal(("Nimali", "Perera", "si"), (updated.Str("firstName"), updated.Str("lastName"), updated.Str("language")));

        await (await client.PatchAsJsonAsync("/api/v1/me", new { language = "fr" }))
            .ErrorAsync(HttpStatusCode.BadRequest, "BAD_REQUEST");
        await (await client.PatchAsJsonAsync("/api/v1/me", new { firstName = "" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Addresses_keep_exactly_one_default()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();

        var list = await client.GetJsonAsync("/api/v1/addresses");
        list.MatchesContract("user", "GET /addresses");
        var home = list.GetProperty("items")[0];
        Assert.True(home.GetProperty("isDefault").GetBoolean());

        var work = await client.PostJsonAsync("/api/v1/addresses",
            new { label = "work", line = "21 Duplication Rd, Colombo 03", latitude = 6.91, longitude = 79.85, isDefault = true },
            HttpStatusCode.Created);
        ShapeAssert.Matches(ContractSamples.Response("user", "GET /addresses").GetProperty("items")[0], work);
        Assert.Equal("Kollupitiya", await api.DbAsync(db => db.Addresses.Where(a => a.Id == work.Str("id")).Select(a => a.Area).SingleAsync()));

        list = await client.GetJsonAsync("/api/v1/addresses");
        Assert.Equal(1, list.GetProperty("items").EnumerateArray().Count(a => a.GetProperty("isDefault").GetBoolean()));
        Assert.Equal(work.Str("id"), list.GetProperty("items")[0].Str("id"));

        await (await client.PatchAsJsonAsync($"/api/v1/addresses/{work.Str("id")}", new { isDefault = false }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        var renamed = await (await client.PatchAsJsonAsync($"/api/v1/addresses/{work.Str("id")}", new { label = "other" })).JsonAsync();
        Assert.Equal("other", renamed.Str("label"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/addresses/{work.Str("id")}")).StatusCode);
        list = await client.GetJsonAsync("/api/v1/addresses");
        Assert.True(list.GetProperty("items")[0].GetProperty("isDefault").GetBoolean());

        await (await client.DeleteAsync($"/api/v1/addresses/{work.Str("id")}")).ErrorAsync(HttpStatusCode.NotFound, "ADDRESS_NOT_FOUND");
        // Someone else's address is invisible.
        var other = await api.CustomerAsync("+94764824761");
        await (await other.DeleteAsync($"/api/v1/addresses/{home.Str("id")}")).ErrorAsync(HttpStatusCode.NotFound, "ADDRESS_NOT_FOUND");
    }

    [Fact]
    public async Task The_first_address_becomes_the_default()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync("+94700000002");
        var first = await client.PostJsonAsync("/api/v1/addresses",
            new { label = "home", line = "5 Temple Road, Kandy", latitude = 7.29, longitude = 80.63, isDefault = false },
            HttpStatusCode.Created);
        Assert.True(first.GetProperty("isDefault").GetBoolean());
        Assert.Equal("Kandy", await api.DbAsync(db => db.Addresses.Where(a => a.Id == first.Str("id")).Select(a => a.Area).SingleAsync()));
        await (await client.PostAsJsonAsync("/api/v1/addresses",
                new { label = "home", line = "", latitude = 51.5, longitude = 0.1, isDefault = false }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Family_members_get_sizes_from_their_measurements()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync("+94700000003");
        var sample = ContractSamples.Response("user", "GET /family-members").GetProperty("items")[0];

        var created = await client.PostJsonAsync("/api/v1/family-members", new
        {
            name = "Nimali Perera",
            relation = "self",
            gender = "female",
            ageGroup = "adult",
            heightCm = 160,
            chestCm = 86,
            waistCm = 71,
            hipCm = 94,
            shoeSize = "38",
            topSize = "XXL",
        }, HttpStatusCode.Created);
        ShapeAssert.Matches(sample, created);
        Assert.Equal(("M", "28"), (created.Str("topSize"), created.Str("bottomSize")));

        var child = await client.PostJsonAsync("/api/v1/family-members",
            new { name = "Sahan", relation = "son", gender = "male", ageGroup = "child", heightCm = 115 }, HttpStatusCode.Created);
        Assert.Equal(("6-7Y", "6-7Y"), (child.Str("topSize"), child.Str("bottomSize")));

        var grown = await (await client.PatchAsJsonAsync($"/api/v1/family-members/{child.Str("id")}", new { heightCm = 128 })).JsonAsync();
        Assert.Equal("8-9Y", grown.Str("topSize"));

        var list = await client.GetJsonAsync("/api/v1/family-members");
        list.MatchesContract("user", "GET /family-members");
        Assert.Equal("self", list.GetProperty("items")[0].Str("relation"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/family-members/{child.Str("id")}")).StatusCode);
        var error = await (await client.PostAsJsonAsync("/api/v1/family-members", new { name = "No relation" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        Assert.True(error.GetProperty("details").TryGetProperty("relation", out _));
    }

    [Fact]
    public async Task Bank_details_are_stored_encrypted_and_shown_masked()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync("+94700000004");
        await (await client.GetAsync("/api/v1/wallet/bank-details")).ErrorAsync(HttpStatusCode.NotFound, "BANK_DETAILS_NOT_FOUND");

        var saved = await (await client.PutAsJsonAsync("/api/v1/wallet/bank-details",
            ContractSamples.Request("user", "PUT /wallet/bank-details"))).JsonAsync();
        saved.MatchesContract("user", "GET /wallet/bank-details");
        Assert.Equal("•••• 4821", saved.Str("accountNumberMasked"));

        var stored = await api.DbAsync(db => db.BankDetails
            .SingleAsync(b => db.Customers.Any(c => c.Id == b.CustomerId && c.Mobile == "+94700000004")));
        Assert.DoesNotContain("8001234821", stored.AccountNumberProtected, StringComparison.Ordinal);
        Assert.True((await client.GetJsonAsync("/api/v1/me")).GetProperty("hasBankDetails").GetBoolean());

        await (await client.PutAsJsonAsync("/api/v1/wallet/bank-details",
                new { bankName = "HNB", branch = "Dehiwala", accountName = "Nimali", accountNumber = "12-34" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/wallet/bank-details")).StatusCode);
        await (await client.GetAsync("/api/v1/wallet/bank-details")).ErrorAsync(HttpStatusCode.NotFound, "BANK_DETAILS_NOT_FOUND");
    }

    [Fact]
    public async Task Customers_read_and_clear_their_notifications()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();

        var page = await client.GetJsonAsync("/api/v1/notifications");
        page.MatchesContract("user", "GET /notifications");
        var first = page.GetProperty("items")[0];
        Assert.Equal("collectorEnRoute", first.Str("type"));
        Assert.Equal("RT-24817", first.Str("targetId"));
        var unread = page.GetProperty("unreadCount").GetInt32();
        Assert.True(unread > 0);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/notifications/{first.Str("id")}/read", null)).StatusCode);
        Assert.Equal(unread - 1, (await client.GetJsonAsync("/api/v1/notifications")).GetProperty("unreadCount").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await client.GetJsonAsync("/api/v1/notifications")).GetProperty("unreadCount").GetInt32());

        await (await client.PostAsync("/api/v1/notifications/ntf_nope/read", null)).ErrorAsync(HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
    }
}
