namespace Vakthund.Proxy.Models;

public sealed record ProxyRouteLogEntry(
    int Index,
    string DownstreamPath,
    string UpstreamBase,
    string UpstreamPath,
    string? RewritePath,
    string? Timeout,
    IReadOnlyList<string> Ips);
