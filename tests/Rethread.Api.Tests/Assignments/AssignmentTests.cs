using System.Net;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Data.Entities;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Assignments;

public sealed class AssignmentTests(PostgresServer server)
{
    private static async Task<HttpClient> CollectorByCodeAsync(TestApi api, string code)
    {
        var mobile = await api.DbAsync(db => db.Collectors.Where(c => c.Code == code).Select(c => c.Mobile).SingleAsync());
        return await api.CollectorAsync(code, mobile);
    }

    private static Task<System.Text.Json.JsonElement> OfferAsync(HttpClient staff, string pickupId, string collectorId,
        string? note = null) =>
        staff.PostJsonAsync($"/api/v1/admin/pickups/{pickupId}/assignments", new { collectorId, note }, HttpStatusCode.Created);

    [Fact]
    public async Task An_accepted_offer_gives_the_collector_the_pickup()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();

        var offer = await OfferAsync(staff, "RT-24841", "col_515", "Customer prefers before 3 PM.");
        offer.MatchesContract("admin", "POST /admin/pickups/{id}/assignments");
        Assert.Equal(("offered", "Kasun Jayawardena", "2026-11-07T06:00:45Z"),
            (offer.Str("status"), offer.Str("offeredBy"), offer.Str("expiresAt")));

