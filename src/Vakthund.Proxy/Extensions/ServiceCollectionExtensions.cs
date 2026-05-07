using Vakthund.Proxy.Middlewares;
using Vakthund.Proxy.Models;
using Vakthund.Proxy.Options;
using Vakthund.Proxy.Services;
using Vakthund.Proxy.Workers;
using Yarp.ReverseProxy.Transforms;

namespace Vakthund.Proxy.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVakthundProxy(this IServiceCollection services, IConfiguration config, IReadOnlyList<VakthundRoute> routes)
    {
        services.Configure<ProxyOptions>(config.GetSection("Proxy"));
        services.AddSingleton<IReadOnlyList<VakthundRoute>>(routes);
        services.AddSignalR();
        services.AddSingleton<AuditQueue>();
        services.AddSingleton<ProxyActivityFeed>();
        services.AddSingleton<RequestInterceptor>();
        services.AddHostedService<AuditBroadcastWorker>();
        services.AddRequestTimeouts();
        services.AddRouteAuthentication(routes);
        int fallbackConnectTimeoutSeconds = config.GetValue("Proxy:ConnectTimeoutSeconds", 30);
        TimeSpan fallbackConnectTimeout = fallbackConnectTimeoutSeconds <= 0
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromSeconds(fallbackConnectTimeoutSeconds);

        services.AddReverseProxy()
            .ConfigureHttpClient((context, handler) =>
            {
                handler.ConnectTimeout = fallbackConnectTimeout;
            })
            .LoadFromMemory(
                routes: ProxyRouteConfigFactory.BuildRoutes(routes),
                clusters: ProxyRouteConfigFactory.BuildClusters(routes))
            .AddTransforms(context =>
            {
                context.AddResponseTransform(transformContext =>
                {
                    int? statusCode = transformContext.ProxyResponse is null
                        ? null
                        : (int)transformContext.ProxyResponse.StatusCode;

                    RequestInterceptor.CaptureTargetResponse(transformContext.HttpContext, statusCode);
                    return default;
                });
            });

        return services;
    }
}
