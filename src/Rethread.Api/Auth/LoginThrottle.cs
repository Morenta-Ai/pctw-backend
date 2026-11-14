using Microsoft.Extensions.Caching.Memory;
using Rethread.Api.Common;

namespace Rethread.Api.Auth;

/// <summary>Slows password guessing: five wrong passwords for an email lock it for 15 minutes.</summary>
public sealed class LoginThrottle(IMemoryCache cache)
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public void EnsureAllowed(string email)
    {
        if (cache.TryGetValue(Key(email), out int failures) && failures >= MaxFailures)
        {
            throw ApiException.TooManyRequests(AuthErrors.TooManyAttempts,
                "Too many sign-in attempts. Try again in 15 minutes.");
        }
    }

    public void Failed(string email)
    {
        var key = Key(email);
        cache.TryGetValue(key, out int failures);
        cache.Set(key, failures + 1, Window);
    }

    public void Succeeded(string email) => cache.Remove(Key(email));

    private static string Key(string email) => $"login-failures:{email.ToLowerInvariant()}";
}
