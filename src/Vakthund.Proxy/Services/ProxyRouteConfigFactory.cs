using Vakthund.Proxy.Models;
using System.Globalization;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Vakthund.Proxy.Services;

public static class ProxyRouteConfigFactory
{
    private const string CatchAllParameter = "{**catch-all}";

    public static List<RouteConfig> BuildRoutes(IReadOnlyList<VakthundRoute> routes) =>
        routes.Select((route, index) =>
        {
            RouteTimeout timeout = BuildTimeout(route);

            return new RouteConfig
            {
                RouteId = $"route-{index}",
                ClusterId = $"cluster-{index}",
                AuthorizationPolicy = RouteAuthPolicies.RequiresAuthorization(route)
                    ? RouteAuthPolicies.PolicyName(index)
                    : null,
                Match = new RouteMatch { Path = ToYarpPath(route.Path) },
                Transforms = BuildTransforms(route),
                Timeout = timeout.Value,
                TimeoutPolicy = timeout.Policy
            };
        }).ToList();

    public static List<ClusterConfig> BuildClusters(IReadOnlyList<VakthundRoute> routes) =>
        routes.Select((route, index) =>
        {
            RouteTimeout timeout = BuildTimeout(route);
            return new ClusterConfig
            {
                ClusterId = $"cluster-{index}",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["default"] = new() { Address = route.Target }
                },
                HttpRequest = BuildForwarderRequestConfig(timeout)
            };
        }).ToList();

    private static ForwarderRequestConfig? BuildForwarderRequestConfig(RouteTimeout timeout)
    {
        if (timeout.Value is { } ts)
        {
            return new ForwarderRequestConfig { ActivityTimeout = ts };
        }

        if (timeout.Policy is not null)
        {
            return new ForwarderRequestConfig { ActivityTimeout = Timeout.InfiniteTimeSpan };
        }

        return null;
    }

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

    private static RouteTimeout BuildTimeout(VakthundRoute route)
    {
        string timeout = route.Timeout.Trim();
        if (string.IsNullOrEmpty(timeout))
        {
            return new RouteTimeout(null, null);
        }

        if (string.Equals(timeout, "disable", StringComparison.OrdinalIgnoreCase))
        {
            return new RouteTimeout(null, "Disable");
        }

        return new RouteTimeout(ParseTimeout(timeout, route.Path), null);
    }

    private static TimeSpan ParseTimeout(string timeout, string routePath)
    {
        if (TryParseSuffixedTimeout(timeout, out TimeSpan suffixedTimeout))
        {
            return ValidateTimeout(suffixedTimeout, timeout, routePath);
        }

        if (double.TryParse(timeout, NumberStyles.Float, CultureInfo.InvariantCulture, out double milliseconds))
        {
            return ValidateTimeout(TimeSpan.FromMilliseconds(milliseconds), timeout, routePath);
        }

        if (TimeSpan.TryParse(timeout, CultureInfo.InvariantCulture, out TimeSpan timeSpan))
        {
            return ValidateTimeout(timeSpan, timeout, routePath);
        }

        throw new InvalidOperationException(
            $"Route '{routePath}' has invalid timeout '{timeout}'. Use milliseconds, hh:mm:ss, or a value ending in ms, s, m, or h.");
    }

    private static bool TryParseSuffixedTimeout(string timeout, out TimeSpan value)
    {
        string unit = "";
        string number = timeout;

        foreach (string candidate in new[] { "ms", "s", "m", "h" })
        {
            if (timeout.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
            {
                unit = candidate;
                number = timeout[..^candidate.Length];
                break;
            }
        }

        if (string.IsNullOrEmpty(unit) ||
            !double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
        {
            value = default;
            return false;
        }

        value = unit.ToLowerInvariant() switch
        {
            "ms" => TimeSpan.FromMilliseconds(amount),
            "s" => TimeSpan.FromSeconds(amount),
            "m" => TimeSpan.FromMinutes(amount),
            "h" => TimeSpan.FromHours(amount),
            _ => default
        };
        return true;
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout, string rawTimeout, string routePath)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Route '{routePath}' timeout '{rawTimeout}' must be greater than zero.");
        }

        return timeout;
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
