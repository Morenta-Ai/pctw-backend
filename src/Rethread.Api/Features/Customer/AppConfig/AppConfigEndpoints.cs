namespace Rethread.Api.Features.Customer.AppConfig;

public sealed record AppConfigDto(
    AppConfigDto.SupportInfo Support,
    AppConfigDto.LegalInfo Legal,
    AppConfigDto.Stores StoreUrls,
    AppConfigDto.FeatureFlags Features,
    string MinSupportedVersion,
    string LatestVersion)
{
    public sealed record SupportInfo(string Phone, string? Whatsapp, string? Email);

    public sealed record LegalInfo(string TermsUrl, string PrivacyUrl);

    public sealed record Stores(string Android, string Ios);

    public sealed record FeatureFlags(bool WalletEnabled, bool? PushEnabled);
}

/// <summary>
/// The user app reads this at startup, signed in or not, so contacts, flags and the minimum version change
/// without a release. Values come from the <c>App</c> configuration section.
/// </summary>
public static class AppConfigEndpoints
{
    public static void MapAppConfig(this RouteGroupBuilder api) =>
        api.MapGet("/app/config", (IConfiguration config) =>
        {
            var app = config.GetSection("App");
            string? Optional(string key) => app[key] is { Length: > 0 } value ? value : null;
            return TypedResults.Ok(new AppConfigDto(
                new AppConfigDto.SupportInfo(app["SupportPhone"] ?? "+94117544444", Optional("SupportWhatsapp") ?? "+94771234500",
                    Optional("SupportEmail") ?? "support@rethread.lk"),
                new AppConfigDto.LegalInfo(app["TermsUrl"] ?? "https://rethread.lk/terms", app["PrivacyUrl"] ?? "https://rethread.lk/privacy"),
                new AppConfigDto.Stores(app["AndroidStoreUrl"] ?? "https://play.google.com/store/apps/details?id=com.morenta.pctw.user",
                    app["IosStoreUrl"] ?? "https://apps.apple.com/lk/app/rethread/id0000000000"),
                new AppConfigDto.FeatureFlags(app.GetValue("WalletEnabled", false), app.GetValue<bool?>("PushEnabled")),
                app["MinSupportedVersion"] ?? "0.1.0",
                app["LatestVersion"] ?? "0.1.0"));
        }).AllowAnonymous();
}
