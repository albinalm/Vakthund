using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vakthund.UI.Models;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Helpers;

public static class RsaJwtTestTokenFactory
{
    public static string BuildToken(RSA rsa, string keyId, string issuer)
    {
        string header = Base64UrlJson(new { alg = "RS256", typ = "JWT", kid = keyId });
        string payload = Base64UrlJson(new { iss = issuer, aud = "orders-api", sub = "user-123", exp = 4_102_444_800 });
        byte[] data = Encoding.ASCII.GetBytes($"{header}.{payload}");
        byte[] signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{payload}.{Base64UrlBytes(signature)}";
    }

    public static string BuildJwks(RSA rsa, string keyId)
    {
        RSAParameters parameters = rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    kid = keyId,
                    alg = "RS256",
                    n = Base64UrlBytes(parameters.Modulus!),
                    e = Base64UrlBytes(parameters.Exponent!)
                }
            }
        });
    }

    public static ParsedToken ParseToken(string token)
    {
        var parser = new JwtTokenParser();
        return Assert.Single(parser.Parse(new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {token}"
        }));
    }

    private static string Base64UrlJson(object value) =>
        Base64UrlBytes(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));

    private static string Base64UrlBytes(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