        var lahiru = await CollectorByCodeAsync(api, "COL-0515");
        var current = await lahiru.GetJsonAsync("/api/v1/collector/assignments/current");
        current.MatchesContract("collector", "GET /assignments/current");
        current.GetProperty("job").MatchesContract("collector", "GET /jobs/{id}");
        Assert.Equal(("RT-24841", "open"), (current.GetProperty("job").Str("id"), current.GetProperty("job").Str("status")));
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.RecipientId == "col_515" && n.Type == "assignment" && n.TargetId == offer.Str("id"))));

        var job = await lahiru.PostJsonAsync($"/api/v1/collector/assignments/{offer.Str("id")}/accept", null);
        Assert.Equal(("accepted", "Kumudu Herath"), (job.Str("status"), job.Str("customerName")));

        var after = await staff.GetJsonAsync($"/api/v1/admin/assignments/{offer.Str("id")}");
        after.MatchesContract("admin", "POST /admin/pickups/{id}/assignments");
        Assert.Equal("accepted", after.Str("status"));
        Assert.Equal("2026-11-07T06:00:00Z", after.Str("respondedAt"));
        var pickup = await api.DbAsync(db => db.Pickups.SingleAsync(p => p.Id == "RT-24841"));
        Assert.Equal((PickupStatus.Assigned, "col_515"), (pickup.Status, pickup.CollectorId));
        Assert.Equal(HttpStatusCode.NoContent, (await lahiru.GetAsync("/api/v1/collector/assignments/current")).StatusCode);
    }

    [Fact]
    public async Task Offers_are_refused_when_they_cant_work()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        await OfferAsync(staff, "RT-24841", "col_515");

        async Task Refused(string pickup, string collector, HttpStatusCode status, string code) =>
            await (await staff.PostAsJsonAsync($"/api/v1/admin/pickups/{pickup}/assignments", new { collectorId = collector }))
                .ErrorAsync(status, code);

        await Refused("RT-24841", "col_312", HttpStatusCode.Conflict, "ASSIGNMENT_PENDING");
        await Refused("RT-24826", "col_312", HttpStatusCode.Conflict, "PICKUP_ALREADY_ASSIGNED");
        await Refused("RT-24844", "col_473", HttpStatusCode.Conflict, "COLLECTOR_OFFLINE");
        await Refused("RT-24844", "col_541", HttpStatusCode.Conflict, "COLLECTOR_OFFLINE");
        await Refused("RT-1", "col_312", HttpStatusCode.NotFound, "PICKUP_NOT_FOUND");
        await Refused("RT-24844", "col_nobody", HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");

        await api.DbAsync(db => db.Collectors.Where(c => c.Id == "col_421").ExecuteUpdateAsync(s => s.SetProperty(c => c.CapacityKg, 10)));
        await Refused("RT-24830", "col_421", HttpStatusCode.Conflict, "LOAD_FULL");
    }

    [Fact]
    public async Task A_decline_alerts_the_call_centre_with_the_reason()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.AdminAsync();
        var offer = await OfferAsync(staff, "RT-24844", "col_457");

        var ruwan = await api.CollectorAsync();
        Assert.Equal(HttpStatusCode.NoContent,
            (await ruwan.PostAsJsonAsync($"/api/v1/collector/assignments/{offer.Str("id")}/decline", new { reason = "tooFar" })).StatusCode);

        var after = await staff.GetJsonAsync($"/api/v1/admin/assignments/{offer.Str("id")}");
        Assert.Equal(("declined", "Too far"), (after.Str("status"), after.Str("declineReason")));
        var alert = await api.DbAsync(db => db.Notifications.SingleAsync(n => n.Id == $"ntf_offerDeclined_{offer.Str("id")}"));
        Assert.Equal(("Ruwan Silva declined RT-24844", "Reason: Too far. Offer it to someone else."), (alert.Title, alert.Body));

        await (await ruwan.PostAsync($"/api/v1/collector/assignments/{offer.Str("id")}/accept", null))
            .ErrorAsync(HttpStatusCode.Conflict, "ASSIGNMENT_EXPIRED");
        // The pickup can be offered again straight away.
        await OfferAsync(staff, "RT-24844", "col_312");
    }

    [Fact]
    public async Task Unanswered_offers_expire_after_45_seconds()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        var offer = await OfferAsync(staff, "RT-24830", "col_312");
        var chaminda = await CollectorByCodeAsync(api, "COL-0312");

        api.Clock.Advance(TimeSpan.FromSeconds(46));
        var after = await staff.GetJsonAsync($"/api/v1/admin/assignments/{offer.Str("id")}");
        Assert.Equal(("expired", "2026-11-07T06:00:45Z"), (after.Str("status"), after.Str("respondedAt")));
        Assert.Equal(HttpStatusCode.NoContent, (await chaminda.GetAsync("/api/v1/collector/assignments/current")).StatusCode);
        await (await chaminda.PostAsync($"/api/v1/collector/assignments/{offer.Str("id")}/accept", null))
            .ErrorAsync(HttpStatusCode.Conflict, "ASSIGNMENT_EXPIRED");
        Assert.Equal("No answer on RT-24830",
            await api.DbAsync(db => db.Notifications.Where(n => n.Id == $"ntf_offerDeclined_{offer.Str("id")}").Select(n => n.Title).SingleAsync()));
    }

    [Fact]
    public async Task Staff_withdraw_open_offers_and_see_the_history()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        var first = await OfferAsync(staff, "RT-24835", "col_421");

        var withdrawn = await staff.PostJsonAsync($"/api/v1/admin/assignments/{first.Str("id")}/withdraw", null);
        Assert.Equal("withdrawn", withdrawn.Str("status"));
        await (await staff.PostAsync($"/api/v1/admin/assignments/{first.Str("id")}/withdraw", null))
            .ErrorAsync(HttpStatusCode.Conflict, "ASSIGNMENT_CLOSED");

        api.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await OfferAsync(staff, "RT-24835", "col_457");
        var history = await staff.GetJsonAsync("/api/v1/admin/pickups/RT-24835/assignments");
        Assert.Equal([second.Str("id"), first.Str("id")], history.GetProperty("items").EnumerateArray().Select(a => a.Str("id")));

        var saman = await CollectorByCodeAsync(api, "COL-0421");
        await (await saman.PostAsync($"/api/v1/collector/assignments/{second.Str("id")}/accept", null))
            .ErrorAsync(HttpStatusCode.NotFound, "ASSIGNMENT_NOT_FOUND");
        await (await staff.GetAsync("/api/v1/admin/assignments/asg_nope")).ErrorAsync(HttpStatusCode.NotFound, "ASSIGNMENT_NOT_FOUND");
    }

    [Fact]
    public async Task Taking_the_job_from_the_board_closes_the_offer()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        var offer = await OfferAsync(staff, "RT-24822", "col_421");
        var ruwan = await api.CollectorAsync();
        await ruwan.PostJsonAsync("/api/v1/collector/jobs/RT-24822/accept", null);

        Assert.Equal("withdrawn", (await staff.GetJsonAsync($"/api/v1/admin/assignments/{offer.Str("id")}")).Str("status"));
        var saman = await CollectorByCodeAsync(api, "COL-0421");
        await (await saman.PostAsync($"/api/v1/collector/assignments/{offer.Str("id")}/accept", null))
            .ErrorAsync(HttpStatusCode.Conflict, "ASSIGNMENT_EXPIRED");
    }

    [Fact]
    public async Task Pickups_waiting_30_minutes_alert_the_call_centre_once()
    {
        await using var api = await TestApi.StartAsync(server);
        Assert.False(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.Id == "ntf_pickupWaiting_RT-24844")));

        api.Clock.Advance(TimeSpan.FromMinutes(23)); // RT-24844 was booked 8 minutes before the start.
        var staff = await api.CallCentreAsync();
        await staff.GetJsonAsync("/api/v1/admin/pickups/RT-24844/assignments");
        await staff.GetJsonAsync("/api/v1/admin/pickups/RT-24844/assignments");
        var alerts = await api.DbAsync(db => db.Notifications.Where(n => n.Id == "ntf_pickupWaiting_RT-24844").ToListAsync());
        var alert = Assert.Single(alerts);
        Assert.Equal(new DateTime(2026, 11, 7, 6, 22, 0, DateTimeKind.Utc), alert.CreatedAt);
    }

    [Fact]
    public async Task In_development_the_simulator_answers_like_the_portals_mock_collectors()
    {
        await using var api = await TestApi.StartAsync(server, new Dictionary<string, string> { ["Simulation:AutoAnswerOffers"] = "true" });
        var staff = await api.CallCentreAsync();
        var free = await OfferAsync(staff, "RT-24841", "col_515");
        var busy = await OfferAsync(staff, "RT-24844", "col_457");
        var silent = await OfferAsync(staff, "RT-24830", "col_421");

        api.Clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal("accepted", (await staff.GetJsonAsync($"/api/v1/admin/assignments/{free.Str("id")}")).Str("status"));
        var declined = await staff.GetJsonAsync($"/api/v1/admin/assignments/{busy.Str("id")}");
        Assert.Equal(("declined", "Already on a job"), (declined.Str("status"), declined.Str("declineReason")));
        Assert.Equal("offered", (await staff.GetJsonAsync($"/api/v1/admin/assignments/{silent.Str("id")}")).Str("status"));

        api.Clock.Advance(TimeSpan.FromSeconds(40));
        Assert.Equal("expired", (await staff.GetJsonAsync($"/api/v1/admin/assignments/{silent.Str("id")}")).Str("status"));
    }
}
