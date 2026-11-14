using System.Security.Cryptography;

namespace Rethread.Api.Common;

/// <summary>Opaque IDs like <c>cus_k3v9q2m8x4tz</c>. Pickups, orders and collector codes use sequences.</summary>
public static class Ids
{
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    public static string New(string prefix, int length = 12) => $"{prefix}_{Random(length)}";

    public static string Random(int length, string alphabet = Alphabet) =>
        string.Create(length, alphabet, static (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
}
