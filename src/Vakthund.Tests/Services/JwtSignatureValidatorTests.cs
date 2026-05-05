using System.Security.Cryptography;
using Vakthund.Shared.Models;
using Vakthund.Tests.Helpers;
using Vakthund.UI.Models;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Services;

public class JwtSignatureValidatorTests
{
    [Fact]
    public async Task ValidateAsync_UsesConfiguredJwks_WhenConfigured()
    {
        using RSA rsa = RSA.Create(2048);
        string token = RsaJwtTestTokenFactory.BuildToken(rsa, "configured-key", "https://token-issuer.example");
        JwtSignatureValidator validator = ValidatorWithResponses(new Dictionary<string, string>
        {
            ["https://configured.example/jwks"] = RsaJwtTestTokenFactory.BuildJwks(rsa, "configured-key")
        });

        JwtSignatureValidationResult result = await validator.ValidateAsync(
            RsaJwtTestTokenFactory.ParseToken(token),
            new AuthExpectation { JwksUrl = "https://configured.example/jwks" });

        Assert.Equal(JwtSignatureValidationStatus.Valid, result.Status);
        Assert.Equal(JwtSigningKeySource.ConfiguredJwks, result.KeySource);
    }

    [Fact]
    public async Task ValidateAsync_UsesConfiguredIssuerMetadata_BeforeTokenIssuerMetadata()
    {
        using RSA rsa = RSA.Create(2048);
        string token = RsaJwtTestTokenFactory.BuildToken(rsa, "configured-issuer-key", "https://token-issuer.example");
        JwtSignatureValidator validator = ValidatorWithResponses(new Dictionary<string, string>
        {
            ["https://configured-issuer.example/.well-known/openid-configuration"] = """{"jwks_uri":"https://configured-issuer.example/jwks"}""",
            ["https://configured-issuer.example/jwks"] = RsaJwtTestTokenFactory.BuildJwks(rsa, "configured-issuer-key")
        });

        JwtSignatureValidationResult result = await validator.ValidateAsync(
            RsaJwtTestTokenFactory.ParseToken(token),
            new AuthExpectation { Issuer = "https://configured-issuer.example" });

        Assert.Equal(JwtSignatureValidationStatus.Valid, result.Status);
        Assert.Equal(JwtSigningKeySource.ConfiguredIssuerMetadata, result.KeySource);
    }

    [Fact]
    public async Task ValidateAsync_InfersMetadataFromTokenIssuer_WhenRouteMetadataIsMissing()
    {
        using RSA rsa = RSA.Create(2048);
        string token = RsaJwtTestTokenFactory.BuildToken(rsa, "inferred-key", "https://issuer.example");
        JwtSignatureValidator validator = ValidatorWithResponses(new Dictionary<string, string>
        {
            ["https://issuer.example/.well-known/openid-configuration"] = """{"jwks_uri":"https://issuer.example/jwks"}""",
            ["https://issuer.example/jwks"] = RsaJwtTestTokenFactory.BuildJwks(rsa, "inferred-key")
        });

        JwtSignatureValidationResult result = await validator.ValidateAsync(RsaJwtTestTokenFactory.ParseToken(token), new AuthExpectation());

        Assert.Equal(JwtSignatureValidationStatus.Valid, result.Status);
        Assert.Equal(JwtSigningKeySource.InferredTokenIssuerMetadata, result.KeySource);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsUnknownKey_WhenKidDoesNotMatch()
    {
        using RSA rsa = RSA.Create(2048);
        string token = RsaJwtTestTokenFactory.BuildToken(rsa, "token-key", "https://issuer.example");
        JwtSignatureValidator validator = ValidatorWithResponses(new Dictionary<string, string>
        {
            ["https://issuer.example/.well-known/openid-configuration"] = """{"jwks_uri":"https://issuer.example/jwks"}""",
            ["https://issuer.example/jwks"] = RsaJwtTestTokenFactory.BuildJwks(rsa, "other-key")
        });

        JwtSignatureValidationResult result = await validator.ValidateAsync(RsaJwtTestTokenFactory.ParseToken(token), new AuthExpectation());

        Assert.Equal(JwtSignatureValidationStatus.UnknownKey, result.Status);
        Assert.Equal(JwtSigningKeySource.InferredTokenIssuerMetadata, result.KeySource);
    }

    private static JwtSignatureValidator ValidatorWithResponses(IReadOnlyDictionary<string, string> responses)
    {
        var handler = new StaticHttpMessageHandler(responses);
        var client = new HttpClient(handler);
        return new JwtSignatureValidator(new StaticHttpClientFactory(client));
    }

}
