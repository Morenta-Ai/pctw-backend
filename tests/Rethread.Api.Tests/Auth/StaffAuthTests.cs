using System.Net;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Data.Entities;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Auth;

public sealed class StaffAuthTests(PostgresServer server)
{
    [Fact]
    public async Task Staff_sign_in_with_email_and_password()
    {
        await using var api = await TestApi.StartAsync(server);
        var session = await api.Client().PostJsonAsync("/api/v1/admin/auth/login",
            new { email = "Admin@Rethread.lk", password = "rethread-admin" });
        session.MatchesContract("admin", "POST /admin/auth/login");
        Assert.Equal(900, session.GetProperty("expiresIn").GetInt32());
        Assert.Equal(("stf_1", "admin"), (session.GetProperty("staff").Str("id"), session.GetProperty("staff").Str("role")));

        var me = await api.WithToken(session.Str("accessToken")).GetJsonAsync("/api/v1/admin/me");
        // GET /admin/me is "the staff object from login".
        ShapeAssert.Matches(ContractSamples.Response("admin", "POST /admin/auth/login").GetProperty("staff"), me);
        Assert.Equal("Nadeesha Perera", me.Str("name"));

        var callCentre = await api.Client().PostJsonAsync("/api/v1/admin/auth/login",
            new { email = "callcentre@rethread.lk", password = "rethread-callcentre" });
        Assert.Equal("callCentre", callCentre.GetProperty("staff").Str("role"));
    }

    [Fact]
    public async Task Wrong_passwords_are_refused_then_throttled()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = api.Client();
        var wrong = new { email = "admin@rethread.lk", password = "nope" };
        for (var i = 0; i < 5; i++)
            await (await client.PostAsJsonAsync("/api/v1/admin/auth/login", wrong))
                .ErrorAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        await (await client.PostAsJsonAsync("/api/v1/admin/auth/login",
                new { email = "admin@rethread.lk", password = "rethread-admin" }))
            .ErrorAsync(HttpStatusCode.TooManyRequests, "TOO_MANY_ATTEMPTS");
    }

    [Fact]
    public async Task Suspended_staff_are_refused_at_login_and_on_their_next_request()
    {
        await using var api = await TestApi.StartAsync(server);
        await (await api.Client().PostAsJsonAsync("/api/v1/admin/auth/login",
                new { email = "ishara@rethread.lk", password = "rethread-callcentre" }))
            .ErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");

        var client = await api.CallCentreAsync();
        await api.DbAsync(db => db.Staff.Where(s => s.Id == "stf_2")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AccountStatus.Suspended)));
        await (await client.GetAsync("/api/v1/admin/me")).ErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task Refresh_returns_a_new_session_and_logout_ends_it()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = api.Client();
        var session = await client.PostJsonAsync("/api/v1/admin/auth/login",
            new { email = "admin@rethread.lk", password = "rethread-admin" });

        var refreshed = await client.PostJsonAsync("/api/v1/admin/auth/refresh", new { refreshToken = session.Str("refreshToken") });
        refreshed.MatchesContract("admin", "POST /admin/auth/login");

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/v1/admin/auth/logout", new { refreshToken = refreshed.Str("refreshToken") })).StatusCode);
        await (await client.PostAsJsonAsync("/api/v1/admin/auth/refresh", new { refreshToken = refreshed.Str("refreshToken") }))
            .ErrorAsync(HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
    }

    [Fact]
    public async Task An_invite_token_sets_the_password()
    {
        await using var api = await TestApi.StartAsync(server);
        await api.DbAsync(async db =>
        {
            db.Staff.Add(new Staff
            {
                Id = "stf_new",
                Name = "Dilini Wickramasinghe",
                Email = "dilini@rethread.lk",
                Role = StaffRole.CallCentre,
                InviteTokenHash = Secrets.Hash("inv_test"),
                InviteExpiresAt = TestClock.Start.UtcDateTime.AddDays(3),
                CreatedAt = TestClock.Start.UtcDateTime,
            });
            await db.SaveChangesAsync();
        });
        var client = api.Client();

        await (await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = "dilini@rethread.lk", password = "anything-at-all" }))
            .ErrorAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        await (await client.PostAsJsonAsync("/api/v1/admin/auth/set-password", new { token = "inv_test", password = "short" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/v1/admin/auth/set-password", new { token = "inv_test", password = "a-good-long-password" })).StatusCode);
        await client.PostJsonAsync("/api/v1/admin/auth/login", new { email = "dilini@rethread.lk", password = "a-good-long-password" });
        await (await client.PostAsJsonAsync("/api/v1/admin/auth/set-password", new { token = "inv_test", password = "another-long-password" }))
            .ErrorAsync(HttpStatusCode.BadRequest, "INVALID_INVITE");
    }

    [Fact]
    public async Task The_portal_needs_a_staff_token()
    {
        await using var api = await TestApi.StartAsync(server);
        await (await api.Client().GetAsync("/api/v1/admin/me")).ErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        var customer = await api.CustomerAsync();
        await (await customer.GetAsync("/api/v1/admin/me")).ErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
