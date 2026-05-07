using System.Security.Claims;
using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Vakthund.Proxy.Models;
using Vakthund.Proxy.Services;
using Vakthund.Shared.Models;

namespace Vakthund.Tests.Services;

public class RouteAuthPoliciesTests
{
    [Fact]
    public void AddRouteAuthentication_RegistersPolicyAndJwtScheme_ForEnforcedRoute()
    {
        var services = new ServiceCollection();
        services.AddRouteAuthentication(
        [
            new VakthundRoute
            {
                Path = "/api/**",
                Target = "https://backend.example",
                Auth = new AuthExpectation
                {
                    Enforced = true,
                    Issuer = "https://issuer.example",
                    Audience = "orders-api",
                    Scopes = ["orders.read"],
                    Roles = ["admin"],
                    JwksUrl = "https://issuer.example/jwks",
                    Jwe = new JweDecryptionConfig
                    {
                        KeyType = JweKeyType.Symmetric,
                        Key = Convert.ToBase64String(new byte[32])
                    }
                }
            }
        ]);

        using ServiceProvider provider = services.BuildServiceProvider();
        var authorizationOptions = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        AuthorizationPolicy? policy = authorizationOptions.GetPolicy(RouteAuthPolicies.PolicyName(0));
        JwtBearerOptions jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(RouteAuthPolicies.SchemeName(0));

        Assert.NotNull(policy);
        Assert.Contains(RouteAuthPolicies.SchemeName(0), policy.AuthenticationSchemes);
        Assert.True(jwtOptions.TokenValidationParameters.ValidateIssuer);
        Assert.Equal("https://issuer.example", jwtOptions.TokenValidationParameters.ValidIssuer);
        Assert.True(jwtOptions.TokenValidationParameters.ValidateAudience);
        Assert.Contains("orders-api", jwtOptions.TokenValidationParameters.ValidAudiences);
        Assert.True(jwtOptions.TokenValidationParameters.ValidateLifetime);
        Assert.NotNull(jwtOptions.TokenValidationParameters.IssuerSigningKeyResolver);
        SymmetricSecurityKey decryptionKey = Assert.IsType<SymmetricSecurityKey>(jwtOptions.TokenValidationParameters.TokenDecryptionKey);
        Assert.Equal(32, decryptionKey.KeySize / 8);
    }

    [Fact]
    public void AddRouteAuthentication_RejectsPartialJweConfig_ForEnforcedRoute()
    {
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => services.AddRouteAuthentication(
        [
            new VakthundRoute
            {
                Path = "/api/**",
                Target = "https://backend.example",
                Auth = new AuthExpectation
                {
                    Enforced = true,
                    Issuer = "https://issuer.example",
                    JwksUrl = "https://issuer.example/jwks",
                    Jwe = new JweDecryptionConfig
                    {
                        KeyType = JweKeyType.Symmetric
                    }
                }
            }
        ]));

        Assert.Contains("keyType and key", exception.Message);
    }

    [Fact]
    public void AddRouteAuthentication_RejectsEnforcedRouteWithoutSigningKeySource()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddRouteAuthentication(
        [
            new VakthundRoute
            {
                Path = "/api/**",
                Target = "https://backend.example",
                Auth = new AuthExpectation
                {
                    Enforced = true,
                    Audience = "orders-api"
                }
            }
        ]));

        Assert.Contains("no signing key source", ex.Message);
    }

    [Fact]
    public async Task RoutePolicy_RequiresConfiguredScopesAndRoles()
    {
        using ServiceProvider provider = BuildEnforcedRouteServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var matchingUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("scope", "orders.read orders.write"),
            new Claim("roles", "admin")
        ], RouteAuthPolicies.SchemeName(0)));
        var missingScopeUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("scope", "orders.write"),
            new Claim("roles", "admin")
        ], RouteAuthPolicies.SchemeName(0)));

        AuthorizationResult matchingResult = await authorization.AuthorizeAsync(
            matchingUser,
            null,
            RouteAuthPolicies.PolicyName(0));
        AuthorizationResult missingScopeResult = await authorization.AuthorizeAsync(
            missingScopeUser,
            null,
            RouteAuthPolicies.PolicyName(0));

        Assert.True(matchingResult.Succeeded);
        Assert.False(missingScopeResult.Succeeded);
    }

    [Fact]
    public async Task RoutePolicy_AllowsMatchingClientIp()
    {
        using ServiceProvider provider = BuildIpWhitelistServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");

        AuthorizationResult result = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            context,
            RouteAuthPolicies.PolicyName(0));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task RoutePolicy_RejectsNonMatchingClientIp()
    {
        using ServiceProvider provider = BuildIpWhitelistServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.10");

        AuthorizationResult result = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            context,
            RouteAuthPolicies.PolicyName(0));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void AddRouteAuthentication_RejectsInvalidIpWhitelistEntry()
    {
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => services.AddRouteAuthentication(
        [
            new VakthundRoute
            {
                Path = "/api/**",
                Target = "https://backend.example",
                Ips = ["203.*.113.*"]
            }
        ]));

        Assert.Contains("invalid ip whitelist entry", exception.Message);
    }

    private static ServiceProvider BuildEnforcedRouteServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouteAuthentication(
        [
            new VakthundRoute
            {
                Path = "/api/**",
                Target = "https://backend.example",
                Auth = new AuthExpectation
                {
                    Enforced = true,
                    Issuer = "https://issuer.example",
                    Audience = "orders-api",
                    Scopes = ["orders.read"],
                    Roles = ["admin"],
                    JwksUrl = "https://issuer.example/jwks"
                }
            }
        ]);

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildIpWhitelistServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouteAuthentication(
        [
            new VakthundRoute
            {
                Path = "/api/**",
                Target = "https://backend.example",
                Ips = ["203.0.113.*", "10.0.0.0/8"]
            }
        ]);

        return services.BuildServiceProvider();
    }
}
