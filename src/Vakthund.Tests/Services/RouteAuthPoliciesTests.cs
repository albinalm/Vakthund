using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
                    JwksUrl = "https://issuer.example/jwks"
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
}
