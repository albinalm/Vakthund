using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Vakthund.Proxy.Extensions;

public static class ForwardedHeadersServiceCollectionExtensions
{
    public static IServiceCollection AddProxyForwardedHeaders(this IServiceCollection services, IConfiguration config)
    {
        IConfigurationSection section = config.GetSection("Proxy:ForwardedHeaders");
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                options.ForwardLimit = section.GetValue("ForwardLimit", 1);
                if (options.ForwardLimit is null or < 1)
                    throw new InvalidOperationException("Proxy:ForwardedHeaders:ForwardLimit must be a positive number.");
                foreach (string proxy in section.GetSection("KnownProxies").Get<string[]>() ?? [])
                {
                    if (!IPAddress.TryParse(proxy, out IPAddress? address))
                        throw new InvalidOperationException($"Proxy:ForwardedHeaders:KnownProxies contains invalid IP address '{proxy}'.");
                    options.KnownProxies.Add(address);
                }
                foreach (string network in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
                {
                    if (!System.Net.IPNetwork.TryParse(network, out System.Net.IPNetwork range))
                        throw new InvalidOperationException($"Proxy:ForwardedHeaders:KnownNetworks contains invalid CIDR '{network}'.");
                    options.KnownIPNetworks.Add(range);
                }
                options.AllowedHosts = section.GetSection("AllowedHosts").Get<string[]>() ?? [];
            })
            .ValidateOnStart();
        return services;
    }
}
