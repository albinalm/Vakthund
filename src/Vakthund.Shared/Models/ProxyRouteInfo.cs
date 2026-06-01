namespace Vakthund.Shared.Models;

public class ProxyRouteInfo
{
    public string Path { get; set; } = "";
    public string Target { get; set; } = "";
    public string To { get; set; } = "";
    public string Timeout { get; set; } = "";
    public int Priority { get; set; }
    public List<string> Hosts { get; set; } = [];
    public List<string> Ips { get; set; } = [];
    public AuthExpectation? Auth { get; set; }
}
