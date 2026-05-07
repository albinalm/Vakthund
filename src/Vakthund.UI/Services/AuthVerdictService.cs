using Vakthund.Shared.Models;
using Vakthund.UI.Models;

namespace Vakthund.UI.Services;

public class AuthVerdictService(JwtSignatureValidator? signatureValidator = null, ProxyRouteMatcher? routeMatcher = null)
{
    private readonly ProxyRouteMatcher _routeMatcher = routeMatcher ?? new ProxyRouteMatcher();

    public AuthVerdict Evaluate(AuditEntry entry, IReadOnlyList<ParsedToken> tokens) =>
        Evaluate(entry, tokens, null, DateTimeOffset.UtcNow);

    public AuthVerdict Evaluate(AuditEntry entry, IReadOnlyList<ParsedToken> tokens, DateTimeOffset now)
        => Evaluate(entry, tokens, null, now);

    public AuthVerdict Evaluate(AuditEntry entry, IReadOnlyList<ParsedToken> tokens, ProxyConfig? config, DateTimeOffset now)
    {
        ProxyRouteInfo? route = _routeMatcher.FindMatchingRoute(config?.Routes, entry.Path);
        return EvaluateWithoutSignature(entry, tokens, route, now);
    }

    public Task<AuthVerdict> EvaluateAsync(AuditEntry entry, IReadOnlyList<ParsedToken> tokens, ProxyConfig? config, CancellationToken ct = default) =>
        EvaluateAsync(entry, tokens, config, DateTimeOffset.UtcNow, ct);

    public async Task<AuthVerdict> EvaluateAsync(AuditEntry entry, IReadOnlyList<ParsedToken> tokens, ProxyConfig? config, DateTimeOffset now, CancellationToken ct = default)
    {
        ProxyRouteInfo? route = _routeMatcher.FindMatchingRoute(config?.Routes, entry.Path);
        AuthVerdict verdict = EvaluateWithoutSignature(entry, tokens, route, now);
        if (verdict.Severity == AuthVerdictSeverity.Error)
        {
            return verdict;
        }

        ParsedToken? bearer = tokens.FirstOrDefault(IsBearerToken);
        if (bearer is null || signatureValidator is null)
        {
            return verdict;
        }

        AuthExpectation expectation = route?.Auth ?? new AuthExpectation();
        JwtSignatureValidationResult signature = await signatureValidator.ValidateAsync(bearer, expectation, ct);
        if (signature.Status == JwtSignatureValidationStatus.Valid)
        {
            return verdict with { Detail = $"{verdict.Detail} {signature.Message} {KeySourceMessage(signature.KeySource)}" };
        }

        if (signature.Status == JwtSignatureValidationStatus.NotConfigured)
        {
            return verdict;
        }

        return SignatureVerdict(signature, route);
    }

    private static AuthVerdict EvaluateWithoutSignature(AuditEntry entry, IReadOnlyList<ParsedToken> tokens, ProxyRouteInfo? route, DateTimeOffset now)
    {
        AuthExpectation? expectation = route?.Auth;
        ParsedToken? bearer = tokens.FirstOrDefault(IsBearerToken);
        bool authFailed = entry.StatusCode is 401 or 403;
        bool routeExpectsAuth = HasAuthExpectation(expectation);

        if (bearer is null)
        {
            return NoBearerVerdict(authFailed, routeExpectsAuth);
        }

        if (IsMalformedBearer(bearer))
        {
            return MalformedBearerVerdict();
        }

        if (bearer.IsJwe && bearer.JwtPayloadJson is null)
        {
            return JweVerdict(bearer);
        }

        if (bearer.Claims?.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            return ExpiredVerdict(expiresAt);
        }

        if (bearer.Claims?.NotBefore is { } notBefore && notBefore > now)
        {
            return NotYetValidVerdict(notBefore);
        }

        if (routeExpectsAuth)
        {
            IReadOnlyList<string> mismatches = FindExpectationMismatches(expectation!, bearer);
            if (mismatches.Count > 0)
            {
                return ExpectationMismatchVerdict(route!, mismatches);
            }
        }

        if (entry.StatusCode == 401)
        {
            return BackendRejectedVerdict("Backend returned 401.", BackendRejectedDetail(routeExpectsAuth, "authentication"));
        }

        if (entry.StatusCode == 403)
        {
            return BackendRejectedVerdict("Backend returned 403.", BackendRejectedDetail(routeExpectsAuth, "authorization"));
        }

        return routeExpectsAuth ? AuthMatchesVerdict(route!) : UsableTokenVerdict();
    }

