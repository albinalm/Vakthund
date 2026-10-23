using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Vakthund.UI.Extensions;

public static class UiForwardedHeadersServiceCollectionExtensions
{
    public static IServiceCollection AddUiForwardedHeaders(this IServiceCollection services, IConfiguration config)
    {
        IConfigurationSection section = config.GetSection("UI:ForwardedHeaders");
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;
            foreach (string proxy in section.GetSection("KnownProxies").Get<string[]>() ?? [])
            {
                if (!IPAddress.TryParse(proxy, out IPAddress? address))
                    throw new InvalidOperationException($"UI:ForwardedHeaders:KnownProxies contains invalid IP address '{proxy}'.");
                options.KnownProxies.Add(address);
            }
            foreach (string network in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
            {
                if (!System.Net.IPNetwork.TryParse(network, out System.Net.IPNetwork range))
                    throw new InvalidOperationException($"UI:ForwardedHeaders:KnownNetworks contains invalid CIDR '{network}'.");
                options.KnownIPNetworks.Add(range);
            }
            options.AllowedHosts = section.GetSection("AllowedHosts").Get<string[]>() ?? [];
        });
        return services;
    }
}
