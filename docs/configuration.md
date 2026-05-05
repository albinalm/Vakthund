# Configuration

Vakthund can be configured with environment variables or `appsettings.json`. In Docker, environment variables are usually the most direct option.

## Proxy Settings

These settings belong to `Vakthund.Proxy`.

| Environment variable | App setting | Default | Purpose |
| --- | --- | --- | --- |
| `TARGET` | `Proxy:TargetUrl` | `https://localhost:5001` in appsettings | Single upstream URL used when no routes file is loaded. |
| `ROUTES_FILE` | `Vakthund:RoutesFile` | `/etc/vakthund/routes.yaml` when that file exists | YAML routes file path. |
| `MAX_BODY_BYTES` | `Vakthund:MaxBodyBytes` | `65536` | Maximum request body size to capture. Use `0` to disable request body capture. |
| `MAX_RESPONSE_BODY_BYTES` | `Vakthund:MaxResponseBodyBytes` | `65536` in code | Maximum response body size to capture. Use `0` to disable response body capture. |
| `MAX_QUEUED_ENTRIES` | `Vakthund:MaxQueuedEntries` | `10000` | Proxy audit queue capacity. Oldest entries are dropped when full. |

The proxy listens on:

| App setting | Default | Purpose |
| --- | --- | --- |
| `Proxy:Urls` | `http://*:8080` | Public proxy listener. |
| `Proxy:HostPatterns` | `*:8080` | Host patterns accepted by the YARP proxy route. |
| `Management:Port` | `8081` | Management port. |
| `Management:Url` | `http://*:8081` | Management listener URL. |

## UI Settings

These settings belong to `Vakthund.UI`.

| Environment variable | App setting | Default | Purpose |
| --- | --- | --- | --- |
| `HUB` | `Proxy:AuditHubUrl` | `http://proxy:8081/connect/audit` | SignalR hub URL exposed by the proxy management port. |
| `MAX_AUDIT_ENTRIES` | `Vakthund:MaxAuditEntries` | `50000` | Maximum entries retained by the UI in memory. |
| `JWE_KEY_TYPE` | `Vakthund:Jwe:KeyType` | empty | JWE key type. Valid values are `Rsa`, `Ec`, `Symmetric`, and `Password`. |
| `JWE_KEY` | `Vakthund:Jwe:Key` | empty | JWE decryption key. |

## Routes File Format

The routes file is YAML:

```yaml
routes:
  - path: /api/**
    target: http://host.docker.internal:5000
  - path: /identity/**
    target: http://host.docker.internal:5001
  - path: /**
    target: http://host.docker.internal:3000
```

Each route has:

- `path`: the incoming path pattern to match.
- `target`: the upstream base URL.
- `auth`: optional expectations used by the UI to explain auth failures.

Vakthund supports `/**` and paths ending in `/**` as catch-all patterns. These are converted to YARP catch-all routes internally.

Use specific paths for individual services, and add a broad `/**` fallback only when you want unmatched traffic to go somewhere.

## Route Auth Expectations

Routes can declare the auth contract the target service expects:

```yaml
routes:
  - path: /api/orders/**
    target: http://host.docker.internal:5000
    auth:
      issuer: https://login.example.com
      audience: orders-api
      scopes:
        - orders.read
      roles:
        - admin
```

The UI compares decoded bearer tokens against the matched route and reports issuer, audience, scope, role, expiry, and not-before mismatches in the auth verdict.

For signature validation, configure either `jwksUrl` directly or `openIdConfigurationUrl` for OIDC discovery:

```yaml
routes:
  - path: /api/orders/**
    target: http://host.docker.internal:5000
    auth:
      issuer: https://login.example.com
      audience: orders-api
      openIdConfigurationUrl: https://login.example.com/.well-known/openid-configuration
```

If `issuer` is an absolute URL and no key endpoint is set, the UI tries `<issuer>/.well-known/openid-configuration`.
If no key endpoint or route issuer is configured, the UI may also try OIDC discovery from the token's `iss` claim. Token-derived metadata is used only for signature validation enrichment; configured route expectations remain the source of truth for issuer, audience, scope, and role checks.

Signing keys are resolved in this order:

1. `auth.jwksUrl`.
2. `auth.openIdConfigurationUrl`.
3. OIDC metadata derived from `auth.issuer`.
4. OIDC metadata derived from the token `iss` claim.

The auth verdict labels the signing key source so inferred metadata is visible in the UI.

## Single Target Versus Routes File

Use `TARGET` when all traffic should go to one upstream service:

```yaml
environment:
  TARGET: "http://host.docker.internal:5000"
```

Use `ROUTES_FILE` when traffic should be split by path:

```yaml
environment:
  ROUTES_FILE: "/etc/vakthund/routes.yaml"
volumes:
  - ./routes.yaml:/etc/vakthund/routes.yaml:ro
```

If a routes file exists and contains routes, it takes priority over `TARGET`.

## JWE Decryption

Vakthund can inspect encrypted JWE payloads when the UI has a key.

Examples:

```yaml
environment:
  JWE_KEY_TYPE: "Symmetric"
  JWE_KEY: "base64-encoded-key-bytes"
```

```yaml
environment:
  JWE_KEY_TYPE: "Rsa"
  JWE_KEY: |
    -----BEGIN PRIVATE KEY-----
    ...
    -----END PRIVATE KEY-----
```

Supported key types:

- `Rsa`: PEM RSA private key.
- `Ec`: PEM EC private key.
- `Symmetric`: base64-encoded symmetric key bytes.
- `Password`: plain password string.

Only configure JWE keys in trusted local environments. The UI process can use the key to decrypt captured tokens.

## Capture Limits

Body capture is intentionally bounded.

Set a low limit when testing high-volume or large-payload traffic. Set the value to `0` when bodies should not be captured at all.

Request and response bodies can contain credentials, tokens, personal data, or business data. Treat the UI as sensitive while it is running.
