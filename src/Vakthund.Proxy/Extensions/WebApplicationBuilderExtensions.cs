using Microsoft.Extensions.Hosting;
using Vakthund.Proxy.Models;
using Vakthund.Proxy.Services;

namespace Vakthund.Proxy.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder ApplyEnvironmentOverrides(this WebApplicationBuilder builder)
    {
        if (Environment.GetEnvironmentVariable("TARGET") is { Length: > 0 } target)
        {
            builder.Configuration["Proxy:TargetUrl"] = target;
        }

        if (Environment.GetEnvironmentVariable("MAX_BODY_BYTES") is { Length: > 0 } maxBody)
        {
            builder.Configuration["Vakthund:MaxBodyBytes"] = maxBody;
        }

        if (Environment.GetEnvironmentVariable("MAX_RESPONSE_BODY_BYTES") is { Length: > 0 } maxResponseBody)
        {
            builder.Configuration["Vakthund:MaxResponseBodyBytes"] = maxResponseBody;
        }

        if (Environment.GetEnvironmentVariable("MAX_QUEUED_ENTRIES") is { Length: > 0 } maxQueue)
        {
            builder.Configuration["Vakthund:MaxQueuedEntries"] = maxQueue;
        }

        if (Environment.GetEnvironmentVariable("ROUTES_FILE") is { Length: > 0 } routesFile)
        {
            builder.Configuration["Vakthund:RoutesFile"] = routesFile;
        }

        return builder;
    }

    public static WebApplicationBuilder AddVakthundProxy(this WebApplicationBuilder builder)
    {
        string? routesFilePath = RoutesFileResolver.Resolve(
            builder.Configuration["Vakthund:RoutesFile"],
            builder.Environment.ContentRootPath,
            builder.Environment.IsDevelopment());
        List<VakthundRoute>? routes = RoutesLoader.TryLoad(routesFilePath);

        if (routes is null)
        {
            string targetUrl = builder.Configuration["Proxy:TargetUrl"]?.Trim() ?? "";
            if (string.IsNullOrEmpty(targetUrl))
            {
                throw new InvalidOperationException("No routes configured. Set the TARGET env var, provide a routes.yaml, or configure Vakthund:RoutesFile in appsettings.");
            }

            routes = [new VakthundRoute { Path = "/**", Target = targetUrl }];
        }

        builder.Services.AddVakthundProxy(builder.Configuration, routes);
        return builder;
    }
}
