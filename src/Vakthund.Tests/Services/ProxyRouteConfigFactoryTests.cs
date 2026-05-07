using Vakthund.Proxy.Models;
using Vakthund.Proxy.Services;
using Vakthund.Shared.Models;
using Yarp.ReverseProxy.Configuration;

namespace Vakthund.Tests.Services;

public class ProxyRouteConfigFactoryTests
{
    [Fact]
    public void BuildRoutes_SetsAuthorizationPolicy_WhenRouteAuthIsEnforced()
    {
        List<RouteConfig> routes = ProxyRouteConfigFactory.BuildRoutes(
        [
            new VakthundRoute
            {
                Path = "/api/**",
                Target = "https://backend.example",
                Auth = new AuthExpectation
                {
                    Enforced = true,
                    Issuer = "https://issuer.example"
                }
            }
        ]);

        RouteConfig route = Assert.Single(routes);
        Assert.Equal(RouteAuthPolicies.PolicyName(0), route.AuthorizationPolicy);
        Assert.Equal("/api/{**catch-all}", route.Match.Path);
    }

    [Fact]
    public void BuildRoutes_DoesNotSetAuthorizationPolicy_WhenRouteAuthIsContextOnly()
    {
        List<RouteConfig> routes = ProxyRouteConfigFactory.BuildRoutes(
        [
            new VakthundRoute
            {
                Path = "/api/**",
                Target = "https://backend.example",
                Auth = new AuthExpectation
                {
                    Audience = "orders-api"
                }
            }
        ]);

        RouteConfig route = Assert.Single(routes);
        Assert.Null(route.AuthorizationPolicy);
    }

    [Fact]
    public void BuildRoutes_SetsAuthorizationPolicy_WhenRouteHasIpWhitelist()
    {
        List<RouteConfig> routes = ProxyRouteConfigFactory.BuildRoutes(
        [
            new VakthundRoute
            {
                Path = "/api/**",
                Target = "https://backend.example",
                Ips = ["203.0.113.*"]
            }
        ]);

        RouteConfig route = Assert.Single(routes);
        Assert.Equal(RouteAuthPolicies.PolicyName(0), route.AuthorizationPolicy);
    }

    [Fact]
    public void BuildRoutes_AddsPathPatternTransform_WhenCatchAllRouteHasToPath()
    {
        List<RouteConfig> routes = ProxyRouteConfigFactory.BuildRoutes(
        [
            new VakthundRoute
            {
                Path = "/foobar/**",
                Target = "http://localhost:5001",
                To = "/api/foobar"
            }
        ]);

        RouteConfig route = Assert.Single(routes);
        IReadOnlyDictionary<string, string> transform = Assert.Single(route.Transforms!);
        Assert.Equal("/api/foobar/{**catch-all}", transform["PathPattern"]);
    }

    [Fact]
    public void BuildRoutes_AddsPathSetTransform_WhenExactRouteHasToPath()
    {
        List<RouteConfig> routes = ProxyRouteConfigFactory.BuildRoutes(
        [
            new VakthundRoute
            {
                Path = "/health",
                Target = "http://localhost:5001",
                To = "/internal/health"
            }
        ]);

        RouteConfig route = Assert.Single(routes);
        IReadOnlyDictionary<string, string> transform = Assert.Single(route.Transforms!);
        Assert.Equal("/internal/health", transform["PathSet"]);
    }

    [Theory]
    [InlineData("3600000")]
    [InlineData("3600s")]
    [InlineData("60m")]
    [InlineData("1h")]
    [InlineData("01:00:00")]
    public void BuildRoutes_SetsRouteTimeout(string timeout)
    {
        List<RouteConfig> routes = ProxyRouteConfigFactory.BuildRoutes(
        [
            new VakthundRoute
            {
                Path = "/api/generate",
                Target = "http://localhost:5000",
                Timeout = timeout
            }
        ]);

        RouteConfig route = Assert.Single(routes);
        Assert.Equal(TimeSpan.FromHours(1), route.Timeout);
        Assert.Null(route.TimeoutPolicy);
    }

    [Theory]
    [InlineData("3600000")]
    [InlineData("3600s")]
    [InlineData("60m")]
    [InlineData("1h")]
    [InlineData("01:00:00")]
    public void BuildClusters_SetsForwarderActivityTimeout(string timeout)
    {
        List<ClusterConfig> clusters = ProxyRouteConfigFactory.BuildClusters(
        [
            new VakthundRoute
            {
                Path = "/api/generate",
                Target = "http://localhost:5000",
                Timeout = timeout
            }
        ]);

        ClusterConfig cluster = Assert.Single(clusters);
        Assert.Equal(TimeSpan.FromHours(1), cluster.HttpRequest?.ActivityTimeout);
    }

    [Fact]
    public void BuildRoutes_DisablesTimeout_WhenRouteTimeoutIsDisable()
    {
        List<RouteConfig> routes = ProxyRouteConfigFactory.BuildRoutes(
        [
            new VakthundRoute
            {
                Path = "/api/generate",
                Target = "http://localhost:5000",
                Timeout = "disable"
            }
        ]);

        RouteConfig route = Assert.Single(routes);
        Assert.Null(route.Timeout);
        Assert.Equal("Disable", route.TimeoutPolicy);
    }

    [Fact]
    public void BuildClusters_DisablesForwarderActivityTimeout_WhenRouteTimeoutIsDisable()
    {
        List<ClusterConfig> clusters = ProxyRouteConfigFactory.BuildClusters(
        [
            new VakthundRoute
            {
                Path = "/api/generate",
                Target = "http://localhost:5000",
                Timeout = "disable"
            }
        ]);

        ClusterConfig cluster = Assert.Single(clusters);
        Assert.Equal(Timeout.InfiniteTimeSpan, cluster.HttpRequest?.ActivityTimeout);
    }

    [Fact]
    public void BuildClusters_UsesYarpDefaultForwarderActivityTimeout_WhenRouteTimeoutIsEmpty()
    {
        List<ClusterConfig> clusters = ProxyRouteConfigFactory.BuildClusters(
        [
            new VakthundRoute
            {
                Path = "/api/generate",
                Target = "http://localhost:5000"
            }
        ]);

        ClusterConfig cluster = Assert.Single(clusters);
        Assert.Null(cluster.HttpRequest);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1s")]
    [InlineData("later")]
    public void BuildRoutes_Throws_WhenRouteTimeoutIsInvalid(string timeout)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ProxyRouteConfigFactory.BuildRoutes(
            [
                new VakthundRoute
                {
                    Path = "/api/generate",
                    Target = "http://localhost:5000",
                    Timeout = timeout
                }
            ]));

        Assert.Contains("timeout", exception.Message);
        Assert.Contains("/api/generate", exception.Message);
    }
}
