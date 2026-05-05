using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jose;
using Microsoft.Extensions.Options;
using Vakthund.UI.Models;
using Vakthund.UI.Options;

namespace Vakthund.UI.Services;

public class JwtTokenParser(IOptions<VakthundOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly JweOptions _jwe = options.Value.Jwe;

    public IReadOnlyList<ParsedToken> Parse(IReadOnlyDictionary<string, string> headers)
    {
        return headers.Select(h => TryParseHeader(h.Key, h.Value)).OfType<ParsedToken>().ToList();
    }

    private ParsedToken? TryParseHeader(string name, string value)
    {
        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            return ParseAuthHeader(name, value);

        string rawToken = value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? value["Bearer ".Length..].Trim()
            : value;

        return TryDecodeToken(name, null, rawToken);
    }

    private ParsedToken ParseAuthHeader(string name, string value)
    {
        int spaceIdx = value.IndexOf(' ');
        if (spaceIdx < 0)
            return new ParsedToken { HeaderName = name, Scheme = value };

        string scheme = value[..spaceIdx];
        string rawToken = value[(spaceIdx + 1)..].Trim();

        if (scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            return TryDecodeToken(name, scheme, rawToken)
                ?? new ParsedToken { HeaderName = name, Scheme = scheme, RawToken = rawToken };

        if (scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase))
        {
            (string? username, string? password) = TryParseBasic(rawToken);
            return new ParsedToken { HeaderName = name, Scheme = scheme, RawToken = rawToken, BasicUsername = username, BasicPassword = password };
        }

        return new ParsedToken { HeaderName = name, Scheme = scheme, RawToken = rawToken };
    }

    private ParsedToken? TryDecodeToken(string name, string? scheme, string token)
    {
        int partCount = token.AsSpan().Count('.') + 1;

        if (partCount == 5)
            return DecodeJwe(name, scheme, token);

        if (partCount == 3)
        {
            (string? headerJson, string? payloadJson, DateTimeOffset? expiry, bool expired) = TryParseJwt(token);
            if (headerJson is null && payloadJson is null)
                return null;
            return new ParsedToken { HeaderName = name, Scheme = scheme, RawToken = token, JwtHeaderJson = headerJson, JwtPayloadJson = payloadJson, JwtExpiry = expiry, JwtExpired = expired };
        }

        return null;
    }

    private ParsedToken DecodeJwe(string name, string? scheme, string token)
    {
        string? headerJson = DecodeBase64UrlJson(token.AsSpan()[..token.IndexOf('.')].ToString());
        string? payloadJson = null;
        string? decryptError = null;
        DateTimeOffset? expiry = null;
        var expired = false;

        if (_jwe.KeyType.HasValue && !string.IsNullOrEmpty(_jwe.Key))
        {
            try
            {
                object key = BuildKey(_jwe);
                string decrypted = JWT.Decode(token, key);
                using JsonDocument doc = JsonDocument.Parse(decrypted);
                payloadJson = JsonSerializer.Serialize(doc, JsonOptions);

                if (doc.RootElement.TryGetProperty("exp", out JsonElement exp) && exp.TryGetInt64(out long expUnix))
                {
                    expiry = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                    expired = expiry < DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex)
            {
                decryptError = ex.Message;
            }
        }

        return new ParsedToken
        {
            HeaderName = name,
            Scheme = scheme,
            RawToken = token,
            IsJwe = true,
            JwtHeaderJson = headerJson,
            JwtPayloadJson = payloadJson,
            JwtExpiry = expiry,
            JwtExpired = expired,
            JweDecryptError = decryptError
        };
    }

    private static object BuildKey(JweOptions opts) => opts.KeyType switch
    {
        JweKeyType.Rsa => LoadRsaKey(opts.Key!),
        JweKeyType.Ec => LoadEcKey(opts.Key!),
        JweKeyType.Symmetric => Convert.FromBase64String(opts.Key!),
        JweKeyType.Password => opts.Key!,
        _ => throw new InvalidOperationException($"Unknown JWE key type: {opts.KeyType}")
    };

    private static RSA LoadRsaKey(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    private static ECDsa LoadEcKey(string pem)
    {
        var ec = ECDsa.Create();
        ec.ImportFromPem(pem);
        return ec;
    }

    private static (string? HeaderJson, string? PayloadJson, DateTimeOffset? Expiry, bool Expired) TryParseJwt(string token)
    {
        string[] parts = token.Split('.');
        if (parts.Length != 3)
            return (null, null, null, false);

        string? headerJson = DecodeBase64UrlJson(parts[0]);
        string? payloadJson = DecodeBase64UrlJson(parts[1]);
        DateTimeOffset? expiry = null;
        bool expired = false;

        if (payloadJson is not null)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("exp", out JsonElement exp) && exp.TryGetInt64(out long expUnix))
                {
                    expiry = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                    expired = expiry < DateTimeOffset.UtcNow;
                }
            }
            catch
            {
                //Ignored
            }
        }

        return (headerJson, payloadJson, expiry, expired);
    }

    private static (string? Username, string? Password) TryParseBasic(string credentials)
    {
        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(credentials));
            int colonIdx = decoded.IndexOf(':');
            return colonIdx < 0
                ? (decoded, null)
                : (decoded[..colonIdx], decoded[(colonIdx + 1)..]);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? DecodeBase64UrlJson(string base64Url)
    {
        try
        {
            string padded = base64Url.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            byte[] bytes = Convert.FromBase64String(padded);
            string json = Encoding.UTF8.GetString(bytes);
            using JsonDocument doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}