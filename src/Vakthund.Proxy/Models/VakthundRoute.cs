using Vakthund.Shared.Models;

namespace Vakthund.Proxy.Models;

public class VakthundRoute
{
    public string Path { get; init; } = "";
    public string Target { get; init; } = "";
    public AuthExpectation? Auth { get; init; }
}
