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
    public void TryLoad_MapsRouteTimeout()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /api/generate
                target: https://backend.example.test
                timeout: 1h
            """);

        try
        {
            VakthundRoute route = Assert.Single(RoutesLoader.TryLoad(filePath)!);

            Assert.Equal("1h", route.Timeout);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_MapsRouteHosts()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /api/**
                target: https://backend.example.test
                hosts:
                  - api.example.test
                  - "*.internal.example.test"
            """);

        try
        {
            VakthundRoute route = Assert.Single(RoutesLoader.TryLoad(filePath)!);

            Assert.Equal(["api.example.test", "*.internal.example.test"], route.Hosts);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_MapsRouteIpWhitelist()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /api/**
                target: https://backend.example.test
                ips:
                  - 203.0.113.*
                  - 10.0.0.0/8
            """);

        try
        {
            VakthundRoute route = Assert.Single(RoutesLoader.TryLoad(filePath)!);

            Assert.Equal(["203.0.113.*", "10.0.0.0/8"], route.Ips);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_ResolvesNamedRouteIps()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            ipPolicies:
              - name: office
                entries:
                  - 203.0.113.*
                  - 10.0.0.0/8
            routes:
              - path: /orders/**
                target: https://orders.example.test
                ips: office
              - path: /invoices/**
                target: https://invoices.example.test
                ips: office
            """);

        try
        {
            List<VakthundRoute> routes = RoutesLoader.TryLoad(filePath)!;

            Assert.Equal(2, routes.Count);
            Assert.Equal(["203.0.113.*", "10.0.0.0/8"], routes[0].Ips);
            Assert.Equal(["203.0.113.*", "10.0.0.0/8"], routes[1].Ips);
            Assert.NotSame(routes[0].Ips, routes[1].Ips);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_PreservesInlineRouteIps_WhenIpPoliciesExist()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            ipPolicies:
              - name: office
                entries:
                  - 203.0.113.*
            routes:
              - path: /api/**
                target: https://backend.example.test
                ips:
                  - 10.0.0.1
                  - 192.168.1.0/24
            """);

        try
        {
            VakthundRoute route = Assert.Single(RoutesLoader.TryLoad(filePath)!);

            Assert.Equal(["10.0.0.1", "192.168.1.0/24"], route.Ips);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_Throws_WhenNamedRouteIpIsMissing()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /api/**
                target: https://backend.example.test
                ips: missing-policy
            """);

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => RoutesLoader.TryLoad(filePath));

            Assert.Contains("unknown ip policy 'missing-policy'", exception.Message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_Throws_WhenIpPolicyNameIsDuplicate()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            ipPolicies:
              - name: office
                entries:
                  - 10.0.0.1
              - name: office
                entries:
                  - 192.168.0.1
            routes:
              - path: /api/**
                target: https://backend.example.test
            """);

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => RoutesLoader.TryLoad(filePath));

            Assert.Contains("duplicate ip policy name 'office'", exception.Message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_Throws_WhenIpPolicyNameIsEmpty()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            ipPolicies:
              - name:
                entries:
                  - 10.0.0.1
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

            AuthExpectation auth = Assert.IsType<AuthExpectation>(route.Auth);
            JweDecryptionConfig jwe = Assert.IsType<JweDecryptionConfig>(auth.Jwe);
            Assert.Equal(JweKeyType.Symmetric, jwe.KeyType);
            Assert.Equal("base64-key", jwe.Key);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TryLoad_MapsRouteAuthEnforce()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /api/**
                target: https://backend.example.test
                auth:
                  enforce: true
                  subject: user-123
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
            Assert.Equal("user-123", route.Auth.Subject);
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
                enforce: true
                subject: user-123
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
            Assert.Equal("user-123", firstAuth.Subject);
            Assert.Equal("https://issuer.example", firstAuth.Issuer);
            Assert.Equal("orders-api", firstAuth.Audience);
            Assert.Equal(["orders.read"], firstAuth.Scopes);
            Assert.Equal("user-123", secondAuth.Subject);
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
