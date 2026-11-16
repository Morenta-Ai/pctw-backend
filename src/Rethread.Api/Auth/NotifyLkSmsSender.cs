using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Rethread.Api.Auth;

/// <summary>notify.lk account settings. SMS go to the log until <see cref="ApiKey"/> is set.</summary>
public sealed class NotifyLkOptions
{
    public const string Section = "Sms:NotifyLk";

    public string UserId { get; set; } = "";
    /// <summary>From the notify.lk settings page. Set it from a secret store; never commit it.</summary>
    public string ApiKey { get; set; } = "";
    /// <summary>
    /// An approved sender ID (mask), such as <c>ReThread</c>. notify.lk's <c>NotifyDEMO</c> is for testing
    /// only: sending sign-in codes from it can get the account suspended.
    /// </summary>
    public string SenderId { get; set; } = "NotifyDEMO";
    public Uri BaseUrl { get; set; } = new("https://app.notify.lk/api/v1/");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Sends text messages through notify.lk (https://developer.notify.lk).</summary>
public sealed class NotifyLkSmsSender(HttpClient http, IOptions<NotifyLkOptions> options,
    ILogger<NotifyLkSmsSender> logger) : ISmsSender
{
    private readonly NotifyLkOptions _options = options.Value;

    public async Task SendAsync(string mobile, string text, CancellationToken ct)
    {
        // POST keeps the API key out of request URLs and logs.
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["user_id"] = _options.UserId,
            ["api_key"] = _options.ApiKey,
            ["sender_id"] = _options.SenderId,
            ["to"] = ToNotifyLkNumber(mobile),
            ["message"] = text,
        });

        string body;
        int status;
        try
        {
            using var response = await http.PostAsync(new Uri(_options.BaseUrl, "send"), form, ct);
            status = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException e)
        {
            throw new SmsNotSentException($"notify.lk could not be reached: {e.Message}", e);
        }

        if (IsSuccess(body)) return;

        logger.LogError("notify.lk did not send an SMS to {Mobile}: HTTP {Status} {Body}", Mask(mobile), status, body);
        throw new SmsNotSentException($"notify.lk answered HTTP {status}: {body}");
    }

    /// <summary>notify.lk wants <c>9477…</c>: the country code without <c>+</c>.</summary>
    public static string ToNotifyLkNumber(string mobile)
    {
        var digits = new string([.. mobile.Where(char.IsAsciiDigit)]);
        return digits.StartsWith('0') && digits.Length == 10 ? $"94{digits[1..]}" : digits;
    }

    private static bool IsSuccess(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                   json.RootElement.TryGetProperty("status", out var s) &&
                   s.ValueKind == JsonValueKind.String && s.GetString() == "success";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Mask(string mobile) => mobile.Length <= 4 ? "****" : $"****{mobile[^4..]}";
}

public sealed class SmsNotSentException(string message, Exception? inner = null) : Exception(message, inner);
