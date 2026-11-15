using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;
using Rethread.Api.Data.Seed;
using Rethread.Api.Domain;
using Rethread.Api.Features.Shared;

namespace Rethread.Api.Hosting;

public static class RethreadServices
{
    public static WebApplicationBuilder AddRethread(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services.AddSingleton(TimeProvider.System);

        services.ConfigureHttpJsonOptions(o => Json.Configure(o.SerializerOptions));
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();
        services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);

        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.");
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history")
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention());

        services.AddDataProtection()
            .SetApplicationName("rethread-api")
            .PersistKeysToDbContext<AppDbContext>();

        services.AddSingleton<BankAccountProtector>();
        services.AddSingleton<IPasswordHasher<Staff>, PasswordHasher<Staff>>();
        services.AddScoped<RateBook>();
        services.AddScoped<NotificationService>();
        services.AddScoped<PickupWorkflow>();
        services.AddScoped<WalletLedger>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<Redemptions>();
        services.AddScoped<OrderWorkflow>();
        services.AddScoped<ImpactCalculator>();
        services.AddScoped<PublicUrls>();
        services.AddScoped<OfferSimulator>();
        services.AddScoped<Maintenance>();
        services.AddHostedService<MaintenanceService>();
        services.AddSingleton<IFileStore, LocalFileStore>();
        services.AddScoped<DevSeeder>();
        services.AddScoped<RecipientNotifications>();

        services.AddRethreadAuth(config);

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
        services.AddOpenApi("v1");

        return builder;
    }
}
