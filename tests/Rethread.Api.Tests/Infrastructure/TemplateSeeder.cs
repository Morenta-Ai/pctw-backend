using Microsoft.Extensions.DependencyInjection;
using Rethread.Api.Data.Seed;

namespace Rethread.Api.Tests.Infrastructure;

/// <summary>Every test database starts with the dev seed, at <see cref="TestClock.Start"/>.</summary>
internal static class TemplateSeeder
{
    public static Task SeedAsync(IServiceProvider services) =>
        services.GetRequiredService<DevSeeder>().SeedAsync();
}
