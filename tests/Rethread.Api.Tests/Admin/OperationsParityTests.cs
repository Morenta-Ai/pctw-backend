using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Admin;

/// <summary>
/// <c>portal-operations.json</c> is what the portal's own mock sources return at <see cref="TestClock.Start"/>,
/// signed in as the call centre. The portal's pages and e2e tests were built against those numbers, so the
/// real API must give the same ones from the same seed.
/// </summary>
public sealed class OperationsParityTests(PostgresServer server)
{
    private static readonly JsonNode Expected = JsonNode.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Seed", "portal-operations.json")))!;

    private static async Task<JsonNode> GetAsync(HttpClient client, string path) =>
        JsonDiff.ToNode(await client.GetJsonAsync($"/api/v1/admin{path}"));

    [Fact]
    public async Task Dashboard_and_counts()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        JsonDiff.AssertSame(Expected["dashboard"], await GetAsync(staff, "/dashboard"));
        JsonDiff.AssertSame(Expected["counts"], await GetAsync(staff, "/counts"));
    }

    [Fact]
    public async Task Pickups()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        JsonDiff.AssertSame(Expected["pickups"], await GetAsync(staff, "/pickups"));
        JsonDiff.AssertSame(Expected["pickupDetail"], await GetAsync(staff, "/pickups/RT-24817"));
    }

    [Fact]
    public async Task Dispatch()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        var actual = await GetAsync(staff, "/dispatch");
        var expected = Expected["dispatch"]!.DeepClone();
        foreach (var key in new[] { "collectors", "pickups" })
        {
            expected[key] = JsonDiff.SortedBy(expected[key]!, "id");
            actual[key] = JsonDiff.SortedBy(actual[key]!, "id");
        }

        // The portal's mock drifts online collectors to fake live positions.
        JsonDiff.AssertSame(expected, actual, "$.collectors[*].latitude", "$.collectors[*].longitude",
            "$.collectors[*].lastSeenAt");
    }

    [Fact]
    public async Task Customers_and_collectors()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        // The mock's balance ignores wallet-paid shop orders; the real ledger takes them off.
        var customers = await GetAsync(staff, "/customers");
        JsonDiff.AssertSame(Expected["customers"], customers, "$.items[*].walletBalanceCents");
        foreach (var row in customers["items"]!.AsArray())
        {
            var id = row!["id"]!.GetValue<string>();
            var ledger = await api.DbAsync(db => db.WalletTransactions
                .Where(t => t.CustomerId == id && t.Status != Rethread.Api.Data.Entities.TransactionStatus.Failed)
                .SumAsync(t => t.AmountCents));
            Assert.Equal(ledger, row["walletBalanceCents"]!.GetValue<long>());
        }
        JsonDiff.AssertSame(Expected["customerDetail"], await GetAsync(staff, "/customers/cus_1"));
        JsonDiff.AssertSame(Expected["collectors"], await GetAsync(staff, "/collectors"));
        JsonDiff.AssertSame(Expected["collectorDetail"], await GetAsync(staff, "/collectors/col_457"));
    }

    [Fact]
    public async Task Staff_and_notifications()
    {
        await using var api = await TestApi.StartAsync(server);
        var admin = await api.AdminAsync();
        JsonDiff.AssertSame(new JsonObject { ["items"] = Expected["staff"]!.DeepClone() }, await GetAsync(admin, "/staff"),
            "$.items[*].lastSignInAt");

        var staff = await api.CallCentreAsync();
        var actual = await GetAsync(staff, "/notifications?limit=50");
        var expected = Expected["notifications"]!.DeepClone();
        expected["items"] = JsonDiff.SortedBy(expected["items"]!, "id");
        actual["items"] = JsonDiff.SortedBy(actual["items"]!, "id");
        JsonDiff.AssertSame(expected, actual, "$.nextCursor");
    }
}
