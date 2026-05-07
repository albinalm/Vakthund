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

    [Fact]
    public void TryLoad_ResolvesNamedRouteAuth()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            auths:
              - name: shared-auth
                enforced: true
                issuer: https://issuer.example
                audience: orders-api
                scopes:
                  - orders.read
            routes:
              - path: /orders/**
                target: https://orders.example.test
                auth: shared-auth
              - path: /invoices/**
                target: https://invoices.example.test
                auth: shared-auth
            """);

        try
        {
            List<VakthundRoute> routes = RoutesLoader.TryLoad(filePath)!;

            Assert.Equal(2, routes.Count);
            AuthExpectation firstAuth = routes[0].Auth!;
            AuthExpectation secondAuth = routes[1].Auth!;
            Assert.NotNull(firstAuth);
            Assert.NotNull(secondAuth);
            Assert.True(firstAuth.Enforced);
            Assert.Equal("https://issuer.example", firstAuth.Issuer);
            Assert.Equal("orders-api", firstAuth.Audience);
            Assert.Equal(["orders.read"], firstAuth.Scopes);
            Assert.Equal("https://issuer.example", secondAuth.Issuer);
            Assert.NotSame(firstAuth, secondAuth);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_PreservesInlineRouteAuth_WhenAuthsExist()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            auths:
              - name: shared-auth
                issuer: https://shared.example
            routes:
              - path: /api/**
                target: https://backend.example.test
                auth:
                  issuer: https://inline.example
                  audience: inline-api
            """);

        try
        {
            VakthundRoute route = Assert.Single(RoutesLoader.TryLoad(filePath)!);

            Assert.NotNull(route.Auth);
            Assert.Equal("https://inline.example", route.Auth.Issuer);
            Assert.Equal("inline-api", route.Auth.Audience);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_Throws_WhenNamedRouteAuthIsMissing()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /api/**
                target: https://backend.example.test
                auth: missing-auth
            """);

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => RoutesLoader.TryLoad(filePath));

            Assert.Contains("unknown auth 'missing-auth'", exception.Message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_Throws_WhenNamedAuthNameIsDuplicate()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            auths:
              - name: shared-auth
              - name: shared-auth
            routes:
              - path: /api/**
                target: https://backend.example.test
                auth: shared-auth
            """);

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => RoutesLoader.TryLoad(filePath));

            Assert.Contains("duplicate auth name 'shared-auth'", exception.Message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_Throws_WhenNamedAuthNameIsEmpty()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            auths:
              - name:
            routes:
              - path: /api/**
                target: https://backend.example.test
            """);

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => RoutesLoader.TryLoad(filePath));

            Assert.Contains("non-empty name", exception.Message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
