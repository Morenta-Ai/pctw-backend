using System.Net;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Data.Entities;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Wallet;

public sealed class WalletTests(PostgresServer server)
{
    private static async Task<(HttpClient Client, string CustomerId)> RedeemerAsync(TestApi api, string redemptionId)
    {
        var (id, mobile) = await api.DbAsync(async db =>
        {
            var r = await db.Redemptions.Include(x => x.Customer).SingleAsync(x => x.Id == redemptionId);
            return (r.CustomerId, r.Customer!.Mobile!);
        });
        return (await api.CustomerAsync(mobile), id);
    }

    private static object Cash(long amount) => new { type = "cash", amountCents = amount };

    [Fact]
    public async Task The_wallet_shows_the_balance_pending_transfers_and_history()
    {
        await using var api = await TestApi.StartAsync(server);
        var (client, _) = await RedeemerAsync(api, "red_1");

        var wallet = await client.GetJsonAsync("/api/v1/wallet");
        wallet.MatchesContract("user", "GET /wallet");
        Assert.Equal((200000L, 12000L, "LKR"), (wallet.GetProperty("pendingCents").GetInt64(),
            wallet.GetProperty("ratePerKgCents").GetInt64(), wallet.Str("currency")));
        Assert.True(wallet.GetProperty("balanceCents").GetInt64() > 0);

        var history = await client.GetJsonAsync("/api/v1/wallet/transactions");
        history.MatchesContract("user", "GET /wallet/transactions");
        var transfer = history.GetProperty("items").EnumerateArray().Single(t => t.Str("type") == "bankTransfer");
        Assert.Equal(("pending", -200000L), (transfer.Str("status"), transfer.GetProperty("amountCents").GetInt64()));
        Assert.Contains(history.GetProperty("items").EnumerateArray(), t => t.Str("type") == "pickupEarning" && t.Str("pickupId").StartsWith("RT-", StringComparison.Ordinal));

        (await client.GetJsonAsync("/api/v1/wallet/voucher-brands")).MatchesContract("user", "GET /wallet/voucher-brands");
        var projects = await client.GetJsonAsync("/api/v1/wallet/donation-projects");
        projects.MatchesContract("user", "GET /wallet/donation-projects");
        Assert.DoesNotContain(projects.GetProperty("items").EnumerateArray(), p => p.Str("id") == "dp_flood_relief");
    }

    [Fact]
    public async Task Cash_needs_bank_details_a_minimum_and_the_balance()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync(); // Nadeesha has earnings but no bank details yet.
        await (await client.PostAsJsonAsync("/api/v1/wallet/redemptions", Cash(60000))).ErrorAsync(HttpStatusCode.Conflict, "BANK_DETAILS_REQUIRED");

        await client.PutAsJsonAsync("/api/v1/wallet/bank-details",
            new { bankName = "HNB", branch = "Dehiwala", accountName = "Nadeesha Perera", accountNumber = "0012345678" });
        await (await client.PostAsJsonAsync("/api/v1/wallet/redemptions", Cash(40000))).ErrorAsync(HttpStatusCode.UnprocessableEntity, "BELOW_MINIMUM_AMOUNT");

