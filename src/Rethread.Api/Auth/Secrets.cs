using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Rethread.Api.Auth;

public static class Secrets
{
    /// <summary>A random URL-safe token with a prefix, such as <c>rt_…</c> for refresh tokens.</summary>
    public static string Token(string prefix) =>
        $"{prefix}_{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32))}";

    /// <summary>SHA-256, hex. Tokens and codes are high-entropy or short-lived, so no salt is needed.</summary>
    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string SixDigitCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000", System.Globalization.CultureInfo.InvariantCulture);

    public static bool HashesMatch(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
