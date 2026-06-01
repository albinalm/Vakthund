using Vakthund.Shared.Models;

namespace Vakthund.UI.Services;

public class ProxyRouteMatcher
{
    public ProxyRouteInfo? FindMatchingRoute(IReadOnlyList<ProxyRouteInfo>? routes, string path, string? host = null)
    {
        return routes?
            .Where(route => RouteHostsMatch(route.Hosts, host) && RouteMatches(route.Path, path))
            .OrderByDescending(route => route.Priority)
            .ThenByDescending(route => RouteSpecificity(route.Path))
            .ThenByDescending(route => HostSpecificity(route.Hosts))
            .FirstOrDefault();
    }

    private static bool RouteHostsMatch(IReadOnlyList<string>? routeHosts, string? requestHost)
    {
        if (routeHosts is null || routeHosts.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(requestHost))
        {
            return false;
        }

        string normalizedHost = StripPort(requestHost);
        return routeHosts.Any(routeHost => HostPatternMatches(routeHost, requestHost) || HostPatternMatches(routeHost, normalizedHost));
    }

    private static bool HostPatternMatches(string pattern, string host)
    {
        pattern = pattern.Trim();
        host = host.Trim();

        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(host))
        {
            return false;
        }

        if (pattern == "*")
        {
            return true;
        }

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            string suffix = pattern[1..];
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                   host.Length > suffix.Length;
        }

        return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripPort(string host)
    {
        int colonIndex = host.LastIndexOf(':');
        return colonIndex > 0 && !host.Contains(']', StringComparison.Ordinal)
            ? host[..colonIndex]
            : host;
    }

    private static bool RouteMatches(string routePath, string requestPath)
    {
        if (routePath is "/**" or "/")
        {
            return true;
        }

        if (routePath.EndsWith("/**", StringComparison.Ordinal))
        {
            string prefix = routePath[..^3];
            return requestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(routePath, requestPath, StringComparison.OrdinalIgnoreCase);
    }

    private static int RouteSpecificity(string routePath) =>
        routePath.Replace("**", "", StringComparison.Ordinal).Length;

    private static int HostSpecificity(IReadOnlyList<string>? hosts) =>
        hosts?.Sum(host => host.Replace("*", "", StringComparison.Ordinal).Length) ?? 0;
}
