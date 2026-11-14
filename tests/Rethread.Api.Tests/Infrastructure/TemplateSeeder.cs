namespace Rethread.Api.Tests.Infrastructure;

/// <summary>What every test database starts with. Filled in once the seed exists.</summary>
internal static class TemplateSeeder
{
    public static Task SeedAsync(IServiceProvider services) => Task.CompletedTask;
}
