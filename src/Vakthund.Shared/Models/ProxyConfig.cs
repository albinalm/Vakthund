namespace Vakthund.Shared.Models;

public class ProxyConfig
{
    public IReadOnlyList<ProxyRouteInfo> Routes { get; set; } = [];
    public int MaxBodyBytes { get; set; }
    public int MaxResponseBodyBytes { get; set; }
    public int MaxQueuedEntries { get; set; }
}
