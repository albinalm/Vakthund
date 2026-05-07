using Vakthund.Shared.Models;

namespace Vakthund.UI.Models;

public sealed record ProxyConfigLoadResult(ProxyConfig? Config, string? ErrorMessage, string? ErrorDetail)
{
    public static ProxyConfigLoadResult Success(ProxyConfig config) => new(config, null, null);
    public static ProxyConfigLoadResult Failed(string message, string? detail = null) => new(null, message, detail);
}
