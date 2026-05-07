using Vakthund.Proxy.Models;
using Vakthund.Proxy.Services;

namespace Vakthund.Tests.Services;

public class ProxyRouteStartupLogTests
{
    [Fact]
    public void BuildEntries_UsesOriginalPath_WhenRouteHasNoRewrite()
    {
        ProxyRouteLogEntry entry = Assert.Single(ProxyRouteStartupLog.BuildEntries(
        [
            new VakthundRoute
            {
                Path = "/logs/**",
                Target = "http://localhost:5001"
            }
        ]));

        Assert.Equal(0, entry.Index);
        Assert.Equal("/logs/**", entry.DownstreamPath);
        Assert.Equal("http://localhost:5001", entry.UpstreamBase);
        Assert.Equal("/logs/**", entry.UpstreamPath);
        Assert.Null(entry.RewritePath);
        Assert.Null(entry.Timeout);
        Assert.Empty(entry.Ips);
    }

    [Fact]
    public void BuildEntries_AppendsCatchAll_WhenCatchAllRouteHasRewrite()
    {
        ProxyRouteLogEntry entry = Assert.Single(ProxyRouteStartupLog.BuildEntries(
        [
            new VakthundRoute
            {
                Path = "/logs/**",
                Target = "http://localhost:5001",
                To = "/api/logs"
            }
        ]));

        Assert.Equal("/api/logs/**", entry.UpstreamPath);
        Assert.Equal("/api/logs", entry.RewritePath);
    }

    [Fact]
    public void BuildEntries_UsesRewritePath_WhenExactRouteHasRewrite()
    {
        ProxyRouteLogEntry entry = Assert.Single(ProxyRouteStartupLog.BuildEntries(
        [
            new VakthundRoute
            {
                Path = "/health",
                Target = "http://localhost:5001",
                To = "internal/health/"
            }
        ]));

        Assert.Equal("/internal/health", entry.UpstreamPath);
        Assert.Equal("/internal/health", entry.RewritePath);
    }

    [Fact]
    public void BuildEntries_IncludesTimeout_WhenRouteHasTimeout()
    {
        ProxyRouteLogEntry entry = Assert.Single(ProxyRouteStartupLog.BuildEntries(
        [
            new VakthundRoute
            {
                Path = "/api/generate",
                Target = "http://localhost:5000",
                Timeout = "1h"
            }
        ]));

        Assert.Equal("1h", entry.Timeout);
    }

    [Fact]
    public void BuildEntries_IncludesIps_WhenRouteHasIpWhitelist()
    {
        ProxyRouteLogEntry entry = Assert.Single(ProxyRouteStartupLog.BuildEntries(
        [
            new VakthundRoute
            {
                Path = "/api/**",
                Target = "http://localhost:5000",
                Ips = ["203.0.113.*"]
            }
        ]));

        Assert.Equal(["203.0.113.*"], entry.Ips);
    }
}
