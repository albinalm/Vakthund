using Vakthund.Proxy.Middlewares;
using Vakthund.Proxy.Models;
using Vakthund.Proxy.Options;
using Vakthund.Proxy.Services;
using Vakthund.Proxy.Workers;
using Yarp.ReverseProxy.Configuration;

namespace Vakthund.Proxy.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVakthundProxy(this IServiceCollection services, IConfiguration config, IReadOnlyList<VakthundRoute> routes)
    {
        services.Configure<VakthundOptions>(config.GetSection("Vakthund"));
        services.AddSingleton<IReadOnlyList<VakthundRoute>>(routes);
        services.AddSignalR();
        services.AddSingleton<AuditQueue>();
        services.AddSingleton<ProxyActivityFeed>();
        services.AddSingleton<RequestInterceptor>();
        services.AddHostedService<AuditBroadcastWorker>();
        services.AddReverseProxy().LoadFromMemory(
            routes: routes.Select((r, i) => new RouteConfig
            {
                RouteId = $"route-{i}",
                ClusterId = $"cluster-{i}",
                Match = new RouteMatch { Path = ToYarpPath(r.Path) }
            }).ToList(),
            clusters: routes.Select((r, i) => new ClusterConfig
            {
                ClusterId = $"cluster-{i}",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["default"] = new() { Address = r.Target }
                }
            }).ToList());

        return services;
    }

    private static string ToYarpPath(string path)
    {
        if (path is "/**" or "/") return "{**catch-all}";
        if (path.EndsWith("/**")) return path[..^3] + "/{**catch-all}";
        return path;
    }
}
