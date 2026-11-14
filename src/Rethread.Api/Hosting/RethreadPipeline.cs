using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Scalar.AspNetCore;

namespace Rethread.Api.Hosting;

public static class RethreadPipeline
{
    public static WebApplication UseRethread(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseContractStatusCodes();
        return app;
    }

    public static WebApplication MapRethread(this WebApplication app)
    {
        app.MapOpenApi("/openapi/{documentName}.json");
        app.MapScalarApiReference("/docs", o => o.WithTitle("ReThread API"));

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(
                new { status = report.Status.ToString().ToLowerInvariant() }, Json.Options),
        });

        app.MapGet("/", () => Results.Redirect("/docs")).ExcludeFromDescription();

        // Route groups by audience. Features add their endpoints to these.
        var api = app.MapGroup("/api/v1");
        _ = api.MapGroup("").WithTags("Customer app");
        _ = api.MapGroup("/collector").WithTags("Collector app");
        _ = api.MapGroup("/admin").WithTags("Admin portal");

        return app;
    }

    /// <summary>Applies pending migrations when <c>Database:MigrateOnStartup</c> is on (local dev).</summary>
    public static async Task MigrateIfConfiguredAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Database:MigrateOnStartup")) return;
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }
}
