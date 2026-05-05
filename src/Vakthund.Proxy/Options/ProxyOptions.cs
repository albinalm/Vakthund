namespace Vakthund.Proxy.Options;

public class ProxyOptions
{
    public int MaxBodyBytes { get; set; } = 65536;
    public int MaxResponseBodyBytes { get; set; } = 65536;
    public int MaxQueuedEntries { get; set; } = 10_000;
}
