using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Rethread.Api.Tests.Infrastructure;

public sealed class TestApiFactory(string connectionString, DateTimeOffset now,
    IReadOnlyDictionary<string, string>? settings = null) : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Seed:OnStartup", "false");
        builder.UseSetting("Maintenance:Enabled", "false");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-that-is-long-enough-for-hs256-0123456789");
        builder.UseSetting("Otp:FixedCode", "123456");
        builder.UseSetting("Storage:Root", Path.Combine(Path.GetTempPath(), "rethread-tests", Guid.NewGuid().ToString("N")));
        foreach (var (key, value) in settings ?? new Dictionary<string, string>()) builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}