        var before = (await client.GetJsonAsync("/api/v1/wallet")).GetProperty("balanceCents").GetInt64();
        var result = await client.PostJsonAsync("/api/v1/wallet/redemptions", Cash(60000), HttpStatusCode.Created);
        result.MatchesContract("user", "POST /wallet/redemptions");
        Assert.Equal(("pending", before - 60000), (result.Str("status"), result.GetProperty("balanceCents").GetInt64()));
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.Id == $"ntf_transferRequested_{result.Str("id")}")));

        var error = await (await client.PostAsJsonAsync("/api/v1/wallet/redemptions", Cash(before)))
            .ErrorAsync(HttpStatusCode.Conflict, "INSUFFICIENT_BALANCE");
        Assert.Equal(before - 60000, error.GetProperty("details").GetProperty("balanceCents").GetInt64());
    }

    [Fact]
    public async Task Two_redemptions_at_once_cannot_spend_the_same_money()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        await client.PutAsJsonAsync("/api/v1/wallet/bank-details",
            new { bankName = "HNB", branch = "Dehiwala", accountName = "Nadeesha Perera", accountNumber = "0012345678" });
        var balance = (await client.GetJsonAsync("/api/v1/wallet")).GetProperty("balanceCents").GetInt64();
        var amount = balance / 2 + 1000;

        var results = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/wallet/redemptions", Cash(amount)),
            client.PostAsJsonAsync("/api/v1/wallet/redemptions", Cash(amount)));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        await results.Single(r => r.StatusCode != HttpStatusCode.Created).ErrorAsync(HttpStatusCode.Conflict, "INSUFFICIENT_BALANCE");
        Assert.True((await client.GetJsonAsync("/api/v1/wallet")).GetProperty("balanceCents").GetInt64() >= 0);
    }

    [Fact]
    public async Task Vouchers_come_in_the_brand_values_with_a_code()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var voucher = await client.PostJsonAsync("/api/v1/wallet/redemptions",
            new { type = "voucher", amountCents = 100000, voucherBrandId = "vb_lanka_fresh" }, HttpStatusCode.Created);
        Assert.Equal("completed", voucher.Str("status"));
        Assert.Matches(@"^LF-[A-Z0-9]{4}-\d{4}$", voucher.Str("voucherCode"));

        await (await client.PostAsJsonAsync("/api/v1/wallet/redemptions", new { type = "voucher", amountCents = 75000, voucherBrandId = "vb_lanka_fresh" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        await (await client.PostAsJsonAsync("/api/v1/wallet/redemptions", new { type = "voucher", amountCents = 50000, voucherBrandId = "vb_nope" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Donations_add_to_the_project_and_count_each_donor_once()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        async Task<DonationProject> Project() => await api.DbAsync(db => db.DonationProjects.AsNoTracking().SingleAsync(p => p.Id == "dp_monaragala"));
        var start = await Project();

        var gift = new { type = "donation", amountCents = 50000, projectId = "dp_monaragala" };
        Assert.Equal(JsonNull, (await client.PostJsonAsync("/api/v1/wallet/redemptions", gift, HttpStatusCode.Created)).GetProperty("voucherCode").ValueKind);
        await client.PostJsonAsync("/api/v1/wallet/redemptions", gift, HttpStatusCode.Created);
        var after = await Project();
        Assert.Equal((start.RaisedCents + 100000, start.DonorCount + 1), (after.RaisedCents, after.DonorCount));
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.RecipientId == "cus_1" && n.Type == "donationThanks")));

        await (await client.PostAsJsonAsync("/api/v1/wallet/redemptions", new { type = "donation", amountCents = 50000, projectId = "dp_flood_relief" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        await (await client.PostAsJsonAsync("/api/v1/wallet/redemptions", new { type = "donation", amountCents = 1000, projectId = "dp_monaragala" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "BELOW_MINIMUM_AMOUNT");
    }

    private const System.Text.Json.JsonValueKind JsonNull = System.Text.Json.JsonValueKind.Null;

    [Fact]
    public async Task Staff_work_the_transfer_queue_oldest_first()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();

        var queue = await staff.GetJsonAsync("/api/v1/admin/redemptions?type=cash&status=pending");
        queue.MatchesContract("admin", "GET /admin/redemptions");
        Assert.Equal(["red_1", "red_2", "red_3"], queue.GetProperty("items").EnumerateArray().Select(r => r.Str("id")));
        Assert.Matches(@"^\d{10}$", queue.GetProperty("items")[0].GetProperty("bank").Str("accountNumber"));

        var paid = await staff.PostJsonAsync("/api/v1/admin/redemptions/red_1/pay", new { reference = "CEFT 1107-88213" });
        Assert.Equal(("paid", "Kasun Jayawardena", "CEFT 1107-88213"), (paid.Str("status"), paid.Str("processedBy"), paid.Str("reference")));
        await (await staff.PostAsJsonAsync("/api/v1/admin/redemptions/red_1/pay", new { reference = "again" }))
            .ErrorAsync(HttpStatusCode.Conflict, "REDEMPTION_CLOSED");

        var (customer, customerId) = await RedeemerAsync(api, "red_2");
        var before = (await customer.GetJsonAsync("/api/v1/wallet")).GetProperty("balanceCents").GetInt64();
        var rejected = await staff.PostJsonAsync("/api/v1/admin/redemptions/red_2/reject", new { reason = "Account name doesn't match the customer." });
        Assert.Equal("rejected", rejected.Str("status"));
        Assert.Equal(before + 75000, (await customer.GetJsonAsync("/api/v1/wallet")).GetProperty("balanceCents").GetInt64());
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.RecipientId == customerId && n.Type == "transferUpdate")));

        await (await staff.PostAsJsonAsync("/api/v1/admin/redemptions/red_6/pay", new { reference = "x" }))
            .ErrorAsync(HttpStatusCode.Conflict, "REDEMPTION_CLOSED");
        await (await staff.PostAsJsonAsync("/api/v1/admin/redemptions/red_3/reject", new { reason = "" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Vouchers_and_donations_show_what_they_were()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        var all = (await staff.GetJsonAsync("/api/v1/admin/redemptions?pageSize=100")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(9, all.Count);
        Assert.Equal("LF-7KQ2-4821", all.Single(r => r.Str("id") == "red_6").GetProperty("voucher").Str("code"));
        Assert.Equal("dp_monaragala", all.Single(r => r.Str("id") == "red_8").GetProperty("donation").Str("projectId"));
        Assert.Equal(JsonNull, all.Single(r => r.Str("id") == "red_8").GetProperty("bank").ValueKind);

        var name = all[0].GetProperty("customer").Str("name").Split(' ')[1];
        var search = await staff.GetJsonAsync($"/api/v1/admin/redemptions?q={name.ToLowerInvariant()}");
        Assert.All(search.GetProperty("items").EnumerateArray(), r => Assert.Contains(name, r.GetProperty("customer").Str("name"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admins_run_donation_projects()
    {
        await using var api = await TestApi.StartAsync(server);
        await (await (await api.CallCentreAsync()).GetAsync("/api/v1/admin/donation-projects")).ErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");

        var admin = await api.AdminAsync();
        var list = await admin.GetJsonAsync("/api/v1/admin/donation-projects");
        list.MatchesContract("admin", "GET /admin/donation-projects");
        Assert.Equal("dp_flood_relief", list.GetProperty("items").EnumerateArray().Last().Str("id"));

        var created = await admin.PostJsonAsync("/api/v1/admin/donation-projects",
            ContractSamples.Request("admin", "POST /admin/donation-projects"), HttpStatusCode.Created);
        ShapeAssert.Matches(list.GetProperty("items")[0], created);
        Assert.Equal((0L, 0), (created.GetProperty("raisedCents").GetInt64(), created.GetProperty("donorCount").GetInt32()));

        var error = await (await admin.PostAsJsonAsync("/api/v1/admin/donation-projects",
            new { name = "Ok name", summary = "Short", goalCents = 50000, active = true })).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        Assert.Equal("The goal must be at least Rs. 1,000.", error.GetProperty("details").Str("goalCents"));
        await (await admin.PostAsJsonAsync("/api/v1/admin/donation-projects",
            new { name = "Ok name", summary = "Short", goalCents = 500000, endsOn = "2026-01-01" })).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");

        var paused = await (await admin.PatchAsJsonAsync($"/api/v1/admin/donation-projects/{created.Str("id")}", new { active = false })).JsonAsync();
        Assert.False(paused.GetProperty("active").GetBoolean());
        var customer = await api.CustomerAsync();
        Assert.DoesNotContain((await customer.GetJsonAsync("/api/v1/wallet/donation-projects")).GetProperty("items").EnumerateArray(),
            p => p.Str("id") == created.Str("id"));
        await (await admin.PatchAsJsonAsync("/api/v1/admin/donation-projects/dp_nope", new { active = false })).ErrorAsync(HttpStatusCode.NotFound, "PROJECT_NOT_FOUND");
    }
}
