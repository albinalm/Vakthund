using Microsoft.Extensions.Options;
using Vakthund.Proxy.Middlewares;
using Vakthund.Proxy.Models;
using Vakthund.Proxy.Options;
using Vakthund.Proxy.Services;
using Vakthund.Shared.Models;
using Vakthund.UI.Hubs;

namespace Vakthund.Proxy.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication ConfigureUrls(this WebApplication app, IConfiguration config)
    {
        int managementPort = config.GetValue("Management:Port", 8081);
        string managementUrl = config.GetValue("Management:Url", $"http://*:{managementPort}");
        string[] proxyUrls = config.GetSection("Proxy:Urls").Get<string[]>() ?? ["http://*:8080"];

        app.Urls.Clear();
        foreach (string url in proxyUrls)
        {
            app.Urls.Add(url);
        }

        app.Urls.Add(managementUrl);

        return app;
    }

    public static WebApplication MapManagementEndpoints(this WebApplication app, IConfiguration config)
    {
        int managementPort = config.GetValue("Management:Port", 8081);
        var managementHost = $"*:{managementPort}";

        app.MapGet("/", async (IWebHostEnvironment env) =>
        {
            string html = await File.ReadAllTextAsync(Path.Combine(env.WebRootPath, "index.html"));
            return Results.Content(html, "text/html; charset=utf-8");
        }).RequireHost(managementHost);

        app.MapGet("/logo.svg", (IWebHostEnvironment env) =>
            Results.File(Path.Combine(env.WebRootPath, "logo.svg"), "image/svg+xml")
        ).RequireHost(managementHost);

        app.MapGet("/favicon.ico", (IWebHostEnvironment env) =>
            Results.File(Path.Combine(env.WebRootPath, "favicon.ico"), "image/x-icon")
        ).RequireHost(managementHost);

        app.MapGet("/activity", (ProxyActivityFeed feed, HttpContext ctx) =>
            feed.WriteEventStreamAsync(ctx.Response, ctx.RequestAborted)
        ).RequireHost(managementHost);

        app.MapGet("/config", (IReadOnlyList<VakthundRoute> routes, IOptions<ProxyOptions> options) =>
            Results.Ok(new ProxyConfig
            {
                Routes = routes
                    .Select(r => new ProxyRouteInfo
                    {
                        Path = r.Path,
                        Target = r.Target,
                        To = r.To,
                        Timeout = r.Timeout,
                        Priority = r.Priority,
                        Hosts = [.. r.Hosts],
                        Ips = [.. r.Ips],
                        Auth = r.Auth
                    })
                    .ToList(),
                MaxBodyBytes = options.Value.MaxBodyBytes,
                MaxResponseBodyBytes = options.Value.MaxResponseBodyBytes,
                MaxQueuedEntries = options.Value.MaxQueuedEntries
            })
        ).RequireHost(managementHost);

        app.MapHub<AuditHub>("/connect/audit").RequireHost(managementHost);

        return app;
    }

    public static WebApplication LogProxyRoutes(this WebApplication app)
    {
        var routes = app.Services.GetRequiredService<IReadOnlyList<VakthundRoute>>();
        ILogger logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Vakthund.Proxy.Routes");

        ProxyRouteStartupLog.Log(logger, routes);
        return app;
    }

    public static WebApplication UseProxyPipeline(this WebApplication app, IConfiguration config)
    {
        int managementPort = config.GetValue("Management:Port", 8081);
        string[] proxyHostPatterns = config.GetSection("Proxy:HostPatterns").Get<string[]>() ?? ["*:8080"];

        app.UseWhen(
            ctx => ctx.Connection.LocalPort != managementPort,
            branch => branch.UseMiddleware<RequestInterceptor>());

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRequestTimeouts();

        app.MapReverseProxy()
            .ConfigureEndpoints((endpointBuilder, route) =>
            {
                if (route.Match.Hosts is null || route.Match.Hosts.Count == 0)
                {
                    endpointBuilder.RequireHost(proxyHostPatterns);
                }
            });

        return app;
    }
}
