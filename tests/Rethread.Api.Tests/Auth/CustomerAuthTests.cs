using System.Net;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Auth;

public sealed class CustomerAuthTests(PostgresServer server)
{
    [Fact]
    public async Task An_existing_customer_signs_in_with_a_code()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = api.Client();

        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request",
            new { channel = "mobile", destination = SignIn.NadeeshaMobile });
        challenge.MatchesContract("user", "POST /auth/otp/request");
        Assert.Equal(30, challenge.GetProperty("resendAfterSeconds").GetInt32());

        var session = await client.PostJsonAsync("/api/v1/auth/otp/verify",
            new { challengeId = challenge.Str("challengeId"), code = SignIn.Code });
        session.MatchesContract("user", "POST /auth/otp/verify");
        session.GetProperty("user").MatchesContract("user", "GET /me");
        Assert.False(session.GetProperty("isNewUser").GetBoolean());
        Assert.Equal("cus_1", session.GetProperty("user").Str("id"));
        Assert.Equal("2026-11-07T07:00:00Z", session.Str("expiresAt"));
    }

    [Fact]
    public async Task A_new_number_creates_an_account()
    {
        await using var api = await TestApi.StartAsync(server);
        var session = await api.CustomerSessionAsync("+94711112222");
        Assert.True(session.GetProperty("isNewUser").GetBoolean());
        var user = session.GetProperty("user");
        Assert.Equal("+94711112222", user.Str("mobile"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, user.GetProperty("firstName").ValueKind);
        Assert.Equal("en", user.Str("language"));
    }

    [Fact]
    public async Task Email_sign_in_works_too()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = api.Client();
        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request",
            new { channel = "email", destination = "New.Person@Example.lk" });
        var session = await client.PostJsonAsync("/api/v1/auth/otp/verify",
            new { challengeId = challenge.Str("challengeId"), code = SignIn.Code });
        Assert.Equal("new.person@example.lk", session.GetProperty("user").Str("email"));
    }

    [Fact]
    public async Task Wrong_expired_and_spammed_codes_are_refused()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = api.Client();
        var request = new { channel = "mobile", destination = SignIn.NadeeshaMobile };
        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request", request);

        await (await client.PostAsJsonAsync("/api/v1/auth/otp/request", request))
            .ErrorAsync(HttpStatusCode.TooManyRequests, "OTP_RATE_LIMITED");
        await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
                new { challengeId = challenge.Str("challengeId"), code = "000000" }))
            .ErrorAsync(HttpStatusCode.BadRequest, "INVALID_OTP");

        api.Clock.Advance(TimeSpan.FromMinutes(6));
        await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
                new { challengeId = challenge.Str("challengeId"), code = SignIn.Code }))
            .ErrorAsync(HttpStatusCode.BadRequest, "OTP_EXPIRED");
    }

    [Fact]
    public async Task A_code_works_once()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = api.Client();
        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request",
            new { channel = "mobile", destination = SignIn.NadeeshaMobile });
        var verify = new { challengeId = challenge.Str("challengeId"), code = SignIn.Code };
        await client.PostJsonAsync("/api/v1/auth/otp/verify", verify);
        await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", verify))
            .ErrorAsync(HttpStatusCode.BadRequest, "INVALID_OTP");
    }

    [Fact]
    public async Task Bad_input_gets_field_errors()
    {
        await using var api = await TestApi.StartAsync(server);
        var error = await (await api.Client().PostAsJsonAsync("/api/v1/auth/otp/request",
                new { channel = "mobile", destination = "0771234567" }))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        Assert.True(error.GetProperty("details").TryGetProperty("destination", out _));
    }

    [Fact]
    public async Task Suspended_customers_cannot_sign_in()
    {
        await using var api = await TestApi.StartAsync(server);
        var mobile = await api.DbAsync(db => db.Customers.Where(c => c.Id == "cus_18").Select(c => c.Mobile!).SingleAsync());
        var client = api.Client();
        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request", new { channel = "mobile", destination = mobile });
        await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
                new { challengeId = challenge.Str("challengeId"), code = SignIn.Code }))
            .ErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task Refresh_rotates_and_a_reused_token_ends_every_session()
    {
        await using var api = await TestApi.StartAsync(server);
        var session = await api.CustomerSessionAsync();
        var client = api.Client();

        var first = await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.Str("refreshToken") });
        first.MatchesContract("user", "POST /auth/refresh");
        Assert.NotEqual(session.Str("refreshToken"), first.Str("refreshToken"));

        await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.Str("refreshToken") }))
            .ErrorAsync(HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
        // Reuse looks like theft, so the newer token stops working as well.
        await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.Str("refreshToken") }))
            .ErrorAsync(HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
    }

    [Fact]
    public async Task Access_tokens_expire_after_an_hour_and_logout_revokes_refresh()
    {
        await using var api = await TestApi.StartAsync(server);
        var session = await api.CustomerSessionAsync();
        var client = api.WithToken(session.Str("accessToken"));

        api.Clock.Advance(TimeSpan.FromMinutes(61));
        await (await client.PostAsync("/api/v1/auth/logout", null)).ErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHORIZED");

        var refreshed = await api.Client().PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.Str("refreshToken") });
        var signedIn = api.WithToken(refreshed.Str("accessToken"));
        Assert.Equal(HttpStatusCode.NoContent, (await signedIn.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        await (await api.Client().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refreshed.Str("refreshToken") }))
            .ErrorAsync(HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
    }

    [Fact]
    public async Task Other_audiences_tokens_are_refused()
    {
        await using var api = await TestApi.StartAsync(server);
        var collector = await api.CollectorAsync();
        await (await collector.PostAsync("/api/v1/auth/logout", null)).ErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await api.Client().PostAsync("/api/v1/auth/logout", null)).ErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }
}
