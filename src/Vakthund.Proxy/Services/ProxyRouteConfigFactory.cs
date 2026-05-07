using Vakthund.Proxy.Models;
using Yarp.ReverseProxy.Configuration;

namespace Vakthund.Proxy.Services;

public static class ProxyRouteConfigFactory
{
    private const string CatchAllParameter = "{**catch-all}";

    public static List<RouteConfig> BuildRoutes(IReadOnlyList<VakthundRoute> routes) =>
        routes.Select((route, index) => new RouteConfig
        {
            RouteId = $"route-{index}",
            ClusterId = $"cluster-{index}",
            AuthorizationPolicy = RouteAuthPolicies.EnforcesAuth(route)
                ? RouteAuthPolicies.PolicyName(index)
                : null,
            Match = new RouteMatch { Path = ToYarpPath(route.Path) },
            Transforms = BuildTransforms(route)
        }).ToList();

    public static List<ClusterConfig> BuildClusters(IReadOnlyList<VakthundRoute> routes) =>
        routes.Select((route, index) => new ClusterConfig
        {
            ClusterId = $"cluster-{index}",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["default"] = new() { Address = route.Target }
            }
        }).ToList();

    private static string ToYarpPath(string path)
    {
        if (path is "/**" or "/")
        {
            return "{**catch-all}";
        }

        if (path.EndsWith("/**", StringComparison.Ordinal))
        {
            return path[..^3] + "/{**catch-all}";
        }

        return path;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>>? BuildTransforms(VakthundRoute route)
    {
        string to = NormalizePath(route.To);
        if (string.IsNullOrEmpty(to))
        {
            return null;
        }

        string transform = IsCatchAllPath(route.Path) ? "PathPattern" : "PathSet";
        string value = IsCatchAllPath(route.Path) ? ToCatchAllPattern(to) : to;
        return [new Dictionary<string, string> { [transform] = value }];
    }

    private static bool IsCatchAllPath(string path) =>
        path is "/**" or "/" ||
        path.EndsWith("/**", StringComparison.Ordinal) ||
        path.Contains("{**", StringComparison.Ordinal);

    private static string ToCatchAllPattern(string path)
    {
        if (path.Contains("{**", StringComparison.Ordinal))
        {
            return path;
        }

        return path == "/"
            ? "/" + CatchAllParameter
            : path + "/" + CatchAllParameter;
    }

    private static string NormalizePath(string path)
    {
        path = path.Trim();
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        if (path is "/**")
        {
            return "/";
        }

        if (path.EndsWith("/**", StringComparison.Ordinal))
        {
            path = path[..^3];
        }

        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            path = "/" + path;
        }

        return path.Length > 1 ? path.TrimEnd('/') : path;
    }
}
