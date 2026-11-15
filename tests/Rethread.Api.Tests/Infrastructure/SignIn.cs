using System.Net.Http.Headers;
using System.Text.Json;

namespace Rethread.Api.Tests.Infrastructure;

/// <summary>Signed-in clients for each audience, through the real sign-in endpoints.</summary>
public static class SignIn
{
    public const string Code = "123456";
    public const string NadeeshaMobile = "+94764821190";
    public const string RuwanCode = "COL-0457";
    public const string RuwanMobile = "+94771234567";

    public static async Task<HttpClient> CustomerAsync(this TestApi api, string mobile = NadeeshaMobile)
    {
        var session = await CustomerSessionAsync(api, mobile);
        return api.WithToken(session.Str("accessToken"));
    }

    public static async Task<JsonElement> CustomerSessionAsync(this TestApi api, string mobile = NadeeshaMobile)
    {
        var client = api.Client();
        var challenge = await client.PostJsonAsync("/api/v1/auth/otp/request",
            new { channel = "sms", destination = mobile });
        return await client.PostJsonAsync("/api/v1/auth/otp/verify",
            new { requestId = challenge.Str("requestId"), code = Code });
    }

    public static async Task<HttpClient> CollectorAsync(this TestApi api, string code = RuwanCode,
        string mobile = RuwanMobile)
    {
        var session = await CollectorSessionAsync(api, code, mobile);
        return api.WithToken(session.Str("accessToken"));
    }

    public static async Task<JsonElement> CollectorSessionAsync(this TestApi api, string code = RuwanCode,
        string mobile = RuwanMobile)
    {
        var client = api.Client();
        var challenge = await client.PostJsonAsync("/api/v1/collector/auth/otp/request",
            new { collectorCode = code, mobile });
        return await client.PostJsonAsync("/api/v1/collector/auth/otp/verify",
            new { challengeId = challenge.Str("challengeId"), code = Code });
    }

    public static Task<HttpClient> AdminAsync(this TestApi api) =>
        StaffAsync(api, "admin@rethread.lk", "rethread-admin");

    public static Task<HttpClient> CallCentreAsync(this TestApi api) =>
        StaffAsync(api, "callcentre@rethread.lk", "rethread-callcentre");

    public static async Task<HttpClient> StaffAsync(this TestApi api, string email, string password)
    {
        var session = await api.Client().PostJsonAsync("/api/v1/admin/auth/login", new { email, password });
        return api.WithToken(session.Str("accessToken"));
    }

    public static HttpClient WithToken(this TestApi api, string accessToken)
    {
        var client = api.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
