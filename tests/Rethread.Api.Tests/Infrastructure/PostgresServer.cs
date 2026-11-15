using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Rethread.Api.Data;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Rethread.Api.Tests.Infrastructure.PostgresServer))]

namespace Rethread.Api.Tests.Infrastructure;

/// <summary>
/// One PostgreSQL server for the whole test run. Set <c>TEST_DATABASE_URL</c> (an Npgsql connection string for
/// a role that can create databases) to use an existing server; otherwise a Testcontainers one starts.
/// A template database is migrated (and seeded) once, and every test clones it.
/// </summary>
public sealed class PostgresServer : IAsyncLifetime
{
    private readonly string _template = $"rethread_template_{Guid.NewGuid():N}";
    private PostgreSqlContainer? _container;
    private string _serverConnection = "";
    private readonly SemaphoreSlim _templateLock = new(1, 1);
    private bool _templateReady;

    public async ValueTask InitializeAsync()
    {
        var url = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(url))
        {
            _serverConnection = url;
            return;
        }

        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _container.StartAsync();
        _serverConnection = _container.GetConnectionString();
    }

    /// <summary>Creates a fresh database from the template and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        await EnsureTemplateAsync();
        var name = $"rethread_test_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{_template}\"");
        return ConnectionFor(name);
    }

    /// <summary>
    /// Best effort: a background PostgreSQL process (autovacuum, say) can briefly hold a connection that a
    /// non-superuser role isn't allowed to end. A leftover test database is harmless; a failed test isn't.
    /// </summary>
    public async Task DropDatabaseAsync(string connectionString)
    {
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        NpgsqlConnection.ClearAllPools();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
                return;
            }
            catch (PostgresException) when (attempt < 3)
            {
                await Task.Delay(200 * attempt);
            }
            catch (PostgresException)
            {
                return;
            }
        }
    }

    private async Task EnsureTemplateAsync()
    {
        if (_templateReady) return;
        await _templateLock.WaitAsync();
        try
        {
            if (_templateReady) return;
            await ExecuteAsync($"DROP DATABASE IF EXISTS \"{_template}\" WITH (FORCE)");
            await ExecuteAsync($"CREATE DATABASE \"{_template}\"");
            var connection = ConnectionFor(_template, pooling: false);
            // Migrate before the API starts, so nothing at start-up queries tables that don't exist yet.
            await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                             .UseNpgsql(connection, o => o.MigrationsHistoryTable("__ef_migrations_history"))
                             .UseSnakeCaseNamingConvention().Options))
            {
                await db.Database.MigrateAsync();
            }

            await using (var api = new TestApiFactory(connection, TestClock.Start))
            {
                await using var scope = api.Services.CreateAsyncScope();
                await TemplateSeeder.SeedAsync(scope.ServiceProvider);
            }

            NpgsqlConnection.ClearAllPools();
            _templateReady = true;
        }
        finally
        {
            _templateLock.Release();
        }
    }

    private string ConnectionFor(string database, bool pooling = true) =>
        new NpgsqlConnectionStringBuilder(_serverConnection)
        {
            Database = database,
            Pooling = pooling,
            IncludeErrorDetail = true,
        }.ConnectionString;

    private async Task ExecuteAsync(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(_serverConnection) { Pooling = false };
        if (string.IsNullOrEmpty(builder.Database)) builder.Database = "postgres";
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync($"DROP DATABASE IF EXISTS \"{_template}\" WITH (FORCE)");
    }
}
