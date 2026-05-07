using Vakthund.Proxy.Models;
using Vakthund.Proxy.Services;
using Vakthund.Shared.Models;

namespace Vakthund.Tests.Services;

public class RoutesLoaderTests
{
    [Fact]
    public void TryLoad_ReturnsNull_WhenFileIsMissing()
    {
        string missingFile = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");

        Assert.Null(RoutesLoader.TryLoad(missingFile));
    }

    [Fact]
    public void TryLoad_MapsCamelCaseYamlRoutes()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /api/{**catch-all}
                target: https://backend.example.test
            """);

        try
        {
            VakthundRoute route = Assert.Single(RoutesLoader.TryLoad(filePath)!);

            Assert.Equal("/api/{**catch-all}", route.Path);
            Assert.Equal("https://backend.example.test", route.Target);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_MapsRouteToPath()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /orders/**
                target: https://backend.example.test
                to: /api/orders
            """);

        try
        {
            VakthundRoute route = Assert.Single(RoutesLoader.TryLoad(filePath)!);

            Assert.Equal("/orders/**", route.Path);
            Assert.Equal("https://backend.example.test", route.Target);
            Assert.Equal("/api/orders", route.To);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_MapsRouteJweDecryptionConfig()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /api/**
                target: https://backend.example.test
                auth:
                  jwe:
                    keyType: Symmetric
                    key: base64-key
            """);

        try
        {
            VakthundRoute route = Assert.Single(RoutesLoader.TryLoad(filePath)!);

            Assert.NotNull(route.Auth);
            Assert.Equal(JweKeyType.Symmetric, route.Auth.Jwe.KeyType);
            Assert.Equal("base64-key", route.Auth.Jwe.Key);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_MapsRouteAuthEnforced()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /api/**
                target: https://backend.example.test
                auth:
                  enforced: true
                  issuer: https://issuer.example
                  audience: orders-api
                  scopes:
                    - orders.read
            """);

        try
        {
            VakthundRoute route = Assert.Single(RoutesLoader.TryLoad(filePath)!);

            Assert.NotNull(route.Auth);
            Assert.True(route.Auth.Enforced);
            Assert.Equal("https://issuer.example", route.Auth.Issuer);
            Assert.Equal("orders-api", route.Auth.Audience);
            Assert.Equal(["orders.read"], route.Auth.Scopes);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
