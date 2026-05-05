using Vakthund.Shared.Models;

namespace Vakthund.UI.Services;

public class ProxyRouteMatcher
{
    public ProxyRouteInfo? FindMatchingRoute(IReadOnlyList<ProxyRouteInfo>? routes, string path)
    {
        return routes?
            .Where(route => RouteMatches(route.Path, path))
            .OrderByDescending(route => RouteSpecificity(route.Path))
            .FirstOrDefault();
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
}