    private static bool IsBearerToken(ParsedToken token) =>
        token.Scheme?.Equals("Bearer", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsMalformedBearer(ParsedToken token) =>
        !token.IsJwe && token.JwtPayloadJson is null;

    private static AuthVerdict NoBearerVerdict(bool authFailed, bool routeExpectsAuth)
    {
        if (authFailed || routeExpectsAuth)
        {
            return new AuthVerdict
            {
                Severity = AuthVerdictSeverity.Error,
                Title = "No bearer token found.",
                Detail = routeExpectsAuth
                    ? "The matched route has auth expectations, but Vakthund did not find a bearer token in the request headers."
                    : "The backend returned an auth failure, but Vakthund did not find a bearer token in the request headers.",
                Hints = ["Check whether the client sends the Authorization header.", "If the token is stored in a cookie, query parameter, or body field, add token-source support next."]
            };
        }

        return new AuthVerdict
        {
            Severity = AuthVerdictSeverity.Info,
            Title = "No bearer token found.",
            Detail = "Vakthund did not find a bearer token in the request headers."
        };
    }

    private static AuthVerdict MalformedBearerVerdict() =>
        new()
        {
            Severity = AuthVerdictSeverity.Error,
            Title = "Bearer token could not be decoded.",
            Detail = "The Authorization header contains a bearer value, but it is not a readable JWT or JWE.",
            Hints = ["Check for a truncated token.", "Check that the client is not sending an opaque reference token where a JWT was expected."]
        };

    private static AuthVerdict JweVerdict(ParsedToken token)
    {
        if (token.JweDecryptError is not null)
        {
            return new AuthVerdict
            {
                Severity = AuthVerdictSeverity.Warning,
                Title = "JWE decryption failed.",
                Detail = token.JweDecryptError,
                Hints = ["Check the route auth.jwe key type and key value.", "Check that the token was encrypted for the configured key."]
            };
        }

        return new AuthVerdict
        {
            Severity = AuthVerdictSeverity.Warning,
            Title = "JWE payload is encrypted.",
            Detail = "Vakthund can read the protected header, but no decrypted payload is available.",
            Hints = ["Configure route auth.jwe in routes.yaml to inspect encrypted token claims."]
        };
    }

    private static AuthVerdict ExpiredVerdict(DateTimeOffset expiresAt) =>
        new()
        {
            Severity = AuthVerdictSeverity.Error,
            Title = "Token is expired.",
            Detail = $"The token expired at {expiresAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}.",
            Hints = ["Refresh the token before sending the request.", "Check clock skew between the client, identity provider, and API."]
        };

    private static AuthVerdict NotYetValidVerdict(DateTimeOffset notBefore) =>
        new()
        {
            Severity = AuthVerdictSeverity.Error,
            Title = "Token is not valid yet.",
            Detail = $"The token is not valid before {notBefore.ToLocalTime():yyyy-MM-dd HH:mm:ss}.",
            Hints = ["Check clock skew between the client, identity provider, and API."]
        };

    private static AuthVerdict BackendRejectedVerdict(string title, string detail) =>
        new()
        {
            Severity = AuthVerdictSeverity.Warning,
            Title = title,
            Detail = detail,
            Hints = ["Next likely checks: issuer, audience, scopes, roles, signing key, or server-side authorization policy."]
        };

    private static AuthVerdict ExpectationMismatchVerdict(ProxyRouteInfo route, IReadOnlyList<string> mismatches) =>
        new()
        {
            Severity = AuthVerdictSeverity.Error,
            Title = "Token does not match route auth expectations.",
            Detail = $"Matched route {route.Path}. {string.Join(" ", mismatches)}",
            Hints = ["Fix the client token request or update the route auth expectations if the API contract changed."]
        };

    private static AuthVerdict SignatureVerdict(JwtSignatureValidationResult signature, ProxyRouteInfo? route)
    {
        AuthVerdictSeverity severity = signature.Status is JwtSignatureValidationStatus.FetchFailed or JwtSignatureValidationStatus.UnsupportedAlgorithm
            ? AuthVerdictSeverity.Warning
            : AuthVerdictSeverity.Error;
        string routeText = route is null ? "" : $"Matched route {route.Path}. ";

        return new AuthVerdict
        {
            Severity = severity,
            Title = "Token signature could not be trusted.",
            Detail = $"{routeText}{signature.Message} {KeySourceMessage(signature.KeySource)}",
            Hints = ["Check the token kid and signing algorithm.", "Check the route jwksUrl/openIdConfigurationUrl or issuer metadata."]
        };
    }

    private static AuthVerdict AuthMatchesVerdict(ProxyRouteInfo route) =>
        new()
        {
            Severity = AuthVerdictSeverity.Info,
            Title = "Token matches configured route expectations.",
            Detail = $"Matched route {route.Path}. Issuer, audience, scopes, roles, and local time claims match the configured checks. Signature validation is only applied when JWKS or OIDC metadata is available."
        };

    private static AuthVerdict UsableTokenVerdict() =>
        new()
        {
            Severity = AuthVerdictSeverity.Info,
            Title = "Bearer token decoded.",
            Detail = "The token decoded and its local time claims look usable. Signature, issuer, audience, scope, and role validation are not enabled yet."
        };

    private static string BackendRejectedDetail(bool routeExpectsAuth, string failureType) =>
        routeExpectsAuth
            ? $"The token matches configured route expectations, but the backend still rejected {failureType}. Check signing key validation, custom server policy, or target application diagnostics."
            : $"The token decoded and its time claims look usable, but the backend still rejected {failureType}.";

    private static IReadOnlyList<string> FindExpectationMismatches(AuthExpectation expectation, ParsedToken bearer)
    {
        var mismatches = new List<string>();
        TokenClaimSummary? claims = bearer.Claims;

        if (claims is null)
        {
            return ["Token claims could not be parsed."];
        }

        AddIssuerMismatch(mismatches, expectation, claims);
        AddAudienceMismatch(mismatches, expectation, claims);
        AddMissingValues(mismatches, "scope", expectation.Scopes ?? [], claims.Scopes);
        AddMissingValues(mismatches, "role", expectation.Roles ?? [], claims.Roles);

        return mismatches;
    }

    private static void AddIssuerMismatch(List<string> mismatches, AuthExpectation expectation, TokenClaimSummary claims)
    {
        if (string.IsNullOrWhiteSpace(expectation.Issuer))
        {
            return;
        }

        if (!string.Equals(expectation.Issuer, claims.Issuer, StringComparison.Ordinal))
        {
            mismatches.Add($"Expected issuer '{expectation.Issuer}', token has '{claims.Issuer ?? "missing"}'.");
        }
    }

    private static void AddAudienceMismatch(List<string> mismatches, AuthExpectation expectation, TokenClaimSummary claims)
    {
        IReadOnlyList<string> expectedAudiences = ExpectedAudiences(expectation);
        if (expectedAudiences.Count == 0)
        {
            return;
        }

        bool matches = expectedAudiences.Any(expected => claims.Audiences.Contains(expected, StringComparer.Ordinal));
        if (!matches)
        {
            mismatches.Add($"Expected audience {FormatExpected(expectedAudiences)}, token has {FormatActual(claims.Audiences)}.");
        }
    }

    private static void AddMissingValues(List<string> mismatches, string label, IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        string[] missing = expected
            .Where(value => !actual.Contains(value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missing.Length > 0)
        {
            mismatches.Add($"Missing {label}{(missing.Length == 1 ? "" : "s")} {FormatExpected(missing)}.");
        }
    }

    private static IReadOnlyList<string> ExpectedAudiences(AuthExpectation expectation)
    {
        var audiences = new List<string>();
        if (!string.IsNullOrWhiteSpace(expectation.Audience))
        {
            audiences.Add(expectation.Audience);
        }

        audiences.AddRange((expectation.Audiences ?? []).Where(audience => !string.IsNullOrWhiteSpace(audience)));
        return audiences.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool HasAuthExpectation(AuthExpectation? expectation) =>
        expectation is not null &&
        (!string.IsNullOrWhiteSpace(expectation.Issuer) ||
         !string.IsNullOrWhiteSpace(expectation.Audience) ||
         expectation.Audiences?.Count > 0 ||
         expectation.Scopes?.Count > 0 ||
         expectation.Roles?.Count > 0 ||
         !string.IsNullOrWhiteSpace(expectation.OpenIdConfigurationUrl) ||
         !string.IsNullOrWhiteSpace(expectation.JwksUrl) ||
         IsJweConfigured(expectation.Jwe));

    private static bool IsJweConfigured(JweDecryptionConfig? jwe) =>
        jwe is not null && (jwe.KeyType.HasValue || !string.IsNullOrWhiteSpace(jwe.Key));

    private static string FormatExpected(IReadOnlyList<string> values) =>
        string.Join(", ", values.Select(value => $"'{value}'"));

    private static string FormatActual(IReadOnlyList<string> values) =>
        values.Count == 0 ? "no audience" : FormatExpected(values);

    private static string KeySourceMessage(JwtSigningKeySource source) => source switch
    {
        JwtSigningKeySource.ConfiguredJwks => "JWKS source: configured jwksUrl.",
        JwtSigningKeySource.ConfiguredOpenIdMetadata => "JWKS source: configured OIDC metadata.",
        JwtSigningKeySource.ConfiguredIssuerMetadata => "JWKS source: metadata from configured issuer.",
        JwtSigningKeySource.InferredTokenIssuerMetadata => "JWKS source: inferred from token issuer.",
        _ => ""
    };
}
