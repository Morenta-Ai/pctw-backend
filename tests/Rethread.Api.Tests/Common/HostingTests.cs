using System.Net;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Common;

public sealed class HostingTests(PostgresServer server)
{
    [Fact]
    public async Task Health_reports_the_database()
    {
        await using var api = await TestApi.StartAsync(server);
        var json = await api.Client().GetJsonAsync("/health");
        Assert.Equal("healthy", json.Str("status"));
    }

    [Fact]
    public async Task Unknown_routes_get_the_contract_error_body()
    {
        await using var api = await TestApi.StartAsync(server);
        await (await api.Client().GetAsync("/api/v1/nothing-here")).ErrorAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        await using var api = await TestApi.StartAsync(server);
        var json = await api.Client().GetJsonAsync("/openapi/v1.json");
        Assert.Equal("3.1.1", json.Str("openapi"));
    }
}
