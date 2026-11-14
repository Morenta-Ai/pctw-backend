using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Data.Seed;
using Rethread.Api.Features.Admin.Assignments;
using Rethread.Api.Features.Admin.Auth;
using Rethread.Api.Features.Admin.Catalogue;
using Rethread.Api.Features.Admin.Collectors;
using Rethread.Api.Features.Admin.Customers;
using Rethread.Api.Features.Admin.Dashboard;
using Rethread.Api.Features.Admin.Dispatch;
using Rethread.Api.Features.Admin.DonationProjects;
using Rethread.Api.Features.Admin.Notifications;
using Rethread.Api.Features.Admin.Orders;
using Rethread.Api.Features.Admin.Pickups;
using Rethread.Api.Features.Admin.Rates;
using Rethread.Api.Features.Admin.Redemptions;
using Rethread.Api.Features.Admin.Staff;
using Rethread.Api.Features.Collector.Assignments;
using Rethread.Api.Features.Collector.Auth;
using Rethread.Api.Features.Collector.Earnings;
using Rethread.Api.Features.Collector.Jobs;
using Rethread.Api.Features.Collector.Load;
using Rethread.Api.Features.Collector.Notifications;
using Rethread.Api.Features.Collector.Profile;
using Rethread.Api.Features.Customer.Addresses;
using Rethread.Api.Features.Customer.Auth;
using Rethread.Api.Features.Customer.Family;
using Rethread.Api.Features.Customer.Impact;
using Rethread.Api.Features.Customer.Notifications;
using Rethread.Api.Features.Customer.Orders;
using Rethread.Api.Features.Customer.Pickups;
using Rethread.Api.Features.Customer.Profile;
using Rethread.Api.Features.Customer.Shop;
using Rethread.Api.Features.Customer.Wallet;
using Scalar.AspNetCore;

namespace Rethread.Api.Hosting;

public static class RethreadPipeline
{
    public static WebApplication UseRethread(this WebApplication app)
    {
        // Behind a load balancer or platform proxy, trust its X-Forwarded-* headers so client IPs (for rate
        // limits) and https links (for uploaded photos) are right.
        if (app.Configuration.GetValue<bool>("Hosting:TrustForwardedHeaders"))
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            };
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            app.UseForwardedHeaders(options);
        }

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
        customer.MapWallet();
        customer.MapShop();
        customer.MapCustomerOrders();
        customer.MapImpact();

        var collector = api.MapGroup("/collector").WithTags("Collector app")
            .RequireAuthorization(Policies.Collector)
            .AddEndpointFilter(new ActiveAccountFilter(Audience.Collector));
        collector.MapCollectorAuth();
        collector.MapCollectorProfile();
        collector.MapJobs();
        collector.MapCollectorNotifications();
        collector.MapLoad();
        collector.MapEarnings();
        collector.MapCollectorAssignments();

        var admin = api.MapGroup("/admin").WithTags("Admin portal")
            .RequireAuthorization(Policies.Staff)
            .AddEndpointFilter(new ActiveAccountFilter(Audience.Staff));
        admin.MapStaffAuth();
        admin.MapAdminAssignments();
        admin.MapAdminRedemptions();
        admin.MapAdminDonationProjects();
        admin.MapAdminOrders();
        admin.MapCatalogue();
        admin.MapDashboard();
        admin.MapAdminPickups();
        admin.MapDispatch();
        admin.MapAdminCustomers();
        admin.MapAdminCollectors();
        admin.MapStaff();
        admin.MapStaffNotifications();
        admin.MapRates();

        app.MapFiles();

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

    /// <summary><c>dotnet Rethread.Api.dll migrate</c>: applies pending migrations, then exits. For deploy pipelines.</summary>
    public static async Task<int> RunMigrateCommandAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        await db.Database.MigrateAsync();
        Console.WriteLine(pending.Count == 0
            ? "The database is up to date."
            : $"Applied {pending.Count} migration(s): {string.Join(", ", pending)}");
        return 0;
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
