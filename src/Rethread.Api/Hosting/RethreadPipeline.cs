using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Data.Seed;
using Rethread.Api.Features.Admin.Auth;
using Rethread.Api.Features.Collector.Auth;
using Rethread.Api.Features.Collector.Jobs;
using Rethread.Api.Features.Collector.Notifications;
using Rethread.Api.Features.Collector.Profile;
using Rethread.Api.Features.Customer.Addresses;
using Rethread.Api.Features.Customer.Auth;
using Rethread.Api.Features.Customer.Family;
using Rethread.Api.Features.Customer.Notifications;
using Rethread.Api.Features.Customer.Pickups;
using Rethread.Api.Features.Customer.Profile;
using Rethread.Api.Features.Customer.Wallet;
using Scalar.AspNetCore;

namespace Rethread.Api.Hosting;

public static class RethreadPipeline
{
    public static WebApplication UseRethread(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseContractStatusCodes();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
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

        // One route group per audience, each with its own policy. Suspended accounts are turned away on
        // every request, whatever their token says.
        var api = app.MapGroup("/api/v1");

        var customer = api.MapGroup("").WithTags("Customer app")
            .RequireAuthorization(Policies.Customer)
            .AddEndpointFilter(new ActiveAccountFilter(Audience.Customer));
        customer.MapCustomerAuth();
        customer.MapCustomerProfile();
        customer.MapAddresses();
        customer.MapFamily();
        customer.MapBankDetails();
        customer.MapCustomerNotifications();
        customer.MapCustomerPickups();

        var collector = api.MapGroup("/collector").WithTags("Collector app")
            .RequireAuthorization(Policies.Collector)
            .AddEndpointFilter(new ActiveAccountFilter(Audience.Collector));
        collector.MapCollectorAuth();
        collector.MapCollectorProfile();
        collector.MapJobs();
        collector.MapCollectorNotifications();

        var admin = api.MapGroup("/admin").WithTags("Admin portal")
            .RequireAuthorization(Policies.Staff)
            .AddEndpointFilter(new ActiveAccountFilter(Audience.Staff));
        admin.MapStaffAuth();

        return app;
    }

    /// <summary>
    /// Applies pending migrations when <c>Database:MigrateOnStartup</c> is on, and seeds an empty database
    /// when <c>Seed:OnStartup</c> is on (local dev only).
    /// </summary>
    public static async Task PrepareDatabaseAsync(this WebApplication app)
    {
        var migrate = app.Configuration.GetValue<bool>("Database:MigrateOnStartup");
        var seed = app.Configuration.GetValue<bool>("Seed:OnStartup") && !app.Environment.IsProduction();
        if (!migrate && !seed) return;

        await using var scope = app.Services.CreateAsyncScope();
        if (migrate) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        if (seed) await scope.ServiceProvider.GetRequiredService<DevSeeder>().SeedAsync();
    }

    /// <summary>
    /// <c>dotnet run -- seed [--reset]</c>: migrates and seeds the database, then exits. <c>--reset</c> drops
    /// the database first. Refuses to run in Production.
    /// </summary>
    public static async Task<int> RunSeedCommandAsync(this WebApplication app, string[] args)
    {
        if (app.Environment.IsProduction())
        {
            Console.Error.WriteLine("Refusing to seed a Production database.");
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (args.Contains("--reset"))
        {
            await db.Database.EnsureDeletedAsync();
            Console.WriteLine("Dropped the database.");
        }

        await db.Database.MigrateAsync();
        var seeded = await scope.ServiceProvider.GetRequiredService<DevSeeder>().SeedAsync();
        Console.WriteLine(seeded
            ? "Seeded the database."
            : "The database already has data. Run `seed --reset` to start again.");
        return 0;
    }
}
