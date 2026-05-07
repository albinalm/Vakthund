namespace Vakthund.Proxy.Models;

internal sealed class RoutesFileDefinition
{
    public List<NamedAuthExpectation> Auths { get; set; } = [];
    public List<IpPolicy> IpPolicies { get; set; } = [];
    public List<RouteDefinition> Routes { get; set; } = [];
}
