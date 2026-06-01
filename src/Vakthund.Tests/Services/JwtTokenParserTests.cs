using System.Text;
using System.Text.Json;
using Jose;
using Vakthund.Shared.Models;
using Vakthund.UI.Models;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Services;

public class JwtTokenParserTests
{
    [Fact]
    public void Parse_DecodesBearerJwt_FromAuthorizationHeader()
    {
        var parser = new JwtTokenParser();
        string token = BuildJwt(new
        {
            alg = "none",
            typ = "JWT"
        }, new
        {
            sub = "user-123",
            iss = "https://issuer.example",
            aud = "orders-api",
            scope = "orders.read orders.write",
            roles = new[] { "admin" },
            client_id = "client-123",
            exp = 4_102_444_800,
            nbf = 1_700_000_000,
            iat = 1_600_000_000
        });

        ParsedToken parsed = Assert.Single(parser.Parse(new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {token}"
        }));

        Assert.Equal("Authorization", parsed.HeaderName);
        Assert.Equal("Bearer", parsed.Scheme);
        Assert.Equal(token, parsed.RawToken);
        Assert.False(parsed.JwtExpired);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(4_102_444_800), parsed.JwtExpiry);

        using JsonDocument payload = JsonDocument.Parse(parsed.JwtPayloadJson!);
        Assert.Equal("user-123", payload.RootElement.GetProperty("sub").GetString());
        Assert.NotNull(parsed.Header);
        Assert.Equal("none", parsed.Header.Algorithm);
        Assert.Equal("JWT", parsed.Header.Type);
        Assert.NotNull(parsed.Claims);
        Assert.Equal("user-123", parsed.Claims.Subject);
        Assert.Equal("https://issuer.example", parsed.Claims.Issuer);
        Assert.Equal(["orders-api"], parsed.Claims.Audiences);
        Assert.Equal(["orders.read", "orders.write"], parsed.Claims.Scopes);
        Assert.Equal(["admin"], parsed.Claims.Roles);
        Assert.Equal("client-123", parsed.Claims.ClientId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), parsed.Claims.NotBefore);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_600_000_000), parsed.Claims.IssuedAt);
    }

    [Fact]
    public void Parse_DecodesBasicCredentials_FromAuthorizationHeader()
    {
        var parser = new JwtTokenParser();
        string credentials = Convert.ToBase64String("alice:secret"u8.ToArray());

        ParsedToken parsed = Assert.Single(parser.Parse(new Dictionary<string, string>
        {
            ["Authorization"] = $"Basic {credentials}"
        }));

        Assert.Equal("Basic", parsed.Scheme);
        Assert.Equal("alice", parsed.BasicUsername);
        Assert.Equal("secret", parsed.BasicPassword);
    }

    [Fact]
    public void Parse_IgnoresHeadersWithoutRecognizableTokens()
    {
        var parser = new JwtTokenParser();

        IReadOnlyList<ParsedToken> parsed = parser.Parse(new Dictionary<string, string>
        {
            ["X-Trace"] = "not-a-token"
        });

        Assert.Empty(parsed);
    }

    [Fact]
    public void Parse_IgnoresDatadogTagsHeader()
    {
        var parser = new JwtTokenParser();

        IReadOnlyList<ParsedToken> parsed = parser.Parse(new Dictionary<string, string>
        {
            ["X-Datadog-Tags"] = "_dd.p.tid=6a1d4d8300000000,_dd.p.dm=-3"
        });

        Assert.Empty(parsed);
    }

    [Fact]
    public void Parse_DecryptsJwe_WithRouteKey()
    {
        byte[] routeKey = BuildSymmetricKey(1);
        string token = BuildJwe(routeKey, new
        {
            sub = "route-user",
            aud = "orders-api"
        });
        var parser = new JwtTokenParser();

        ParsedToken parsed = Assert.Single(parser.Parse(
            new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}"
            },
            new AuthExpectation
            {
                Jwe = new JweDecryptionConfig
                {
                    KeyType = JweKeyType.Symmetric,
                    Key = Convert.ToBase64String(routeKey)
                }
            }));

        Assert.True(parsed.IsJwe);
        Assert.Null(parsed.JweDecryptError);
        Assert.NotNull(parsed.JwtPayloadJson);
        Assert.NotNull(parsed.Claims);
        Assert.Equal("route-user", parsed.Claims.Subject);
        Assert.Equal(["orders-api"], parsed.Claims.Audiences);
    }
    
    private static string BuildJwt(object header, object payload) =>
        string.Join('.', Base64Url(header), Base64Url(payload), "");

    private static string BuildJwe(byte[] key, object payload)
    {
        string json = JsonSerializer.Serialize(payload);
        return JWT.Encode(json, key, JweAlgorithm.DIR, JweEncryption.A256GCM);
    }

    private static byte[] BuildSymmetricKey(byte seed)
    {
        byte[] key = new byte[32];
        Array.Fill(key, seed);
        return key;
    }

    private static string Base64Url(object value)
    {
        string json = JsonSerializer.Serialize(value);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
