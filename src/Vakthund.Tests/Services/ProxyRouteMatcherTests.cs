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

    [Fact]
    public void FindMatchingRoute_PrefersHostSpecificRoute_WhenHostMatches()
    {
        var matcher = new ProxyRouteMatcher();
        ProxyRouteInfo hostRoute = new() { Path = "/api/**", Target = "https://api.example", Hosts = ["api.example.test"] };
        ProxyRouteInfo fallbackRoute = new() { Path = "/api/**", Target = "https://fallback.example" };

        ProxyRouteInfo? route = matcher.FindMatchingRoute([fallbackRoute, hostRoute], "/api/orders", "api.example.test:8080");

        Assert.Same(hostRoute, route);
    }

    [Fact]
    public void FindMatchingRoute_PrefersHigherPriorityRoute()
    {
        var matcher = new ProxyRouteMatcher();
        ProxyRouteInfo specificRoute = new() { Path = "/api/orders/**", Target = "https://orders.example" };
        ProxyRouteInfo priorityRoute = new() { Path = "/api/**", Target = "https://priority.example", Priority = 10 };

        ProxyRouteInfo? route = matcher.FindMatchingRoute([specificRoute, priorityRoute], "/api/orders/123");

        Assert.Same(priorityRoute, route);
    }

    [Fact]
    public void FindMatchingRoute_UsesFallbackRoute_WhenHostDoesNotMatch()
    {
        var matcher = new ProxyRouteMatcher();
        ProxyRouteInfo hostRoute = new() { Path = "/api/**", Target = "https://api.example", Hosts = ["api.example.test"] };
        ProxyRouteInfo fallbackRoute = new() { Path = "/api/**", Target = "https://fallback.example" };

        ProxyRouteInfo? route = matcher.FindMatchingRoute([hostRoute, fallbackRoute], "/api/orders", "web.example.test");

        Assert.Same(fallbackRoute, route);
    }

    [Fact]
    public void FindMatchingRoute_MatchesWildcardHost()
    {
        var matcher = new ProxyRouteMatcher();
        ProxyRouteInfo hostRoute = new() { Path = "/api/**", Target = "https://api.example", Hosts = ["*.example.test"] };

        ProxyRouteInfo? route = matcher.FindMatchingRoute([hostRoute], "/api/orders", "orders.example.test");

        Assert.Same(hostRoute, route);
    }
}
