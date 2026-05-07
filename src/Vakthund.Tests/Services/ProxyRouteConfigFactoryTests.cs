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
}
