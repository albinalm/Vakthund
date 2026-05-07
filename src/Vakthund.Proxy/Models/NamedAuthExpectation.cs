using Vakthund.Shared.Models;

namespace Vakthund.Proxy.Models;

internal sealed class NamedAuthExpectation : AuthExpectation
{
    public string? Name { get; set; }
}
