using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Data.Entities;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Collector;

public sealed class JobTests(PostgresServer server)
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1];

    private static async Task<HttpClient> CollectorByCodeAsync(TestApi api, string code)
    {
        var mobile = await api.DbAsync(db => db.Collectors.Where(c => c.Code == code).Select(c => c.Mobile).SingleAsync());
        return await api.CollectorAsync(code, mobile);
    }

    private static MultipartFormDataContent Photo(byte[] bytes, string kind = "bag", string type = "image/jpeg")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { file, "file", "photo.jpg" }, { new StringContent(kind), "kind" } };
    }

    [Fact]
    public async Task Collectors_read_and_update_their_profile_and_status()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CollectorAsync();

        (await client.GetJsonAsync("/api/v1/collector/me")).MatchesContract("collector", "GET /me");
        var updated = await (await client.PatchAsJsonAsync("/api/v1/collector/me", new { language = "si", scaleAutoConnect = false })).JsonAsync();
        Assert.Equal(("si", false), (updated.Str("language"), updated.GetProperty("scale").GetProperty("autoConnect").GetBoolean()));

        var status = await (await client.PutAsJsonAsync("/api/v1/collector/me/status",
            new { online = false, latitude = 6.8511, longitude = 79.8655 })).JsonAsync();
        status.MatchesContract("collector", "PUT /me/status");
        Assert.False(status.GetProperty("online").GetBoolean());
        var dashboard = await client.GetJsonAsync("/api/v1/collector/me/dashboard");
        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("nearby").ValueKind);
    }

    [Fact]
    public async Task The_dashboard_sums_today_the_load_and_the_active_job()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CollectorAsync();
        var dashboard = await client.GetJsonAsync("/api/v1/collector/me/dashboard");
        dashboard.MatchesContract("collector", "GET /me/dashboard");

        var today = dashboard.GetProperty("today");
        Assert.Equal((3, 20.5m, 82000L), (today.GetProperty("jobsDone").GetInt32(), today.GetProperty("collectedKg").GetDecimal(), today.GetProperty("earnedCents").GetInt64()));
        var load = dashboard.GetProperty("load");
        Assert.Equal((20.5m, 120, 3), (load.GetProperty("weightKg").GetDecimal(), load.GetProperty("capacityKg").GetInt32(), load.GetProperty("jobCount").GetInt32()));
        Assert.Equal("RT-24817", dashboard.GetProperty("activeJob").Str("id"));
        Assert.True(dashboard.GetProperty("unreadNotifications").GetInt32() > 0);
    }

    [Fact]
    public async Task The_board_lists_open_jobs_nearby_closest_first()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CollectorAsync();

        var board = await client.GetJsonAsync("/api/v1/collector/jobs");
        board.MatchesContract("collector", "GET /jobs");
        var jobs = board.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(jobs);
        var distances = jobs.Select(j => j.GetProperty("distanceKm").GetDouble()).ToList();
        Assert.Equal(distances.Order(), distances);
        Assert.All(distances, d => Assert.True(d <= 5));

        var wide = await client.GetJsonAsync("/api/v1/collector/jobs?radiusKm=25");
        Assert.Equal(5, wide.GetProperty("items").GetArrayLength());
        var small = await client.GetJsonAsync("/api/v1/collector/jobs?radiusKm=25&size=small");
        Assert.All(small.GetProperty("items").EnumerateArray(), j => Assert.True(j.GetProperty("estimatedKg").GetDecimal() < 5));
        var afternoon = await client.GetJsonAsync("/api/v1/collector/jobs?radiusKm=25&slot=afternoon");
        Assert.All(afternoon.GetProperty("items").EnumerateArray(), j => Assert.Equal("afternoon", j.Str("slot")));
        await (await client.GetAsync("/api/v1/collector/jobs?size=huge")).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Accepting_reveals_the_customer_and_only_one_collector_wins()
    {
        await using var api = await TestApi.StartAsync(server);
        var ruwan = await api.CollectorAsync();

        var open = await ruwan.GetJsonAsync("/api/v1/collector/jobs/RT-24822");
        open.MatchesContract("collector", "GET /jobs/{id}");
        Assert.Equal("open", open.Str("status"));
        Assert.Equal(JsonValueKind.Null, open.GetProperty("customerName").ValueKind);
        Assert.Equal(JsonValueKind.Number, open.GetProperty("items")[0].GetProperty("quantity").ValueKind);

        var accepted = await ruwan.PostJsonAsync("/api/v1/collector/jobs/RT-24822/accept", null);
        Assert.Equal(("accepted", "Mohamed Fazil"), (accepted.Str("status"), accepted.Str("customerName")));

        var chaminda = await CollectorByCodeAsync(api, "COL-0312");
        await (await chaminda.PostAsync("/api/v1/collector/jobs/RT-24830/accept", null)).JsonAsync();
        await (await ruwan.PostAsync("/api/v1/collector/jobs/RT-24830/accept", null)).ErrorAsync(HttpStatusCode.Conflict, "JOB_TAKEN");
        await (await ruwan.PostAsync("/api/v1/collector/jobs/RT-24815/accept", null)).ErrorAsync(HttpStatusCode.Conflict, "JOB_NOT_ACTIVE");
        await (await ruwan.PostAsync("/api/v1/collector/jobs/RT-1/accept", null)).ErrorAsync(HttpStatusCode.NotFound, "JOB_NOT_FOUND");
        await (await ruwan.GetAsync("/api/v1/collector/jobs/RT-24826")).ErrorAsync(HttpStatusCode.NotFound, "JOB_NOT_FOUND");
    }

    [Fact]
    public async Task Two_collectors_racing_for_a_job_get_one_winner()
    {
        await using var api = await TestApi.StartAsync(server);
        var ruwan = await api.CollectorAsync();
        var chaminda = await CollectorByCodeAsync(api, "COL-0312");

        var results = await Task.WhenAll(
            ruwan.PostAsync("/api/v1/collector/jobs/RT-24835/accept", null),
            chaminda.PostAsync("/api/v1/collector/jobs/RT-24835/accept", null));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var loser = results.Single(r => r.StatusCode != HttpStatusCode.OK);
        await loser.ErrorAsync(HttpStatusCode.Conflict, "JOB_TAKEN");
        Assert.Single(await api.DbAsync(db => db.PickupEvents.Where(e => e.PickupId == "RT-24835" && e.Status == PickupStatus.Assigned).ToListAsync()));
    }

    [Fact]
    public async Task A_collection_pays_the_customer_from_the_server_rate()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CollectorAsync();

        var active = await client.GetJsonAsync("/api/v1/collector/jobs/active");
        active.MatchesContract("collector", "GET /jobs/{id}");
        Assert.Equal(("RT-24817", "onTheWay"), (active.Str("id"), active.Str("status")));
        await (await client.PostAsync("/api/v1/collector/jobs/RT-24817/start", null)).ErrorAsync(HttpStatusCode.Conflict, "JOB_NOT_ACTIVE");

        Assert.Equal("arrived", (await client.PostJsonAsync("/api/v1/collector/jobs/RT-24817/arrive", null)).Str("status"));
        var photo = await (await client.PostAsync("/api/v1/collector/jobs/RT-24817/photos", Photo(Jpeg))).JsonAsync();
        ShapeAssert.Matches(ContractSamples.Section("collector", "POST /jobs/{id}/photos")[0].Json, photo);

        var result = await client.PostJsonAsync("/api/v1/collector/jobs/RT-24817/collection", new
        {
            weightKg = 7.4,
            weightSource = "scale",
            bagCount = 3,
            checkedItemIds = new[] { "itm_24817_1" },
            photoIds = new[] { photo.Str("photoId") },
        });
        result.MatchesContract("collector", "POST /jobs/{id}/collection");
        Assert.Equal((88800L, 12000L), (result.GetProperty("payoutCents").GetInt64(), result.GetProperty("ratePerKgCents").GetInt64()));
        Assert.Equal((27.9m, 4), (result.GetProperty("load").GetProperty("weightKg").GetDecimal(), result.GetProperty("load").GetProperty("jobCount").GetInt32()));

        await api.DbAsync(async db =>
        {
            var pickup = await db.Pickups.SingleAsync(p => p.Id == "RT-24817");
            Assert.Equal((PickupStatus.Collected, 29600L), (pickup.Status, pickup.CollectorPayoutCents!.Value));
            Assert.True(await db.WalletTransactions.AnyAsync(t => t.Id == "txn_RT-24817" && t.AmountCents == 88800 && t.CustomerId == "cus_1"));
            Assert.Equal(2, await db.Notifications.CountAsync(n => n.RecipientId == "cus_1" && n.TargetType != null && n.CreatedAt == TestClock.Start.UtcDateTime));
        });

        await (await client.PostAsJsonAsync("/api/v1/collector/jobs/RT-24817/collection", new { weightKg = 1, weightSource = "manual", bagCount = 1 }))
            .ErrorAsync(HttpStatusCode.Conflict, "JOB_NOT_ACTIVE");
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/v1/collector/jobs/active")).StatusCode);
    }

    [Fact]
    public async Task Photos_must_be_small_jpegs_for_the_collectors_job()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CollectorAsync();
        await (await client.PostAsync("/api/v1/collector/jobs/RT-24817/photos", Photo([0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0], type: "image/png")))
            .ErrorAsync(HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_FILE_TYPE");
        var big = new byte[2 * 1024 * 1024 + 1];
        Jpeg.CopyTo(big, 0);
        await (await client.PostAsync("/api/v1/collector/jobs/RT-24817/photos", Photo(big)))
            .ErrorAsync(HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");
        await (await client.PostAsJsonAsync("/api/v1/collector/jobs/RT-24817/collection",
                new { weightKg = 7, weightSource = "scale", bagCount = 3, photoIds = new[] { "pho_someone_else" } }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task A_full_vehicle_cannot_take_more()
    {
        await using var api = await TestApi.StartAsync(server);
        var saman = await CollectorByCodeAsync(api, "COL-0421"); // A motorbike: 40 kg.
        await saman.PostJsonAsync("/api/v1/collector/jobs/RT-24841/accept", null);
        await saman.PostJsonAsync("/api/v1/collector/jobs/RT-24841/start", null);
        await (await saman.PostAsJsonAsync("/api/v1/collector/jobs/RT-24841/collection", new { weightKg = 45, weightSource = "manual", bagCount = 3 }))
            .ErrorAsync(HttpStatusCode.Conflict, "LOAD_FULL");
        var collected = await saman.PostJsonAsync("/api/v1/collector/jobs/RT-24841/collection",
            new { weightKg = 9.6, weightSource = "manual", bagCount = 3 });
        Assert.Equal((9.6m, 40, 1), (collected.GetProperty("load").GetProperty("weightKg").GetDecimal(),
            collected.GetProperty("load").GetProperty("capacityKg").GetInt32(), collected.GetProperty("load").GetProperty("jobCount").GetInt32()));
    }

    [Fact]
    public async Task Starting_tells_the_customer_and_other_collectors_jobs_are_off_limits()
    {
        await using var api = await TestApi.StartAsync(server);
        var dilshan = await CollectorByCodeAsync(api, "COL-0466");
        Assert.Equal("onTheWay", (await dilshan.PostJsonAsync("/api/v1/collector/jobs/RT-24826/start", null)).Str("status"));
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.RecipientId == "cus_9" && n.Type == "collectorEnRoute")));

        var ruwan = await api.CollectorAsync();
        await (await ruwan.PostAsync("/api/v1/collector/jobs/RT-24826/arrive", null)).ErrorAsync(HttpStatusCode.NotFound, "JOB_NOT_FOUND");
    }

    [Fact]
    public async Task Collectors_read_their_notifications()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CollectorAsync();
        var page = await client.GetJsonAsync("/api/v1/collector/notifications");
        page.MatchesContract("collector", "GET /notifications");
        Assert.Contains(page.GetProperty("items").EnumerateArray(), n => n.Str("type") == "hubReminder");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/collector/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await client.GetJsonAsync("/api/v1/collector/notifications")).GetProperty("unreadCount").GetInt32());
    }
}
