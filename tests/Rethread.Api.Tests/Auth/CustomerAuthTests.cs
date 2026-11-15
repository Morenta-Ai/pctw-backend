using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Auth;

public sealed class CustomerAuthTests(PostgresServer server)
{
    private const string Contract = "user";

    private static object Request(string destination, string channel = "sms") => new { channel, destination };

    [Fact]
    public async Task An_existing_customer_signs_in_with_a_code()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = api.Client();

        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request", Request(SignIn.NadeeshaMobile));
        challenge.MatchesContract(Contract, "POST /auth/otp/request");
        Assert.Equal((300, 60), (challenge.GetProperty("expiresInSeconds").GetInt32(), challenge.GetProperty("resendAfterSeconds").GetInt32()));

        var session = await client.PostJsonAsync("/api/v1/auth/otp/verify",
            new { requestId = challenge.Str("requestId"), code = SignIn.Code });
        ShapeAssert.Matches(ContractSamples.Response(Contract, "GET /me"), session.GetProperty("user"));
        Assert.Equal((3600, false), (session.GetProperty("expiresIn").GetInt32(), session.GetProperty("isNewUser").GetBoolean()));
        Assert.Equal(("cus_1", "Nadeesha Perera", "en"), (session.GetProperty("user").Str("id"),
            session.GetProperty("user").Str("fullName"), session.GetProperty("user").Str("preferredLanguage")));
    }

    [Fact]
    public async Task A_new_number_or_email_creates_an_account()
    {
        await using var api = await TestApi.StartAsync(server);
        var session = await api.CustomerSessionAsync("+94711112222");
        Assert.True(session.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("user").GetProperty("fullName").ValueKind);

        var client = api.Client();
        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request", Request("New.Person@Example.lk", "email"));
        var byEmail = await client.PostJsonAsync("/api/v1/auth/otp/verify", new { requestId = challenge.Str("requestId"), code = SignIn.Code });
        Assert.Equal("new.person@example.lk", byEmail.GetProperty("user").Str("email"));
    }

    [Fact]
    public async Task Wrong_expired_and_spammed_codes_use_the_apps_error_codes()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = api.Client();
        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request", Request(SignIn.NadeeshaMobile));

        await (await client.PostAsJsonAsync("/api/v1/auth/otp/request", Request(SignIn.NadeeshaMobile)))
            .ErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        for (var i = 0; i < 5; i++)
        {
            await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { requestId = challenge.Str("requestId"), code = "000000" }))
                .ErrorAsync(HttpStatusCode.BadRequest, "OTP_INVALID");
        }

        await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { requestId = challenge.Str("requestId"), code = SignIn.Code }))
            .ErrorAsync(HttpStatusCode.TooManyRequests, "OTP_TOO_MANY_ATTEMPTS");
        await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { requestId = "otp_unknown", code = SignIn.Code }))
            .ErrorAsync(HttpStatusCode.BadRequest, "OTP_EXPIRED");

        api.Clock.Advance(TimeSpan.FromMinutes(2));
        var fresh = await client.PostJsonAsync("/api/v1/auth/otp/request", Request(SignIn.NadeeshaMobile));
        api.Clock.Advance(TimeSpan.FromMinutes(6));
        await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { requestId = fresh.Str("requestId"), code = SignIn.Code }))
            .ErrorAsync(HttpStatusCode.BadRequest, "OTP_EXPIRED");
        var error = await (await client.PostAsJsonAsync("/api/v1/auth/otp/request", Request("0771234567")))
            .ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        Assert.True(error.GetProperty("details").TryGetProperty("destination", out _));
    }

    [Fact]
    public async Task Suspended_customers_cannot_sign_in()
    {
        await using var api = await TestApi.StartAsync(server);
        var mobile = await api.DbAsync(db => db.Customers.Where(c => c.Id == "cus_18").Select(c => c.Mobile!).SingleAsync());
        var client = api.Client();
        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request", Request(mobile));
        await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { requestId = challenge.Str("requestId"), code = SignIn.Code }))
            .ErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task Refresh_rotates_and_logout_revokes_with_the_refresh_token()
    {
        await using var api = await TestApi.StartAsync(server);
        var session = await api.CustomerSessionAsync();
        var client = api.Client();

        var refreshed = await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.Str("refreshToken") });
        refreshed.MatchesContract(Contract, "POST /auth/refresh");
        await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.Str("refreshToken") }))
            .ErrorAsync(HttpStatusCode.Unauthorized, "SESSION_EXPIRED");

        api.Clock.Advance(TimeSpan.FromMinutes(61));
        // Signing out works even with an expired access token: the refresh token is the credential.
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = refreshed.Str("refreshToken") })).StatusCode);
        await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refreshed.Str("refreshToken") }))
            .ErrorAsync(HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
    }

    [Fact]
    public async Task Other_audiences_tokens_are_refused()
    {
        await using var api = await TestApi.StartAsync(server);
        var collector = await api.CollectorAsync();
        await (await collector.GetAsync("/api/v1/me")).ErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await api.Client().GetAsync("/api/v1/me")).ErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }
}
