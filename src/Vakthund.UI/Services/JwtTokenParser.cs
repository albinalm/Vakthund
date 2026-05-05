using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jose;
using Microsoft.Extensions.Options;
using Vakthund.UI.Models;
using Vakthund.UI.Options;
using Base64UrlHelper = Vakthund.UI.Helpers.Base64Url;

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
        {
            return ParseAuthHeader(name, value);
        }

        string rawToken = value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? value["Bearer ".Length..].Trim()
            : value;

        return TryDecodeToken(name, null, rawToken);
    }

    private ParsedToken ParseAuthHeader(string name, string value)
    {
        int spaceIdx = value.IndexOf(' ');
        if (spaceIdx < 0)
        {
            return new ParsedToken { HeaderName = name, Scheme = value };
        }

        string scheme = value[..spaceIdx];
        string rawToken = value[(spaceIdx + 1)..].Trim();

        if (scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return TryDecodeToken(name, scheme, rawToken)
                   ?? new ParsedToken { HeaderName = name, Scheme = scheme, RawToken = rawToken };
        }

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
        {
            return DecodeJwe(name, scheme, token);
        }

        if (partCount == 3)
        {
            (string? headerJson, string? payloadJson, DateTimeOffset? expiry, bool expired, TokenClaimSummary? claims, TokenHeaderSummary? header) = TryParseJwt(token);
            if (headerJson is null && payloadJson is null)
            {
                return null;
            }

            return new ParsedToken
            {
                HeaderName = name,
                Scheme = scheme,
                RawToken = token,
                JwtHeaderJson = headerJson,
                JwtPayloadJson = payloadJson,
                Header = header,
                JwtExpiry = expiry,
                JwtExpired = expired,
                Claims = claims
            };
        }

        return null;
    }

    private ParsedToken DecodeJwe(string name, string? scheme, string token)
    {
        string? headerJson = DecodeBase64UrlJson(token.AsSpan()[..token.IndexOf('.')].ToString());
        string? payloadJson = null;
        string? decryptError = null;
        TokenHeaderSummary? header = TryParseHeaderSummary(headerJson);
        DateTimeOffset? expiry = null;
        TokenClaimSummary? claims = null;
        var expired = false;

        if (_jwe.KeyType.HasValue && !string.IsNullOrEmpty(_jwe.Key))
        {
            try
            {
                object key = BuildKey(_jwe);
                string decrypted = JWT.Decode(token, key);

                if (decrypted.AsSpan().Count('.') == 2)
                {
                    // cty:JWT — decrypted payload is itself a JWT (nested token)
                    (_, payloadJson, expiry, expired, claims, _) = TryParseJwt(decrypted);
                }
                else
                {
                    using JsonDocument doc = JsonDocument.Parse(decrypted);
                    payloadJson = JsonSerializer.Serialize(doc, JsonOptions);
                    claims = ParseClaimSummary(doc.RootElement);

                    if (claims.ExpiresAt.HasValue)
                    {
                        expiry = claims.ExpiresAt.Value;
                        expired = expiry < DateTimeOffset.UtcNow;
                    }
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
            Header = header,
            JwtExpiry = expiry,
            JwtExpired = expired,
            Claims = claims,
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

    private static (string? HeaderJson, string? PayloadJson, DateTimeOffset? Expiry, bool Expired, TokenClaimSummary? Claims, TokenHeaderSummary? Header) TryParseJwt(string token)
    {
        string[] parts = token.Split('.');
        if (parts.Length != 3)
        {
            return (null, null, null, false, null, null);
        }

        string? headerJson = DecodeBase64UrlJson(parts[0]);
        string? payloadJson = DecodeBase64UrlJson(parts[1]);
        TokenHeaderSummary? header = TryParseHeaderSummary(headerJson);
        DateTimeOffset? expiry = null;
        TokenClaimSummary? claims = null;
        bool expired = false;

        if (payloadJson is not null)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payloadJson);
                claims = ParseClaimSummary(doc.RootElement);
                if (claims.ExpiresAt.HasValue)
                {
                    expiry = claims.ExpiresAt.Value;
                    expired = expiry < DateTimeOffset.UtcNow;
                }
            }
            catch
            {
                //Ignored
            }
        }

        return (headerJson, payloadJson, expiry, expired, claims, header);
    }

    private static TokenHeaderSummary? TryParseHeaderSummary(string? headerJson)
    {
        if (headerJson is null)
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(headerJson);
            return new TokenHeaderSummary
            {
                Algorithm = ReadString(doc.RootElement, "alg"),
                KeyId = ReadString(doc.RootElement, "kid"),
                Type = ReadString(doc.RootElement, "typ")
            };
        }
        catch
        {
            return null;
        }
    }

    private static TokenClaimSummary ParseClaimSummary(JsonElement payload)
    {
        return new TokenClaimSummary
        {
            Subject = ReadString(payload, "sub"),
            Issuer = ReadString(payload, "iss"),
            Audiences = ReadStringList(payload, "aud"),
            Scopes = ReadScopes(payload),
            Roles = ReadStringList(payload, "roles"),
            ClientId = ReadString(payload, "client_id"),
            AuthorizedParty = ReadString(payload, "azp"),
            ExpiresAt = ReadUnixTime(payload, "exp"),
            NotBefore = ReadUnixTime(payload, "nbf"),
            IssuedAt = ReadUnixTime(payload, "iat")
        };
    }

    private static IReadOnlyList<string> ReadScopes(JsonElement payload)
    {
        List<string> scopes = [];
        scopes.AddRange(ReadSpaceSeparatedString(payload, "scope"));
        scopes.AddRange(ReadSpaceSeparatedString(payload, "scp"));
        return scopes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ReadSpaceSeparatedString(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out JsonElement property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return ReadArrayValues(property);
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        string? value = property.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyList<string> ReadStringList(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out JsonElement property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return ReadArrayValues(property);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            string? value = property.GetString();
            return string.IsNullOrWhiteSpace(value) ? [] : [value];
        }

        return [];
    }

    private static IReadOnlyList<string> ReadArrayValues(JsonElement array)
    {
        var values = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                values.Add(item.GetString()!);
            }
        }

        return values;
    }

    private static string? ReadString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static DateTimeOffset? ReadUnixTime(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt64(out long unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : null;
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
            string json = Base64UrlHelper.DecodeString(base64Url);
            using JsonDocument doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
