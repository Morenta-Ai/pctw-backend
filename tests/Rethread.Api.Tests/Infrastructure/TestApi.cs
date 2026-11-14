using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Rethread.Api.Data;

namespace Rethread.Api.Tests.Infrastructure;

/// <summary>The API on its own fresh database, with a fake clock at <see cref="TestClock.Start"/>.</summary>
public sealed class TestApi : IAsyncDisposable
{
    private readonly PostgresServer _server;
    private readonly string _connectionString;

    private TestApi(PostgresServer server, string connectionString)
    {
        _server = server;
        _connectionString = connectionString;
        Factory = new TestApiFactory(connectionString, TestClock.Start);
    }

    public TestApiFactory Factory { get; }
    public FakeTimeProvider Clock => Factory.Clock;

    public static async Task<TestApi> StartAsync(PostgresServer server) =>
        new(server, await server.CreateDatabaseAsync());

    public HttpClient Client() => Factory.CreateClient();

    /// <summary>Runs <paramref name="action"/> against the database in its own scope.</summary>
    public async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task DbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _server.DropDatabaseAsync(_connectionString);
    }
}
