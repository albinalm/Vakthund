namespace Vakthund.Shared.Models;

public class ProxyRouteInfo
{
    public string Path { get; set; } = "";
    public string Target { get; set; } = "";
    public string To { get; set; } = "";
    public AuthExpectation? Auth { get; set; }
}
