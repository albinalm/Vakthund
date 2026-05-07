using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Vakthund.Proxy.Models;
using Vakthund.Shared.Models;

namespace Vakthund.Proxy.Services;

public static class RouteAuthPolicies
{
    private const string PolicyPrefix = "vakthund-route-auth-";
    private const string SchemePrefix = "vakthund-route-jwt-";
    private const string IpSchemeName = "vakthund-route-ip";

    public static bool EnforcesAuth(VakthundRoute route) => route.Auth?.Enforced == true;

    public static bool RestrictsIp(VakthundRoute route) => RequiredValues(route.Ips ?? []).Length > 0;

    public static bool RequiresAuthorization(VakthundRoute route) => EnforcesAuth(route) || RestrictsIp(route);

    public static string PolicyName(int routeIndex) => $"{PolicyPrefix}{routeIndex}";

    public static string SchemeName(int routeIndex) => $"{SchemePrefix}{routeIndex}";

    public static IServiceCollection AddRouteAuthentication(this IServiceCollection services, IReadOnlyList<VakthundRoute> routes)
    {
        ValidateRoutes(routes);

        AuthenticationBuilder authentication = services.AddAuthentication();
        foreach ((VakthundRoute route, int index) in EnforcedRoutes(routes))
        {
            authentication.AddJwtBearer(SchemeName(index), options => ConfigureJwtBearer(options, route.Auth!));
        }

        if (routes.Any(route => RestrictsIp(route) && !EnforcesAuth(route)))
        {
            authentication.AddScheme<AuthenticationSchemeOptions, IpWhitelistAuthenticationHandler>(
                IpSchemeName,
                _ => { });
        }

        services.AddAuthorization(options =>
        {
            foreach ((VakthundRoute route, int index) in AuthorizedRoutes(routes))
            {
                options.AddPolicy(PolicyName(index), policy => ConfigurePolicy(policy, route, index));
            }
        });

        return services;
    }

    private static IReadOnlyList<string> ExpectedAudiences(AuthExpectation auth)
    {
        var audiences = new List<string>();
        if (!string.IsNullOrWhiteSpace(auth.Audience))
        {
            audiences.Add(auth.Audience);
        }

        audiences.AddRange(auth.Audiences.Where(audience => !string.IsNullOrWhiteSpace(audience)));
        return audiences.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<(VakthundRoute Route, int Index)> EnforcedRoutes(IReadOnlyList<VakthundRoute> routes) =>
        routes.Select((route, index) => (Route: route, Index: index)).Where(item => EnforcesAuth(item.Route));

    private static IEnumerable<(VakthundRoute Route, int Index)> AuthorizedRoutes(IReadOnlyList<VakthundRoute> routes) =>
        routes.Select((route, index) => (Route: route, Index: index)).Where(item => RequiresAuthorization(item.Route));

    private static void ConfigureJwtBearer(JwtBearerOptions options, AuthExpectation auth)
    {
        IReadOnlyList<string> audiences = ExpectedAudiences(auth);

        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = !string.IsNullOrWhiteSpace(auth.Issuer),
            ValidIssuer = auth.Issuer,
            ValidateAudience = audiences.Count > 0,
            ValidAudiences = audiences,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "sub",
            RoleClaimType = "roles"
        };

        if (!string.IsNullOrWhiteSpace(auth.JwksUrl))
        {
            options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) =>
                JwksSigningKeyResolver.Resolve(auth.JwksUrl!);
            return;
        }

        if (!string.IsNullOrWhiteSpace(auth.OpenIdConfigurationUrl))
        {
            options.MetadataAddress = auth.OpenIdConfigurationUrl;
            return;
        }

        options.Authority = auth.Issuer!.TrimEnd('/');
    }

    private static void ConfigurePolicy(AuthorizationPolicyBuilder policy, VakthundRoute route, int routeIndex)
    {
        if (EnforcesAuth(route))
        {
            string schemeName = SchemeName(routeIndex);
            AuthExpectation auth = route.Auth!;
            policy.AuthenticationSchemes.Add(schemeName);
            policy.RequireAuthenticatedUser();

            string[] scopes = RequiredValues(auth.Scopes);
            if (scopes.Length > 0)
            {
                policy.RequireAssertion(context => HasAllClaimValues(context.User, scopes, "scope", "scp"));
            }

            string[] roles = RequiredValues(auth.Roles);
            if (roles.Length > 0)
            {
                policy.RequireAssertion(context => HasAllClaimValues(context.User, roles, "roles", "role", ClaimTypes.Role));
            }
        }

        if (RestrictsIp(route))
        {
            if (!EnforcesAuth(route))
            {
                policy.AuthenticationSchemes.Add(IpSchemeName);
            }

            IReadOnlyList<IpWhitelist.IpRange> ranges = IpWhitelist.Parse(route.Ips, route.Path);
            policy.RequireAssertion(context =>
                context.Resource is HttpContext httpContext &&
                IpWhitelist.Allows(ClientIpResolver.Resolve(httpContext), ranges));
        }
    }

    private static void ValidateRoutes(IReadOnlyList<VakthundRoute> routes)
    {
        foreach ((VakthundRoute route, _) in AuthorizedRoutes(routes))
        {
            _ = IpWhitelist.Parse(route.Ips, route.Path);
        }

        foreach ((VakthundRoute route, _) in EnforcedRoutes(routes))
        {
            AuthExpectation auth = route.Auth!;
            if (HasSigningKeySource(auth))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Route '{route.Path}' has auth.enforced set to true, but no signing key source. " +
                "Set auth.jwksUrl, auth.openIdConfigurationUrl, or an absolute auth.issuer for OIDC discovery.");
        }
    }

    private static bool HasSigningKeySource(AuthExpectation auth) =>
        !string.IsNullOrWhiteSpace(auth.JwksUrl) ||
        !string.IsNullOrWhiteSpace(auth.OpenIdConfigurationUrl) ||
        IsHttpUri(auth.Issuer);

    private static bool IsHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string[] RequiredValues(IEnumerable<string> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HasAllClaimValues(ClaimsPrincipal user, IReadOnlyList<string> requiredValues, params string[] claimTypes)
    {
        string[] actualValues = user.Claims
            .Where(claim => claimTypes.Any(type => string.Equals(type, claim.Type, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();

        return requiredValues.All(required =>
            actualValues.Contains(required, StringComparer.OrdinalIgnoreCase));
    }
}
