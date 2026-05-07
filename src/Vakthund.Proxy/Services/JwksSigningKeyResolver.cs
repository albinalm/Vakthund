using System.Collections.Concurrent;
using Microsoft.IdentityModel.Tokens;
using Vakthund.Proxy.Models;

namespace Vakthund.Proxy.Services;

public static class JwksSigningKeyResolver
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly ConcurrentDictionary<string, JwksSigningKeyCacheEntry> Cache = new(StringComparer.Ordinal);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public static IReadOnlyCollection<SecurityKey> Resolve(string jwksUrl)
    {
        if (Cache.TryGetValue(jwksUrl, out JwksSigningKeyCacheEntry? entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return entry.Keys;
        }

        string json = HttpClient.GetStringAsync(jwksUrl).GetAwaiter().GetResult();
        var keySet = new JsonWebKeySet(json);
        SecurityKey[] keys = keySet.Keys.Cast<SecurityKey>().ToArray();

        if (keys.Length == 0)
        {
            throw new SecurityTokenInvalidSigningKeyException($"No signing keys were returned from {jwksUrl}.");
        }

        Cache[jwksUrl] = new JwksSigningKeyCacheEntry(keys, DateTimeOffset.UtcNow.Add(CacheDuration));
        return keys;
    }
}
