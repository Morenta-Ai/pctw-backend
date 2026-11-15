using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Rethread.Api.Common;

namespace Rethread.Api.Auth;

public static class AuthSetup
{
    public const string AuthRateLimit = "auth";

    public static IServiceCollection AddRethreadAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters.")
            .ValidateOnStart();
        services.AddOptions<OtpOptions>().Bind(config.GetSection(OtpOptions.Section));

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<CurrentUser>();
        services.AddScoped<TokenService>();
        services.AddScoped<OtpService>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<DevOutbox>();
        services.AddSingleton<ISmsSender, LoggingSmsSender>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Customer, p => p.RequireAuthenticatedUser().RequireClaim("role", Roles.Customer))
            .AddPolicy(Policies.Collector, p => p.RequireAuthenticatedUser().RequireClaim("role", Roles.Collector))
            .AddPolicy(Policies.Staff, p => p.RequireAuthenticatedUser()
                .RequireClaim("role", Roles.Admin, Roles.CallCentre));

        services.AddRateLimiter(o =>
        {
            o.AddPolicy(AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
            o.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var (_, body) = ErrorDialect.Translate(context.HttpContext, StatusCodes.Status429TooManyRequests,
                    new ErrorBody(ErrorCodes.TooManyRequests, "Too many requests. Try again in a minute."));
                await context.HttpContext.Response.WriteAsJsonAsync(body, ct);
            };
        });

        return services;
    }

    /// <summary>Token validation, using <see cref="TimeProvider"/> for lifetimes so tests can move the clock.</summary>
    private sealed class ConfigureJwtBearer(IOptions<JwtOptions> jwt, TimeProvider time)
        : IConfigureNamedOptions<JwtBearerOptions>
    {
        public void Configure(string? name, JwtBearerOptions options) => Configure(options);

        public void Configure(JwtBearerOptions options)
        {
            var settings = jwt.Value;
            options.MapInboundClaims = false;
            options.TimeProvider = time;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = TokenService.SigningKey(settings),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = "sub",
                RoleClaimType = "role",
                ClockSkew = TimeSpan.FromSeconds(30),
                LifetimeValidator = (notBefore, expires, _, parameters) =>
                {
                    var now = time.GetUtcNow().UtcDateTime;
                    return expires is { } e && e > now - parameters.ClockSkew &&
                           (notBefore is not { } nb || nb <= now + parameters.ClockSkew);
                },
            };
        }
    }
}
