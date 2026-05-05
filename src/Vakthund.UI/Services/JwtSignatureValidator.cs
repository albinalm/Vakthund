using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vakthund.Shared.Models;
using Vakthund.UI.Helpers;
using Vakthund.UI.Models;

namespace Vakthund.UI.Services;

public class JwtSignatureValidator(IHttpClientFactory httpClientFactory)
{
    private readonly ConcurrentDictionary<string, string> _jwksCache = new();
    private readonly ConcurrentDictionary<string, string> _metadataCache = new();

    public async Task<JwtSignatureValidationResult> ValidateAsync(ParsedToken token, AuthExpectation expectation, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token.RawToken))
        {
            return Result(JwtSignatureValidationStatus.MalformedToken, "Token is empty.");
        }

        JwtSigningKeyEndpoint? endpoint;
        try
        {
            endpoint = await ResolveSigningKeyEndpointAsync(token, expectation, ct);
        }
        catch (Exception ex)
        {
            return Result(JwtSignatureValidationStatus.FetchFailed, $"Could not load OIDC metadata: {ex.Message}");
        }

        if (endpoint is null)
        {
            return Result(JwtSignatureValidationStatus.NotConfigured, "No JWKS or OIDC metadata endpoint is configured for this route.");
        }

        string rawJwt = token.IsJwe
            ? token.DecryptedRawJwt ?? ""
            : token.RawToken ?? "";

        if (token.IsJwe && string.IsNullOrEmpty(rawJwt))
        {
            return Result(JwtSignatureValidationStatus.NotConfigured, "JWE payload is not a nested JWT; signature validation does not apply.");
        }

        string[] parts = rawJwt.Split('.');
        if (parts.Length != 3)
        {
            return Result(JwtSignatureValidationStatus.MalformedToken, "Signature validation needs a three-part JWT.");
        }

        (string? algorithm, string? tokenKid) = token.IsJwe
            ? ParseJwtHeader(parts[0])
            : (token.Header?.Algorithm, token.Header?.KeyId);
        if (string.IsNullOrWhiteSpace(algorithm) || algorithm.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return Result(JwtSignatureValidationStatus.UnsupportedAlgorithm, $"Unsupported signing algorithm '{algorithm ?? "missing"}'.");
        }

        try
        {
            string jwksJson = await GetCachedStringAsync(_jwksCache, endpoint.Url, ct);
            return ValidateWithJwks(jwksJson, tokenKid, parts, algorithm) with { KeySource = endpoint.Source };
        }
        catch (FormatException ex)
        {
            return Result(JwtSignatureValidationStatus.MalformedToken, $"Token signature or JWKS key material is not valid base64url: {ex.Message}") with { KeySource = endpoint.Source };
        }
        catch (Exception ex)
        {
            return Result(JwtSignatureValidationStatus.FetchFailed, $"Could not load signing keys: {ex.Message}") with { KeySource = endpoint.Source };
        }
    }

    private async Task<JwtSigningKeyEndpoint?> ResolveSigningKeyEndpointAsync(ParsedToken token, AuthExpectation expectation, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(expectation.JwksUrl))
        {
            return new JwtSigningKeyEndpoint(expectation.JwksUrl, JwtSigningKeySource.ConfiguredJwks);
        }

        if (!string.IsNullOrWhiteSpace(expectation.OpenIdConfigurationUrl))
        {
            string? jwksUrl = await ResolveJwksUrlFromMetadataAsync(expectation.OpenIdConfigurationUrl, ct);
            return jwksUrl is null ? null : new JwtSigningKeyEndpoint(jwksUrl, JwtSigningKeySource.ConfiguredOpenIdMetadata);
        }

        if (!string.IsNullOrWhiteSpace(expectation.Issuer))
        {
            string? metadataUrl = BuildMetadataUrl(expectation.Issuer);
            string? jwksUrl = metadataUrl is null ? null : await ResolveJwksUrlFromMetadataAsync(metadataUrl, ct);
            return jwksUrl is null ? null : new JwtSigningKeyEndpoint(jwksUrl, JwtSigningKeySource.ConfiguredIssuerMetadata);
        }

        string? inferredMetadataUrl = BuildMetadataUrl(token.Claims?.Issuer);
        if (inferredMetadataUrl is null)
        {
            return null;
        }

        string? inferredJwksUrl = await ResolveJwksUrlFromMetadataAsync(inferredMetadataUrl, ct);
        return inferredJwksUrl is null ? null : new JwtSigningKeyEndpoint(inferredJwksUrl, JwtSigningKeySource.InferredTokenIssuerMetadata);
    }

    private async Task<string?> ResolveJwksUrlFromMetadataAsync(string metadataUrl, CancellationToken ct)
    {
        if (metadataUrl is null)
        {
            return null;
        }

        string metadataJson = await GetCachedStringAsync(_metadataCache, metadataUrl, ct);
        using JsonDocument doc = JsonDocument.Parse(metadataJson);
        return doc.RootElement.TryGetProperty("jwks_uri", out JsonElement jwksUri) && jwksUri.ValueKind == JsonValueKind.String
            ? jwksUri.GetString()
            : null;
    }

    private static string? BuildMetadataUrl(string? issuer)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string baseUri = uri.ToString().TrimEnd('/');
        return $"{baseUri}/.well-known/openid-configuration";
    }

    private async Task<string> GetCachedStringAsync(ConcurrentDictionary<string, string> cache, string url, CancellationToken ct)
    {
        if (cache.TryGetValue(url, out string? cached))
        {
            return cached;
        }

        HttpClient client = httpClientFactory.CreateClient();
        string value = await client.GetStringAsync(url, ct);
        cache[url] = value;
        return value;
    }

    private static JwtSignatureValidationResult ValidateWithJwks(string jwksJson, string? tokenKid, string[] parts, string algorithm)
    {
        using JsonDocument doc = JsonDocument.Parse(jwksJson);
        if (!doc.RootElement.TryGetProperty("keys", out JsonElement keys) || keys.ValueKind != JsonValueKind.Array)
        {
            return Result(JwtSignatureValidationStatus.UnknownKey, "JWKS response does not contain a keys array.");
        }

        byte[] data = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        byte[] signature = Base64Url.DecodeBytes(parts[2]);
        var matchingKidSeen = false;
        var supportedAlgorithmSeen = false;

        foreach (JsonElement key in keys.EnumerateArray())
        {
            string? keyId = ReadString(key, "kid");
            if (!string.IsNullOrWhiteSpace(tokenKid) && keyId != tokenKid)
            {
                continue;
            }

            matchingKidSeen = true;
            if (TryVerifyKey(key, algorithm, data, signature, out bool verified))
            {
                supportedAlgorithmSeen = true;
                if (verified)
                {
                    return Result(JwtSignatureValidationStatus.Valid, KeyMessage("Signature is valid.", keyId));
                }
            }
        }

        if (!matchingKidSeen)
        {
            return Result(JwtSignatureValidationStatus.UnknownKey, $"No JWKS key matched kid '{tokenKid ?? "missing"}'.");
        }

        return supportedAlgorithmSeen
            ? Result(JwtSignatureValidationStatus.Invalid, "Signature validation failed with the matching JWKS key.")
            : Result(JwtSignatureValidationStatus.UnsupportedAlgorithm, $"No compatible JWKS key was found for algorithm '{algorithm}'.");
    }

    private static bool TryVerifyKey(JsonElement key, string algorithm, byte[] data, byte[] signature, out bool verified)
    {
        verified = false;
        string? keyType = ReadString(key, "kty");

        try
        {
            if (keyType == "RSA" && IsRsaAlgorithm(algorithm))
            {
                verified = VerifyRsa(key, algorithm, data, signature);
                return true;
            }

            if (keyType == "EC" && IsEcAlgorithm(algorithm))
            {
                verified = VerifyEc(key, algorithm, data, signature);
                return true;
            }
        }
        catch (CryptographicException)
        {
        }

        return false;
    }

    private static bool VerifyRsa(JsonElement key, string algorithm, byte[] data, byte[] signature)
    {
        string? modulus = ReadString(key, "n");
        string? exponent = ReadString(key, "e");
        if (modulus is null || exponent is null)
        {
            return false;
        }

        using RSA rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = Base64Url.DecodeBytes(modulus),
            Exponent = Base64Url.DecodeBytes(exponent)
        });

        return rsa.VerifyData(data, signature, HashAlgorithmNameFor(algorithm), PaddingFor(algorithm));
    }

    private static bool VerifyEc(JsonElement key, string algorithm, byte[] data, byte[] signature)
    {
        string? curve = ReadString(key, "crv");
        string? x = ReadString(key, "x");
        string? y = ReadString(key, "y");
        if (curve is null || x is null || y is null)
        {
            return false;
        }

        using ECDsa ec = ECDsa.Create(new ECParameters
        {
            Curve = CurveFor(curve),
            Q = new ECPoint
            {
                X = Base64Url.DecodeBytes(x),
                Y = Base64Url.DecodeBytes(y)
            }
        });

        return ec.VerifyData(data, signature, HashAlgorithmNameFor(algorithm), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static bool IsRsaAlgorithm(string algorithm) =>
        algorithm is "RS256" or "RS384" or "RS512" or "PS256" or "PS384" or "PS512";

    private static bool IsEcAlgorithm(string algorithm) =>
        algorithm is "ES256" or "ES384" or "ES512";

    private static HashAlgorithmName HashAlgorithmNameFor(string algorithm) => algorithm switch
    {
        "RS384" or "PS384" or "ES384" => HashAlgorithmName.SHA384,
        "RS512" or "PS512" or "ES512" => HashAlgorithmName.SHA512,
        _ => HashAlgorithmName.SHA256
    };

    private static RSASignaturePadding PaddingFor(string algorithm) =>
        algorithm.StartsWith("PS", StringComparison.Ordinal) ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1;

    private static ECCurve CurveFor(string curve) => curve switch
    {
        "P-384" => ECCurve.NamedCurves.nistP384,
        "P-521" => ECCurve.NamedCurves.nistP521,
        _ => ECCurve.NamedCurves.nistP256
    };

    private static string? ReadString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static (string? Algorithm, string? KeyId) ParseJwtHeader(string base64UrlHeader)
    {
        try
        {
            byte[] bytes = Base64Url.DecodeBytes(base64UrlHeader);
            using JsonDocument doc = JsonDocument.Parse(bytes);
            string? alg = ReadString(doc.RootElement, "alg");
            string? kid = ReadString(doc.RootElement, "kid");
            return (alg, kid);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string KeyMessage(string message, string? keyId) =>
        string.IsNullOrWhiteSpace(keyId) ? message : $"{message} Matched kid '{keyId}'.";

    private static JwtSignatureValidationResult Result(JwtSignatureValidationStatus status, string message) =>
        new() { Status = status, Message = message };
}
