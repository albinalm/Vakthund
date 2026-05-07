namespace Vakthund.Proxy.Models;

internal sealed class RouteDefinition
{
    public string Path { get; init; } = "";
    public string Target { get; init; } = "";
    public string To { get; init; } = "";
    public string Timeout { get; init; } = "";
    public RouteIpDefinition? Ips { get; init; }
    public RouteAuthDefinition? Auth { get; init; }
}
