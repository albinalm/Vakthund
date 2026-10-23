using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vakthund.Proxy.Extensions;
using Vakthund.Proxy.Services;

namespace Vakthund.Tests.Services;

public sealed class ForwardedHeadersTests
{
    [Fact]
    public async Task ProxyPipeline_RoutesByForwardedHostAndForwardsTheExternalRequestContext()
    {
        WebApplicationBuilder backendBuilder = WebApplication.CreateBuilder();
        backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using WebApplication backend = backendBuilder.Build();
        backend.MapGet("/", (HttpRequest request) => Results.Text(
            $"{request.Headers["X-Forwarded-Proto"]}|{request.Headers["X-Forwarded-Host"]}|{request.Headers["X-Forwarded-For"]}"));
        await backend.StartAsync();
        WebApplicationBuilder proxyBuilder = WebApplication.CreateBuilder();
        proxyBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        proxyBuilder.Services.AddVakthundProxy(proxyBuilder.Configuration,
        [
            new Vakthund.Proxy.Models.VakthundRoute
            {
                Path = "/**", Hosts = ["shop.example.test"], Target = backend.Urls.Single()
            }
        ]);
        await using WebApplication proxy = proxyBuilder.Build();
        proxy.UseProxyPipeline(proxyBuilder.Configuration);
        await proxy.StartAsync();
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, proxy.Urls.Single());
        request.Headers.Add("X-Forwarded-For", "100.64.0.1, 203.0.113.42");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "shop.example.test");
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        Assert.Equal("https|shop.example.test|203.0.113.42", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task TrustedIngress_PreservesExternalSchemeHostAndClientIp()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Proxy:ForwardedHeaders:KnownProxies:0"] = "10.42.0.10"
        });
        DefaultHttpContext context = CreateContext("10.42.0.10");
        await BuildPipeline(provider)(context);
        Assert.Equal("https", context.Request.Scheme);
        Assert.Equal("shop.example.test", context.Request.Host.Value);
        Assert.Equal("203.0.113.42", ClientIpResolver.Resolve(context));
    }
    [Fact]
    public async Task UnknownPeer_CannotSpoofSchemeHostOrClientIp()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Proxy:ForwardedHeaders:KnownProxies:0"] = "10.42.0.10"
        });
        DefaultHttpContext context = CreateContext("198.51.100.10");
        await BuildPipeline(provider)(context);
        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal("proxy.internal", context.Request.Host.Value);
        Assert.Equal("198.51.100.10", ClientIpResolver.Resolve(context));
    }
    [Fact]
    public async Task TrustedNetwork_ProcessesOnlyTheNearestHopByDefault()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Proxy:ForwardedHeaders:KnownNetworks:0"] = "10.42.0.0/16"
        });
        DefaultHttpContext context = CreateContext("10.42.1.10");
        context.Request.Headers["X-Forwarded-For"] = "100.64.0.1, 203.0.113.42";
        await BuildPipeline(provider)(context);
        Assert.Equal("203.0.113.42", ClientIpResolver.Resolve(context));
    }
    [Fact]
    public async Task ForwardedHost_MustMatchConfiguredAllowedHosts()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Proxy:ForwardedHeaders:KnownProxies:0"] = "10.42.0.10",
            ["Proxy:ForwardedHeaders:AllowedHosts:0"] = "api.example.test"
        });
        DefaultHttpContext context = CreateContext("10.42.0.10");
        await BuildPipeline(provider)(context);
        Assert.Equal("proxy.internal", context.Request.Host.Value);
    }
    [Theory]
    [InlineData("KnownProxies:0", "not-an-ip")]
    [InlineData("KnownNetworks:0", "not-a-network")]
    [InlineData("ForwardLimit", "0")]
    [InlineData("ForwardLimit", "-1")]
    public void InvalidTrustConfiguration_FailsInsteadOfTrustingAllPeers(string key, string value)
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"Proxy:ForwardedHeaders:{key}"] = value
        });
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
    }
    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProxyForwardedHeaders(configuration);
        return services.BuildServiceProvider();
    }
    private static RequestDelegate BuildPipeline(ServiceProvider provider) =>
        new ForwardedHeadersMiddleware(_ => Task.CompletedTask, provider.GetRequiredService<ILoggerFactory>(),
            provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>()).Invoke;
    private static DefaultHttpContext CreateContext(string peer)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("proxy.internal");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.42";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "shop.example.test";
        return context;
    }
}
