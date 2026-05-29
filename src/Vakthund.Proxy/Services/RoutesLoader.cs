using Vakthund.Proxy.Helpers;
using Vakthund.Proxy.Models;
using Vakthund.Shared.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vakthund.Proxy.Services;

public static class RoutesLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new RouteAuthDefinitionConverter())
        .WithTypeConverter(new RouteIpDefinitionConverter())
        .IgnoreUnmatchedProperties()
        .Build();

    public static List<VakthundRoute>? TryLoad(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        string yaml = File.ReadAllText(filePath);
        var file = Deserializer.Deserialize<RoutesFileDefinition>(yaml);
        Dictionary<string, AuthExpectation> namedAuths = BuildNamedAuths(file.Auths);
        Dictionary<string, List<string>> namedIps = BuildNamedIps(file.IpPolicies);
        List<VakthundRoute> routes = file.Routes
            .Select(route => new VakthundRoute
            {
                Path = route.Path,
                Target = route.Target,
                To = route.To,
                Timeout = route.Timeout,
                Hosts = NormalizeHosts(route.Hosts),
                Ips = ResolveIps(route.Ips, namedIps, route.Path),
                Auth = ResolveAuth(route.Auth, namedAuths, route.Path)
            })
            .ToList();

        return routes.Count > 0 ? routes : null;
    }

    private static Dictionary<string, AuthExpectation> BuildNamedAuths(IEnumerable<NamedAuthExpectation> auths)
    {
        var namedAuths = new Dictionary<string, AuthExpectation>(StringComparer.Ordinal);

        foreach (NamedAuthExpectation auth in auths)
        {
            string? name = auth.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Routes file auths entries must define a non-empty name.");
            }

            if (!namedAuths.TryAdd(name, CloneAuth(auth)))
            {
                throw new InvalidOperationException($"Routes file defines duplicate auth name '{name}'.");
            }
        }

        return namedAuths;
    }

    private static Dictionary<string, List<string>> BuildNamedIps(IEnumerable<IpPolicy> ips)
    {
        var namedIps = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (IpPolicy ipList in ips)
        {
            string? name = ipList.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Routes file ipPolicies entries must define a non-empty name.");
            }

            if (!namedIps.TryAdd(name, ipList.Entries))
            {
                throw new InvalidOperationException($"Routes file defines duplicate ip policy name '{name}'.");
            }
        }

        return namedIps;
    }

    private static List<string> ResolveIps(
        RouteIpDefinition? routeIps,
        IReadOnlyDictionary<string, List<string>> namedIps,
        string routePath)
    {
        if (routeIps is null)
        {
            return [];
        }

        if (routeIps.Inline is { } inlineIps)
        {
            return [.. inlineIps];
        }

        string? name = routeIps.Reference?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"Route '{routePath}' has an empty ip reference.");
        }

        if (!namedIps.TryGetValue(name, out List<string>? namedIpList))
        {
            throw new InvalidOperationException($"Route '{routePath}' references unknown ip policy '{name}'. Define it under ipPolicies.");
        }

        return [.. namedIpList];
    }

    private static AuthExpectation? ResolveAuth(
        RouteAuthDefinition? routeAuth,
        IReadOnlyDictionary<string, AuthExpectation> namedAuths,
        string routePath)
    {
        if (routeAuth is null)
        {
            return null;
        }

        if (routeAuth.Inline is { } inlineAuth)
        {
            return CloneAuth(inlineAuth);
        }

        string? name = routeAuth.Reference?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"Route '{routePath}' has an empty auth reference.");
        }

        if (!namedAuths.TryGetValue(name, out AuthExpectation? namedAuth))
        {
            throw new InvalidOperationException($"Route '{routePath}' references unknown auth '{name}'. Define it under auths.");
        }

        return CloneAuth(namedAuth);
    }

    private static AuthExpectation CloneAuth(AuthExpectation auth)
    {
        JweDecryptionConfig jwe = auth.Jwe ?? new JweDecryptionConfig();
        return new AuthExpectation
        {
            Enforced = auth.Enforced,
            Issuer = auth.Issuer,
            Audience = auth.Audience,
            Audiences = auth.Audiences is null ? [] : [.. auth.Audiences],
            Scopes = auth.Scopes is null ? [] : [.. auth.Scopes],
            Roles = auth.Roles is null ? [] : [.. auth.Roles],
            OpenIdConfigurationUrl = auth.OpenIdConfigurationUrl,
            JwksUrl = auth.JwksUrl,
            Jwe = new JweDecryptionConfig
            {
                KeyType = jwe.KeyType,
                Key = jwe.Key
            }
        };
    }

    private static List<string> NormalizeHosts(IEnumerable<string>? hosts) =>
        (hosts ?? Enumerable.Empty<string>()).Select(host => host.Trim())
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

}
