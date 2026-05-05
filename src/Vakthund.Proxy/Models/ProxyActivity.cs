namespace Vakthund.Proxy.Models;

public sealed record ProxyActivity
{
    public required string Phase { get; init; }
    public required string Method { get; init; }
    public required string Uri { get; init; }
    public int? StatusCode { get; init; }
}
