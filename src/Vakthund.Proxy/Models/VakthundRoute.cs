using Vakthund.Shared.Models;

namespace Vakthund.Proxy.Models;

public class VakthundRoute
{
    public string Path { get; init; } = "";
    public string Target { get; init; } = "";
    public string To { get; init; } = "";
    public string Timeout { get; init; } = "";
    public List<string> Hosts { get; init; } = [];
    public List<string> Ips { get; init; } = [];
    public AuthExpectation? Auth { get; init; }
}
