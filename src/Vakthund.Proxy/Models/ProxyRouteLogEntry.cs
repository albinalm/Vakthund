namespace Vakthund.Proxy.Models;

public sealed record ProxyRouteLogEntry(
    int Index,
    string DownstreamPath,
    string UpstreamBase,
    string UpstreamPath,
    string? RewritePath,
    string? Timeout,
    int Priority,
    IReadOnlyList<string> Hosts,
    IReadOnlyList<string> Ips);
