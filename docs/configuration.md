# Configuration

Vakthund can be configured with environment variables or `appsettings.json`. In Docker, environment variables are usually the most direct option.

## Proxy Settings

These settings belong to `Vakthund.Proxy`.

| Environment variable | App setting | Default | Purpose |
| --- | --- | --- | --- |
| `TARGET` | `Proxy:TargetUrl` | `https://localhost:5001` in appsettings | Single upstream URL used when no routes file is loaded. |
| `ROUTES_FILE` | `Proxy:RoutesFile` | `routes.local.yaml` in development when that file exists; `/etc/vakthund/routes.yaml` when that file exists | YAML routes file path. |
| `MAX_BODY_BYTES` | `Proxy:MaxBodyBytes` | `65536` | Maximum request body size to capture. Use `0` to disable request body capture. |
| `MAX_RESPONSE_BODY_BYTES` | `Proxy:MaxResponseBodyBytes` | `65536` in code | Maximum response body size to capture. Use `0` to disable response body capture. |
| `MAX_QUEUED_ENTRIES` | `Proxy:MaxQueuedEntries` | `10000` | Proxy audit queue capacity. Oldest entries are dropped when full. |

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
| `MAX_AUDIT_ENTRIES` | `UI:MaxAuditEntries` | `50000` | Maximum entries retained by the UI in memory. |
| `JWE_KEY_TYPE` | `UI:JweFallback:KeyType` | empty | Global fallback JWE key type. Valid values are `Rsa`, `Ec`, `Symmetric`, and `Password`. Prefer route `auth.jwe` for route-specific keys. |
| `JWE_KEY` | `UI:JweFallback:Key` | empty | Global fallback JWE decryption key. Prefer route `auth.jwe` for route-specific keys. |

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

During local debug, `Vakthund.Proxy` automatically checks for `routes.local.yaml` in the proxy project directory when no explicit routes file is configured. The file is ignored by Git so each developer can keep machine-specific targets and auth expectations locally. Use `src/Vakthund.Proxy/routes.local.example.yaml` as the committed shape.

Explicit route file configuration still wins. Relative paths in `Proxy:RoutesFile` or `ROUTES_FILE` are resolved from the proxy content root.

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
      jwe:
        keyType: Rsa
        key: |
          -----BEGIN PRIVATE KEY-----
          ...
          -----END PRIVATE KEY-----
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

`auth.jwe` is optional route-level JWE decryption config. It is used only to decrypt encrypted bearer tokens so Vakthund can inspect the claims. It is not used for signature validation; signatures are still checked with `jwksUrl` or OIDC metadata.

Supported JWE key types are `Rsa`, `Ec`, `Symmetric`, and `Password`. `Rsa` and `Ec` expect PEM private keys, `Symmetric` expects base64-encoded key bytes, and `Password` expects a plain password string. Route-level `auth.jwe` takes precedence over the global `UI:JweFallback`.

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

Vakthund can inspect encrypted JWE payloads when the matched route has `auth.jwe` configured, or when the UI has a global fallback key.

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

For local development, prefer putting route-specific JWE keys in `src/Vakthund.Proxy/routes.local.yaml`. That file is ignored by Git.

## Capture Limits

Body capture is intentionally bounded.

Set a low limit when testing high-volume or large-payload traffic. Set the value to `0` when bodies should not be captured at all.

Request and response bodies can contain credentials, tokens, personal data, or business data. Treat the UI as sensitive while it is running.
