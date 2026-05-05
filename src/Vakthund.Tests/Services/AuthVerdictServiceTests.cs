using System.Security.Cryptography;
using Vakthund.Shared.Models;
using Vakthund.Tests.Helpers;
using Vakthund.UI.Models;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Services;

public class AuthVerdictServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void Evaluate_ReturnsNoBearerVerdict_ForAuthFailureWithoutToken()
    {
        var service = new AuthVerdictService();

        AuthVerdict verdict = service.Evaluate(Entry(401), [], Now);

        Assert.Equal(AuthVerdictSeverity.Error, verdict.Severity);
        Assert.Equal("No bearer token found.", verdict.Title);
    }

    [Fact]
    public void Evaluate_ReturnsMalformedVerdict_ForUnreadableBearerToken()
    {
        var service = new AuthVerdictService();
        var token = new ParsedToken { Scheme = "Bearer", RawToken = "not-a-jwt" };

        AuthVerdict verdict = service.Evaluate(Entry(401), [token], Now);

        Assert.Equal(AuthVerdictSeverity.Error, verdict.Severity);
        Assert.Equal("Bearer token could not be decoded.", verdict.Title);
    }

    [Fact]
    public void Evaluate_ReturnsExpiredVerdict_ForExpiredToken()
    {
        var service = new AuthVerdictService();
        var token = new ParsedToken
        {
            Scheme = "Bearer",
            JwtPayloadJson = "{}",
            Claims = new TokenClaimSummary { ExpiresAt = Now.AddMinutes(-1) }
        };

        AuthVerdict verdict = service.Evaluate(Entry(401), [token], Now);

        Assert.Equal(AuthVerdictSeverity.Error, verdict.Severity);
        Assert.Equal("Token is expired.", verdict.Title);
    }

    [Fact]
    public void Evaluate_ReturnsNotYetValidVerdict_ForFutureNotBefore()
    {
        var service = new AuthVerdictService();
        var token = new ParsedToken
        {
            Scheme = "Bearer",
            JwtPayloadJson = "{}",
            Claims = new TokenClaimSummary { NotBefore = Now.AddMinutes(1) }
        };

        AuthVerdict verdict = service.Evaluate(Entry(401), [token], Now);

        Assert.Equal(AuthVerdictSeverity.Error, verdict.Severity);
        Assert.Equal("Token is not valid yet.", verdict.Title);
    }

    [Fact]
    public void Evaluate_ReturnsBackendRejectedVerdict_For403WithUsableToken()
    {
        var service = new AuthVerdictService();
        var token = new ParsedToken
        {
            Scheme = "Bearer",
            JwtPayloadJson = "{}",
            Claims = new TokenClaimSummary { ExpiresAt = Now.AddMinutes(10) }
        };

        AuthVerdict verdict = service.Evaluate(Entry(403), [token], Now);

        Assert.Equal(AuthVerdictSeverity.Warning, verdict.Severity);
        Assert.Equal("Backend returned 403.", verdict.Title);
    }

    [Fact]
    public void Evaluate_ReturnsExpectationMismatchVerdict_WhenAudienceDoesNotMatchRoute()
    {
        var service = new AuthVerdictService();
        var token = new ParsedToken
        {
            Scheme = "Bearer",
            JwtPayloadJson = "{}",
            Claims = new TokenClaimSummary
            {
                Issuer = "https://issuer.example",
                Audiences = ["account-api"],
                Scopes = ["orders.read"]
            }
        };
        var config = new ProxyConfig
        {
            Routes =
            [
                new ProxyRouteInfo
                {
                    Path = "/api/**",
                    Target = "http://localhost:5000",
                    Auth = new AuthExpectation
                    {
                        Issuer = "https://issuer.example",
                        Audience = "orders-api",
                        Scopes = ["orders.read"]
                    }
                }
            ]
        };

        AuthVerdict verdict = service.Evaluate(Entry(401), [token], config, Now);

        Assert.Equal(AuthVerdictSeverity.Error, verdict.Severity);
        Assert.Equal("Token does not match route auth expectations.", verdict.Title);
        Assert.Contains("Expected audience 'orders-api'", verdict.Detail);
    }

    [Fact]
    public void Evaluate_ReturnsMatchesVerdict_WhenTokenMatchesRouteExpectations()
    {
        var service = new AuthVerdictService();
        var token = new ParsedToken
        {
            Scheme = "Bearer",
            JwtPayloadJson = "{}",
            Claims = new TokenClaimSummary
            {
                Issuer = "https://issuer.example",
                Audiences = ["orders-api"],
                Scopes = ["orders.read"],
                Roles = ["admin"],
                ExpiresAt = Now.AddMinutes(10)
            }
        };
        var config = new ProxyConfig
        {
            Routes =
            [
                new ProxyRouteInfo
                {
                    Path = "/api/**",
                    Target = "http://localhost:5000",
                    Auth = new AuthExpectation
                    {
                        Issuer = "https://issuer.example",
                        Audience = "orders-api",
                        Scopes = ["orders.read"],
                        Roles = ["admin"]
                    }
                }
            ]
        };

        AuthVerdict verdict = service.Evaluate(Entry(200), [token], config, Now);

        Assert.Equal(AuthVerdictSeverity.Info, verdict.Severity);
        Assert.Equal("Token matches configured route expectations.", verdict.Title);
    }

    [Fact]
    public async Task EvaluateAsync_AppendsInferredTokenIssuerSource_WhenSignatureValidationUsesTokenIssuer()
    {
        using RSA rsa = RSA.Create(2048);
        string tokenValue = RsaJwtTestTokenFactory.BuildToken(rsa, "inferred-key", "https://issuer.example");
        ParsedToken token = RsaJwtTestTokenFactory.ParseToken(tokenValue);
        var validator = new JwtSignatureValidator(new StaticHttpClientFactory(new HttpClient(new StaticHttpMessageHandler(new Dictionary<string, string>
        {
            ["https://issuer.example/.well-known/openid-configuration"] = """{"jwks_uri":"https://issuer.example/jwks"}""",
            ["https://issuer.example/jwks"] = RsaJwtTestTokenFactory.BuildJwks(rsa, "inferred-key")
        }))));
        var service = new AuthVerdictService(validator);

        AuthVerdict verdict = await service.EvaluateAsync(Entry(200), [token], null, Now);

        Assert.Equal(AuthVerdictSeverity.Info, verdict.Severity);
        Assert.Contains("JWKS source: inferred from token issuer.", verdict.Detail);
    }

    private static AuditEntry Entry(int statusCode) => new()
    {
        Scheme = "http",
        Host = "localhost",
        Path = "/api/orders",
        Method = "GET",
        StatusCode = statusCode
    };
}
