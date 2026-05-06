using Vakthund.Proxy.Models;
using Yarp.ReverseProxy.Configuration;

namespace Vakthund.Proxy.Services;

public static class ProxyRouteConfigFactory
{
    public static List<RouteConfig> BuildRoutes(IReadOnlyList<VakthundRoute> routes) =>
        routes.Select((route, index) => new RouteConfig
        {
            RouteId = $"route-{index}",
            ClusterId = $"cluster-{index}",
            AuthorizationPolicy = RouteAuthPolicies.EnforcesAuth(route)
                ? RouteAuthPolicies.PolicyName(index)
                : null,
            Match = new RouteMatch { Path = ToYarpPath(route.Path) }
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
}
