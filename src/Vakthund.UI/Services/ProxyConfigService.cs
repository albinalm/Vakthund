using System.Net.Http.Json;
using Vakthund.Shared.Models;

namespace Vakthund.UI.Services;

public class ProxyConfigService(IHttpClientFactory httpClientFactory)
{
    public async Task<ProxyConfig?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("proxy-management");
            return await client.GetFromJsonAsync<ProxyConfig>("/config", ct);
        }
        catch
        {
            return null;
        }
    }
}
