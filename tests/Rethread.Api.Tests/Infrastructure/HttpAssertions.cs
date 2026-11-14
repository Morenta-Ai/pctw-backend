using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rethread.Api.Tests.Infrastructure;

public static class HttpAssertions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Asserts the contract error body: the status and its <c>code</c>.</summary>
    public static async Task<JsonElement> ErrorAsync(this HttpResponseMessage response, HttpStatusCode status,
        string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status,
            $"Expected {(int)status} {code} but got {(int)response.StatusCode}: {body}");
        var json = JsonDocument.Parse(body).RootElement.Clone();
        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("message").GetString()));
        return json;
    }

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string path,
        HttpStatusCode expected = HttpStatusCode.OK) =>
        await (await client.GetAsync(path)).JsonAsync(expected);

    public static async Task<JsonElement> PostJsonAsync(this HttpClient client, string path, object? body,
        HttpStatusCode expected = HttpStatusCode.OK) =>
        await (await client.PostAsJsonAsync(path, body)).JsonAsync(expected);

    public static string Str(this JsonElement element, string property) =>
        element.GetProperty(property).GetString()!;
}
