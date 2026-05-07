using Vakthund.Proxy.Models;

namespace Vakthund.Proxy.Services;

public static class ProxyRouteStartupLog
{
    public static IReadOnlyList<ProxyRouteLogEntry> BuildEntries(IReadOnlyList<VakthundRoute> routes) =>
        routes.Select((route, index) => new ProxyRouteLogEntry(
            Index: index,
            DownstreamPath: route.Path,
            UpstreamBase: NormalizeBase(route.Target),
            UpstreamPath: ResolveUpstreamPath(route),
            RewritePath: string.IsNullOrWhiteSpace(route.To) ? null : NormalizePath(route.To),
            Timeout: string.IsNullOrWhiteSpace(route.Timeout) ? null : route.Timeout.Trim(),
            Ips: route.Ips is { Count: > 0 } ? [.. route.Ips] : []))
        .ToList();

    public static void Log(ILogger logger, IReadOnlyList<VakthundRoute> routes)
    {
        logger.LogInformation("Configured {RouteCount} proxy routes", routes.Count);

        foreach (ProxyRouteLogEntry entry in BuildEntries(routes))
        {
            logger.LogInformation(
                "Proxy route configured {RouteIndex}: {DownstreamPath} -> {UpstreamBase}{UpstreamPath}; rewrite {PathRewrite}; timeout {RouteTimeout}; ips {AllowedIps}",
                entry.Index,
                entry.DownstreamPath,
                entry.UpstreamBase,
                entry.UpstreamPath,
                entry.RewritePath ?? "none",
                entry.Timeout ?? "default",
                entry.Ips.Count > 0 ? string.Join(", ", entry.Ips) : "any");
        }
    }

    private static string ResolveUpstreamPath(VakthundRoute route)
    {
        string rewritePath = NormalizePath(route.To);
        if (string.IsNullOrEmpty(rewritePath))
        {
            return route.Path;
        }

        if (!IsCatchAllPath(route.Path))
        {
            return rewritePath;
        }

        return rewritePath == "/"
            ? "/**"
            : rewritePath + "/**";
    }

    private static bool IsCatchAllPath(string path) =>
        path is "/**" or "/" ||
        path.EndsWith("/**", StringComparison.Ordinal) ||
        path.Contains("{**", StringComparison.Ordinal);

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

    private static string NormalizeBase(string target) => target.Trim().TrimEnd('/');
}
