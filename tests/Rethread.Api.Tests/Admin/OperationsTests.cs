using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rethread.Api.Auth;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Admin;

public sealed partial class OperationsTests(PostgresServer server)
{
    [Fact]
    public async Task Every_operations_response_matches_the_contract()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        (await staff.GetJsonAsync("/api/v1/admin/dashboard")).MatchesContract("admin", "GET /admin/dashboard");
        (await staff.GetJsonAsync("/api/v1/admin/counts")).MatchesContract("admin", "GET /admin/counts");
        (await staff.GetJsonAsync("/api/v1/admin/pickups")).MatchesContract("admin", "GET /admin/pickups");
        (await staff.GetJsonAsync("/api/v1/admin/pickups/RT-24817")).MatchesContract("admin", "GET /admin/pickups/{id}");
        (await staff.GetJsonAsync("/api/v1/admin/dispatch")).MatchesContract("admin", "GET /admin/dispatch");
        (await staff.GetJsonAsync("/api/v1/admin/customers")).MatchesContract("admin", "GET /admin/customers");
        (await staff.GetJsonAsync("/api/v1/admin/customers/cus_1")).MatchesContract("admin", "GET /admin/customers/{id}");
        (await staff.GetJsonAsync("/api/v1/admin/collectors")).MatchesContract("admin", "GET /admin/collectors");
        (await staff.GetJsonAsync("/api/v1/admin/collectors/col_457")).MatchesContract("admin", "GET /admin/collectors/{id}");
        (await staff.GetJsonAsync("/api/v1/admin/hubs")).MatchesContract("admin", "GET /admin/hubs");
        (await staff.GetJsonAsync("/api/v1/admin/notifications")).MatchesContract("admin", "GET /admin/notifications");
        (await (await api.AdminAsync()).GetJsonAsync("/api/v1/admin/staff")).MatchesContract("admin", "GET /admin/staff");
    }

    [Fact]
    public async Task Pickups_filter_sort_and_cancel_with_a_reason()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();

        var pending = await staff.GetJsonAsync("/api/v1/admin/pickups?status=pending&date=2026-11-07&sort=estimatedKg");
        var kgs = pending.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("estimatedKg").GetDecimal()).ToList();
        Assert.Equal(5, kgs.Count);
        Assert.Equal(kgs.Order(), kgs);
        var kumudu = (await staff.GetJsonAsync("/api/v1/admin/pickups?q=kumudu")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("RT-24841", kumudu[0].Str("id"));
        Assert.All(kumudu, p => Assert.Equal("Kumudu Herath", p.Str("customerName")));
        var byCollector = await staff.GetJsonAsync("/api/v1/admin/pickups?q=COL-0457&source=app");
        Assert.All(byCollector.GetProperty("items").EnumerateArray(), p => Assert.Equal("COL-0457", p.GetProperty("collector").Str("code")));
        await (await staff.GetAsync("/api/v1/admin/pickups?sort=-name")).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");

        await (await staff.PostAsJsonAsync("/api/v1/admin/pickups/RT-24817/cancel", new { reason = "no" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        var cancelled = await staff.PostJsonAsync("/api/v1/admin/pickups/RT-24817/cancel",
            ContractSamples.Request("admin", "POST /admin/pickups/{id}/cancel"));
        Assert.Equal(("cancelled", "Customer called to cancel: travelling this week."), (cancelled.Str("status"), cancelled.Str("cancelReason")));
        Assert.Equal("Kasun Jayawardena", cancelled.GetProperty("timeline").EnumerateArray().Last().Str("actor"));
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.RecipientId == "col_457" && n.Type == "jobCancelled")));
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.RecipientId == "cus_1" && n.Type == "pickupCancelled")));
        await (await staff.PostAsJsonAsync("/api/v1/admin/pickups/RT-24790/cancel", new { reason = "Too late now" }))
            .ErrorAsync(HttpStatusCode.Conflict, "PICKUP_NOT_CANCELLABLE");
        await (await staff.GetAsync("/api/v1/admin/pickups/RT-1")).ErrorAsync(HttpStatusCode.NotFound, "PICKUP_NOT_FOUND");
    }

    [Fact]
    public async Task Suspending_a_customer_locks_them_out()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        var app = await api.CustomerAsync();

        await (await staff.PostAsJsonAsync("/api/v1/admin/customers/cus_1/status", new { status = "suspended" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        var suspended = await staff.PostJsonAsync("/api/v1/admin/customers/cus_1/status", ContractSamples.Request("admin", "POST /admin/customers/{id}/status"));
        Assert.Equal("suspended", suspended.Str("status"));
        await (await app.GetAsync("/api/v1/me")).ErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");

        Assert.Equal("active", (await staff.PostJsonAsync("/api/v1/admin/customers/cus_1/status", new { status = "active" })).Str("status"));
        Assert.Equal(1, (await staff.GetJsonAsync("/api/v1/admin/customers?status=suspended")).GetProperty("totalCount").GetInt32());
        Assert.Contains((await staff.GetJsonAsync("/api/v1/admin/customers?q=dehiwala")).GetProperty("items").EnumerateArray(), c => c.Str("id") == "cus_1");
        await (await staff.GetAsync("/api/v1/admin/customers/cus_nope")).ErrorAsync(HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Registering_a_collector_issues_the_next_code_and_they_can_sign_in()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();

        var created = await staff.PostJsonAsync("/api/v1/admin/collectors", ContractSamples.Request("admin", "POST /admin/collectors"), HttpStatusCode.Created);
        created.MatchesContract("admin", "GET /admin/collectors/{id}");
        Assert.Equal(("COL-0542", "Kotte", "active", false), (created.Str("code"), created.Str("area"), created.Str("status"), created.GetProperty("online").GetBoolean()));

        var session = await api.CollectorSessionAsync("COL-0542", "+94771239876");
        Assert.Equal("Colombo East", session.GetProperty("collector").GetProperty("area").Str("name"));

        await (await staff.PostAsJsonAsync("/api/v1/admin/collectors", ContractSamples.Request("admin", "POST /admin/collectors")))
            .ErrorAsync(HttpStatusCode.Conflict, "MOBILE_IN_USE");
        var badHub = new { firstName = "A", lastName = "B", mobile = "+94770000001", area = "Kotte", hubId = "hub_x", vehicle = new { type = "van", plate = "WP X-1", capacityKg = 400 } };
        await (await staff.PostAsJsonAsync("/api/v1/admin/collectors", badHub)).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Suspending_a_collector_takes_them_offline_and_withdraws_offers()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();
        var session = await api.CollectorSessionAsync();
        var offer = await staff.PostJsonAsync("/api/v1/admin/pickups/RT-24844/assignments", new { collectorId = "col_457" }, HttpStatusCode.Created);

        var suspended = await staff.PostJsonAsync("/api/v1/admin/collectors/col_457/status", new { status = "suspended", reason = "Licence expired." });
        Assert.Equal(("suspended", false), (suspended.Str("status"), suspended.GetProperty("online").GetBoolean()));
        Assert.Equal("withdrawn", (await staff.GetJsonAsync($"/api/v1/admin/assignments/{offer.Str("id")}")).Str("status"));
        await (await api.WithToken(session.Str("accessToken")).GetAsync("/api/v1/collector/me")).ErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
        await (await api.Client().PostAsJsonAsync("/api/v1/collector/auth/refresh", new { refreshToken = session.Str("refreshToken") }))
            .ErrorAsync(HttpStatusCode.Unauthorized, "SESSION_EXPIRED");

        var online = await staff.GetJsonAsync("/api/v1/admin/collectors?online=true");
        Assert.DoesNotContain(online.GetProperty("items").EnumerateArray(), c => c.Str("id") == "col_457");
        Assert.Single((await staff.GetJsonAsync("/api/v1/admin/collectors?q=WP%20ABC")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Admins_invite_staff_who_set_a_password_and_sign_in()
    {
        await using var api = await TestApi.StartAsync(server);
        await (await (await api.CallCentreAsync()).GetAsync("/api/v1/admin/staff")).ErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        var admin = await api.AdminAsync();

        var invited = await admin.PostJsonAsync("/api/v1/admin/staff", ContractSamples.Request("admin", "POST /admin/staff"), HttpStatusCode.Created);
        Assert.Equal(("callCentre", "active"), (invited.Str("role"), invited.Str("status")));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, invited.GetProperty("lastSignInAt").ValueKind);
        await (await admin.PostAsJsonAsync("/api/v1/admin/staff", ContractSamples.Request("admin", "POST /admin/staff")))
            .ErrorAsync(HttpStatusCode.Conflict, "EMAIL_IN_USE");

        var email = api.Factory.Services.GetRequiredService<DevOutbox>().LastTo("dilini@rethread.lk")!;
        var token = Uri.UnescapeDataString(InviteToken().Match(email.Body).Groups[1].Value);
        Assert.Equal(HttpStatusCode.NoContent,
            (await api.Client().PostAsJsonAsync("/api/v1/admin/auth/set-password", new { token, password = "dilini-strong-password" })).StatusCode);
        var dilini = await api.StaffAsync("dilini@rethread.lk", "dilini-strong-password");
        Assert.Equal("Dilini Wickramasinghe", (await dilini.GetJsonAsync("/api/v1/admin/me")).Str("name"));

        var promoted = await (await admin.PatchAsJsonAsync($"/api/v1/admin/staff/{invited.Str("id")}", new { role = "admin" })).JsonAsync();
        Assert.Equal("admin", promoted.Str("role"));
        await (await admin.PatchAsJsonAsync("/api/v1/admin/staff/stf_1", new { status = "suspended" })).ErrorAsync(HttpStatusCode.Conflict, "CANNOT_CHANGE_SELF");
        await (await admin.PatchAsJsonAsync("/api/v1/admin/staff/stf_nope", new { role = "admin" })).ErrorAsync(HttpStatusCode.NotFound, "STAFF_NOT_FOUND");

        await admin.PatchAsJsonAsync($"/api/v1/admin/staff/{invited.Str("id")}", new { status = "suspended" });
        await (await dilini.GetAsync("/api/v1/admin/me")).ErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task Staff_notifications_track_reads_per_person()
    {
        await using var api = await TestApi.StartAsync(server);
        var kasun = await api.CallCentreAsync();
        var nadeesha = await api.AdminAsync();

        var page = await kasun.GetJsonAsync("/api/v1/admin/notifications?limit=5");
        Assert.Equal(5, page.GetProperty("items").GetArrayLength());
        var total = page.GetProperty("unreadCount").GetInt32();
        var next = await kasun.GetJsonAsync($"/api/v1/admin/notifications?limit=5&cursor={page.Str("nextCursor")}");
        Assert.Empty(next.GetProperty("items").EnumerateArray().Select(n => n.Str("id")).Intersect(page.GetProperty("items").EnumerateArray().Select(n => n.Str("id"))));

        var first = page.GetProperty("items")[0].Str("id");
        Assert.Equal(HttpStatusCode.NoContent, (await kasun.PostAsync($"/api/v1/admin/notifications/{first}/read", null)).StatusCode);
        Assert.Equal(total - 1, (await kasun.GetJsonAsync("/api/v1/admin/notifications")).GetProperty("unreadCount").GetInt32());
        Assert.Equal(total, (await nadeesha.GetJsonAsync("/api/v1/admin/notifications")).GetProperty("unreadCount").GetInt32());
        Assert.DoesNotContain((await kasun.GetJsonAsync("/api/v1/admin/notifications?unread=true&limit=50")).GetProperty("items").EnumerateArray(), n => n.Str("id") == first);

        Assert.Equal(HttpStatusCode.NoContent, (await kasun.PostAsync("/api/v1/admin/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await kasun.GetJsonAsync("/api/v1/admin/counts")).GetProperty("unreadNotifications").GetInt32());
        Assert.All((await kasun.GetJsonAsync("/api/v1/admin/notifications")).GetProperty("items").EnumerateArray(), n => Assert.True(n.GetProperty("read").GetBoolean()));
        await (await kasun.PostAsync("/api/v1/admin/notifications/ntf_nope/read", null)).ErrorAsync(HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
    }

    [GeneratedRegex(@"token=([^\s]+)")]
    private static partial Regex InviteToken();
}
