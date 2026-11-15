using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Data.Entities;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Customer;

public sealed class ProfileTests(PostgresServer server)
{
    private const string Contract = "user-next";
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1];

    private static JsonNode AddressSample() => JsonNode.Parse(ContractSamples.Request(Contract, "POST /addresses").GetRawText())!;

    [Fact]
    public async Task Completing_the_profile_sets_the_name_and_creates_the_own_family_profile()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync("+94700000001");

        var me = await client.GetJsonAsync("/api/v1/me");
        me.MatchesContract(Contract, "GET /me");
        Assert.Equal(JsonValueKind.Null, me.GetProperty("fullName").ValueKind);

        var saved = await (await client.PutAsJsonAsync("/api/v1/me",
            ContractSamples.Request(Contract, "PUT /me"))).JsonAsync();
        saved.MatchesContract(Contract, "GET /me");
        Assert.Equal(("Nimali Perera", "nimali.perera@gmail.com", "si"), (saved.Str("fullName"), saved.Str("email"), saved.Str("preferredLanguage")));

        var family = await client.GetJsonAsync("/api/v1/family-members");
        Assert.Equal(("self", "Nimali Perera"), (family.GetProperty("items")[0].Str("relation"), family.GetProperty("items")[0].Str("name")));
        Assert.Single(family.GetProperty("items").EnumerateArray());

        // Contacts change only through the verified flow once set.
        await (await client.PutAsJsonAsync("/api/v1/me", new { fullName = "Nimali", email = "other@example.lk", preferredLanguage = "en" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        await (await client.PutAsJsonAsync("/api/v1/me", new { fullName = "", preferredLanguage = "en" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        var other = await api.CustomerAsync("+94700000002");
        await (await other.PutAsJsonAsync("/api/v1/me", new { fullName = "Someone", email = "nimali.perera@gmail.com", preferredLanguage = "en" }))
            .ErrorAsync(HttpStatusCode.Conflict, "CONTACT_IN_USE");
    }

    [Fact]
    public async Task Contacts_change_with_a_code_sent_to_the_new_one()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var request = await client.PostJsonAsync("/api/v1/me/contact-change/request", new { channel = "sms", newDestination = "+94771239876" });
        request.MatchesContract(Contract, "POST /me/contact-change/request");
        Assert.StartsWith("cc_", request.Str("requestId"), StringComparison.Ordinal);

        // A contact-change code can't sign anyone in.
        await (await api.Client().PostAsJsonAsync("/api/v1/auth/otp/verify", new { requestId = request.Str("requestId"), code = SignIn.Code }))
            .ErrorAsync(HttpStatusCode.BadRequest, "OTP_EXPIRED");
        var me = await client.PostJsonAsync("/api/v1/me/contact-change/verify", new { requestId = request.Str("requestId"), code = SignIn.Code });
        Assert.Equal("+94771239876", me.Str("mobile"));

        await (await client.PostAsJsonAsync("/api/v1/me/contact-change/request", new { channel = "sms", newDestination = "+94764824761" }))
            .ErrorAsync(HttpStatusCode.Conflict, "CONTACT_IN_USE");
    }

    [Fact]
    public async Task Avatars_upload_and_are_served()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var me = await (await client.PostAsync("/api/v1/me/avatar", new MultipartFormDataContent { { new ByteArrayContent(Jpeg), "file", "me.jpg" } })).JsonAsync();
        var url = me.Str("avatarUrl");
        Assert.Equal(HttpStatusCode.OK, (await api.Client().GetAsync(new Uri(url).AbsolutePath)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await (await client.DeleteAsync("/api/v1/me/avatar")).JsonAsync()).GetProperty("avatarUrl").ValueKind);
    }

    [Fact]
    public async Task Deleting_the_account_needs_a_code_and_nothing_outstanding()
    {
        await using var api = await TestApi.StartAsync(server);
        var nadeesha = await api.CustomerAsync();
        api.Clock.Advance(TimeSpan.FromSeconds(61)); // Past the resend wait from signing in.
        async Task<string> CodeFor(string mobile) =>
            (await api.Client().PostJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = mobile })).Str("requestId");

        var error = await (await nadeesha.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me")
        {
            Content = JsonContent.Create(new { reason = "privacy", otpRequestId = await CodeFor(SignIn.NadeeshaMobile), otpCode = SignIn.Code }),
        })).ErrorAsync(HttpStatusCode.Conflict, "ACTIVE_PICKUPS");
        Assert.True(error.GetProperty("details").GetProperty("count").GetInt32() > 0);

        var fresh = await api.CustomerAsync("+94700000003");
        await fresh.PutAsJsonAsync("/api/v1/me", new { fullName = "Short Stay", preferredLanguage = "en" });
        api.Clock.Advance(TimeSpan.FromMinutes(2));
        var requestId = await CodeFor("+94700000003");
        Assert.Equal(HttpStatusCode.NoContent, (await fresh.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me")
        {
            Content = JsonContent.Create(new { reason = "movingAway", otpRequestId = requestId, otpCode = SignIn.Code }),
        })).StatusCode);
        var gone = await api.DbAsync(db => db.Customers.SingleAsync(c => c.FirstName == "Deleted" && c.DeletedAt != null));
        Assert.Null(gone.Mobile);
        await (await fresh.GetAsync("/api/v1/me")).ErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task Addresses_keep_exactly_one_default()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();

        var list = await client.GetJsonAsync("/api/v1/addresses");
        list.MatchesContract(Contract, "GET /addresses");
        var home = list.GetProperty("items")[0];
        Assert.Equal("Dehiwala", home.Str("city"));

        var body = AddressSample();
        var grandma = await (await client.PostAsJsonAsync("/api/v1/addresses", body)).JsonAsync(HttpStatusCode.Created);
        ShapeAssert.Matches(ContractSamples.Response(Contract, "GET /addresses").GetProperty("items")[0], grandma);
        Assert.Equal(("Grandma's", false), (grandma.Str("customLabel"), grandma.GetProperty("isDefault").GetBoolean()));

        var made = await client.PostJsonAsync($"/api/v1/addresses/{grandma.Str("id")}/default", null);
        Assert.True(made.GetProperty("isDefault").GetBoolean());
        body["isDefault"] = false;
        body["notes"] = "Blue door";
        var replaced = await (await client.PutAsJsonAsync($"/api/v1/addresses/{grandma.Str("id")}", body)).JsonAsync();
        Assert.Equal(("Blue door", true), (replaced.Str("notes"), replaced.GetProperty("isDefault").GetBoolean()));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/addresses/{grandma.Str("id")}")).StatusCode);
        Assert.True((await client.GetJsonAsync("/api/v1/addresses")).GetProperty("items")[0].GetProperty("isDefault").GetBoolean());
        await (await client.DeleteAsync($"/api/v1/addresses/{grandma.Str("id")}")).ErrorAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        // RT-24817 is on its way to the home address.
        await (await client.DeleteAsync($"/api/v1/addresses/{home.Str("id")}")).ErrorAsync(HttpStatusCode.Conflict, "ADDRESS_IN_USE");

        body["lat"] = 51.5;
        await (await client.PostAsJsonAsync("/api/v1/addresses", body)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "OUTSIDE_SERVICE_AREA");
        body["lat"] = 6.03;
        body["city"] = "";
        var error = await (await client.PostAsJsonAsync("/api/v1/addresses", body)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        Assert.True(error.GetProperty("details").TryGetProperty("city", out _));
    }

    [Fact]
    public async Task Family_members_follow_the_self_rules()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var list = await client.GetJsonAsync("/api/v1/family-members");
        list.MatchesContract(Contract, "GET /family-members");
        var self = list.GetProperty("items")[0];
        Assert.Equal("self", self.Str("relation"));

        var mother = await (await client.PostAsJsonAsync("/api/v1/family-members",
            ContractSamples.Request(Contract, "POST /family-members"))).JsonAsync(HttpStatusCode.Created);
        ShapeAssert.Matches(ContractSamples.Response(Contract, "GET /family-members").GetProperty("items")[0], mother);

        var body = JsonNode.Parse(ContractSamples.Request(Contract, "POST /family-members").GetRawText())!;
        body["heightCm"] = 999;
        var error = await (await client.PostAsJsonAsync("/api/v1/family-members", body)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        Assert.DoesNotContain("999", error.GetProperty("details").Str("heightCm"), StringComparison.Ordinal);
        body["heightCm"] = 150;
        body["relation"] = "self";
        await (await client.PostAsJsonAsync("/api/v1/family-members", body)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        await (await client.PutAsJsonAsync($"/api/v1/family-members/{mother.Str("id")}", body)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        body["relation"] = "mother";
        body["shoeSize"] = "38.5";
        Assert.Equal("38.5", (await (await client.PutAsJsonAsync($"/api/v1/family-members/{mother.Str("id")}", body)).JsonAsync()).Str("shoeSize"));

        await (await client.DeleteAsync($"/api/v1/family-members/{self.Str("id")}")).ErrorAsync(HttpStatusCode.Forbidden, "CANNOT_DELETE_SELF");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/family-members/{mother.Str("id")}")).StatusCode);
        await (await client.DeleteAsync($"/api/v1/family-members/{mother.Str("id")}")).ErrorAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task The_catalog_lists_garment_types_and_size_charts()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var types = await client.GetJsonAsync("/api/v1/catalog/garment-types");
        types.MatchesContract(Contract, "GET /catalog/garment-types");
        Assert.Equal(("shirt", "chest"), (types.GetProperty("items")[1].Str("id"), types.GetProperty("items")[1].Str("sizeBasis")));
        var charts = await client.GetJsonAsync("/api/v1/catalog/size-charts");
        charts.MatchesContract(Contract, "GET /catalog/size-charts");
        var adultChest = charts.GetProperty("items").EnumerateArray().Single(c => c.Str("sizeBasis") == "chest" && c.Str("ageGroup") == "adult");
        Assert.Equal("M", adultChest.GetProperty("ranges").EnumerateArray().Single(r => r.GetProperty("minCm").GetInt32() == 88).Str("size"));
    }

    [Fact]
    public async Task Bank_details_are_stored_encrypted_and_shown_masked()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync("+94700000004");
        await (await client.GetAsync("/api/v1/wallet/bank-details")).ErrorAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        var saved = await (await client.PutAsJsonAsync("/api/v1/wallet/bank-details",
            ContractSamples.Request(Contract, "PUT /wallet/bank-details"))).JsonAsync();
        saved.MatchesContract(Contract, "GET /wallet/bank-details");
        var stored = await api.DbAsync(db => db.BankDetails
            .SingleAsync(b => db.Customers.Any(c => c.Id == b.CustomerId && c.Mobile == "+94700000004")));
        Assert.DoesNotContain("8001234821", stored.AccountNumberProtected, StringComparison.Ordinal);
        Assert.True((await client.GetJsonAsync("/api/v1/me")).GetProperty("hasBankDetails").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/wallet/bank-details")).StatusCode);
    }

    [Fact]
    public async Task Seeded_family_age_groups_use_the_apps_set()
    {
        await using var api = await TestApi.StartAsync(server);
        var groups = await api.DbAsync(db => db.FamilyMembers.Select(m => m.AgeGroup).Distinct().ToListAsync());
        Assert.All(groups, g => Assert.Contains(g, new[] { AgeGroup.Adult, AgeGroup.Teen, AgeGroup.Kid }));
    }
}
