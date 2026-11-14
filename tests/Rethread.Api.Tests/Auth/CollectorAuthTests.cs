using System.Net;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Auth;

public sealed class CollectorAuthTests(PostgresServer server)
{
    [Fact]
    public async Task A_collector_signs_in_with_code_and_mobile()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = api.Client();
        var challenge = await client.PostJsonAsync("/api/v1/collector/auth/otp/request",
            new { collectorCode = "col-0457", mobile = SignIn.RuwanMobile });
        challenge.MatchesContract("collector", "POST /auth/otp/request");

        var session = await client.PostJsonAsync("/api/v1/collector/auth/otp/verify",
            new { challengeId = challenge.Str("challengeId"), code = SignIn.Code });
        session.MatchesContract("collector", "POST /auth/otp/verify");
        var me = session.GetProperty("collector");
        me.MatchesContract("collector", "GET /me");
        Assert.Equal(("col_457", "COL-0457", "Colombo South"),
            (me.Str("id"), me.Str("collectorCode"), me.GetProperty("area").Str("name")));
        Assert.Equal("threeWheeler", me.GetProperty("vehicle").Str("type"));
        Assert.Equal(12000, me.GetProperty("customerRatePerKgCents").GetInt64());
        Assert.Equal(4000, me.GetProperty("collectorRatePerKgCents").GetInt64());
    }

    [Fact]
    public async Task Code_and_mobile_must_belong_together()
    {
        await using var api = await TestApi.StartAsync(server);
        await (await api.Client().PostAsJsonAsync("/api/v1/collector/auth/otp/request",
                new { collectorCode = "COL-0312", mobile = SignIn.RuwanMobile }))
            .ErrorAsync(HttpStatusCode.NotFound, "COLLECTOR_NOT_FOUND");
    }

    [Fact]
    public async Task Suspended_collectors_cannot_sign_in()
    {
        await using var api = await TestApi.StartAsync(server);
        var mobile = await api.DbAsync(db => db.Collectors.Where(c => c.Code == "COL-0541").Select(c => c.Mobile).SingleAsync());
        await (await api.Client().PostAsJsonAsync("/api/v1/collector/auth/otp/request",
                new { collectorCode = "COL-0541", mobile }))
            .ErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task Logout_takes_the_collector_offline()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CollectorAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/collector/auth/logout", null)).StatusCode);
        Assert.False(await api.DbAsync(db => db.Collectors.Where(c => c.Id == "col_457").Select(c => c.Online).SingleAsync()));
    }

    [Fact]
    public async Task Collector_refresh_tokens_only_work_for_collectors()
    {
        await using var api = await TestApi.StartAsync(server);
        var session = await api.CollectorSessionAsync();
        await (await api.Client().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.Str("refreshToken") }))
            .ErrorAsync(HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
        var refreshed = await api.Client().PostJsonAsync("/api/v1/collector/auth/refresh",
            new { refreshToken = session.Str("refreshToken") });
        refreshed.MatchesContract("collector", "POST /auth/refresh");
    }
}
