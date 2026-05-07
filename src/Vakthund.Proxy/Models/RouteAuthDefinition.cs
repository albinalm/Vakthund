using Vakthund.Shared.Models;

namespace Vakthund.Proxy.Models;

internal sealed class RouteAuthDefinition
{
    public string? Reference { get; init; }
    public AuthExpectation? Inline { get; init; }
}
