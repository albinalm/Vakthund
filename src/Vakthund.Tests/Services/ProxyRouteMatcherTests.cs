using Vakthund.Shared.Models;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Services;

public class ProxyRouteMatcherTests
{
    [Fact]
    public void FindMatchingRoute_ReturnsMostSpecificMatchingRoute()
    {
        var matcher = new ProxyRouteMatcher();
        ProxyRouteInfo apiRoute = new() { Path = "/api/**", Target = "https://api.example" };
        ProxyRouteInfo ordersRoute = new() { Path = "/api/orders/**", Target = "https://orders.example" };
        ProxyRouteInfo fallbackRoute = new() { Path = "/**", Target = "https://fallback.example" };

        ProxyRouteInfo? route = matcher.FindMatchingRoute(
            [fallbackRoute, apiRoute, ordersRoute],
            "/api/orders/123");

        Assert.Same(ordersRoute, route);
    }

    [Fact]
    public void FindMatchingRoute_ReturnsFallbackRoute_WhenNoSpecificRouteMatches()
    {
        var matcher = new ProxyRouteMatcher();
        ProxyRouteInfo fallbackRoute = new() { Path = "/**", Target = "https://fallback.example" };

        ProxyRouteInfo? route = matcher.FindMatchingRoute(
            [new ProxyRouteInfo { Path = "/api/**", Target = "https://api.example" }, fallbackRoute],
            "/admin");

        Assert.Same(fallbackRoute, route);
    }
}
