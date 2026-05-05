using System.Net.Http.Json;
using Vakthund.Shared.Models;

namespace Vakthund.UI.Services;

public class ProxyConfigService(IHttpClientFactory httpClientFactory)
{
    public async Task<ProxyConfigLoadResult> GetAsync(CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("proxy-management");

        try
        {
            ProxyConfig? config = await client.GetFromJsonAsync<ProxyConfig>("/config", ct);
            return config is null
                ? ProxyConfigLoadResult.Failed("The proxy returned an empty configuration response.")
                : ProxyConfigLoadResult.Success(config);
        }
        catch (HttpRequestException ex)
        {
            return ProxyConfigLoadResult.Failed(
                $"The proxy management endpoint did not respond at {FormatEndpoint(client.BaseAddress)}.",
                ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return ProxyConfigLoadResult.Failed(
                $"Timed out while loading proxy configuration from {FormatEndpoint(client.BaseAddress)}.",
                ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ProxyConfigLoadResult.Failed(
                $"The proxy configuration response could not be loaded from {FormatEndpoint(client.BaseAddress)}.",
                ex.Message);
        }
    }

    private static string FormatEndpoint(Uri? baseAddress) => baseAddress?.ToString().TrimEnd('/') ?? "the configured proxy URL";
}

public sealed record ProxyConfigLoadResult(ProxyConfig? Config, string? ErrorMessage, string? ErrorDetail)
{
    public static ProxyConfigLoadResult Success(ProxyConfig config) => new(config, null, null);
    public static ProxyConfigLoadResult Failed(string message, string? detail = null) => new(null, message, detail);
}
