namespace Vakthund.Proxy.Models;

internal sealed class IpPolicy
{
    public string? Name { get; set; }
    public List<string> Entries { get; set; } = [];
}
