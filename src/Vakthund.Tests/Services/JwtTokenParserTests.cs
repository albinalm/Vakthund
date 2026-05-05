using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vakthund.UI.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Services;

public class JwtTokenParserTests
{
    [Fact]
    public void Parse_DecodesBearerJwt_FromAuthorizationHeader()
    {
        var parser = new JwtTokenParser(Options.Create(new VakthundOptions()));
        string token = BuildJwt(new { alg = "none", typ = "JWT" }, new { sub = "user-123", exp = 4_102_444_800 });

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
    }

    [Fact]
    public void Parse_DecodesBasicCredentials_FromAuthorizationHeader()
    {
        var parser = new JwtTokenParser(Options.Create(new VakthundOptions()));
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
        var parser = new JwtTokenParser(Options.Create(new VakthundOptions()));

        IReadOnlyList<ParsedToken> parsed = parser.Parse(new Dictionary<string, string>
        {
            ["X-Trace"] = "not-a-token"
        });

        Assert.Empty(parsed);
    }

    private static string BuildJwt(object header, object payload) =>
        string.Join('.', Base64Url(header), Base64Url(payload), "");

    private static string Base64Url(object value)
    {
        string json = JsonSerializer.Serialize(value);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
