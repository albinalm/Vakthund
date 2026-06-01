# Configuration

Vakthund can be configured with environment variables or `appsettings.json`. In Docker, environment variables are usually the most direct option.

## Proxy Settings

These settings belong to `Vakthund.Proxy`.

| Environment variable | App setting | Default | Purpose |
| --- | --- | --- | --- |
| `TARGET` | `Proxy:TargetUrl` | `https://localhost:5001` in appsettings | Single upstream URL used when no routes file is loaded. |
| `ROUTES_FILE` | `Proxy:RoutesFile` | `routes.local.yaml` beside the proxy when that file exists; `/etc/vakthund/routes.yaml` when that file exists | YAML routes file path. |
| `MAX_BODY_BYTES` | `Proxy:MaxBodyBytes` | `65536` | Maximum request body size to capture. Use `0` to disable request body capture. |
| `MAX_RESPONSE_BODY_BYTES` | `Proxy:MaxResponseBodyBytes` | `65536` in code | Maximum response body size to capture. Use `0` to disable response body capture. |
| `MAX_QUEUED_ENTRIES` | `Proxy:MaxQueuedEntries` | `10000` | Proxy audit queue capacity. Oldest entries are dropped when full. |
| `Proxy__ConnectTimeoutSeconds` | `Proxy:ConnectTimeoutSeconds` | `30` | TCP connect timeout for upstream destinations. Use `0` to disable connect timeout. |

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
| `MAX_AUDIT_ENTRIES` | `UI:MaxStoredAuditEntries` | `50000` | Maximum entries to retain. Use `0` for no count limit. |
| `STORAGE_MODE` | `UI:StorageMode` | `Memory` | `Memory` or `Disk`. Use `Disk` to persist requests across restarts. |
| `STORAGE_PATH` | `UI:StoragePath` | `/app/data/audit.db` in Docker | Path to the SQLite database file. Relative paths are resolved from the app directory. |
| `RETENTION` | `UI:Retention` | empty | Maximum age of retained requests. Accepts `m`, `h`, or `d` suffixes — for example `30m`, `24h`, `10d`. Entries older than this are discarded on the next incoming request. |

## Routes File Format

The routes file is YAML:

```yaml
routes:
  - path: /api/**
    target: http://host.docker.internal:5000
    hosts:
      - api.example.test
      - "*.api.example.test"
  - path: /identity/**
    target: http://host.docker.internal:5001
  - path: /**
    target: http://host.docker.internal:3000
```

The root object supports:

- `routes`: route definitions for incoming paths.
- `auths`: optional named auth contracts that routes can reference.

When the upstream service uses a different path than the downstream path exposed by Vakthund, add `to` on that route:

```yaml
routes:
  - path: /foobar/**
    target: http://host.docker.internal:5001
    to: /api/foobar
```

With this route, `/foobar/items/123` is forwarded to `http://host.docker.internal:5001/api/foobar/items/123`.

Each route has:

- `path`: the incoming path pattern to match.
- `hosts`: optional host filters for the route. Values can be exact hosts such as `api.example.test`, wildcard subdomains such as `*.api.example.test`, or `*`.
- `target`: the upstream base URL.
- `to`: optional upstream path prefix. For catch-all routes, the remaining request path is appended to `to`. For exact routes, `to` replaces the request path.
- `timeout`: optional per-route proxy timeout. Use milliseconds, `hh:mm:ss`, or a value ending in `ms`, `s`, `m`, or `h`. Vakthund applies this to both YARP's route timeout and forwarder activity timeout. Use `disable` to disable both.
- `priority`: optional integer route priority. Higher numbers are matched before lower numbers. The default is `0`.
- `ips`: optional client IP whitelist for the route. Values can be exact IPs, CIDR ranges, or trailing IPv4 wildcards such as `203.0.*` and `203.0.113.*`.
- `auth`: optional route auth contract. This can be an inline auth object or the name of an auth contract defined under `auths`. By default it is used by the UI to explain auth failures. Set `auth.enforce: true` to make the proxy enforce the same contract before forwarding.

Vakthund supports `/**` and paths ending in `/**` as catch-all patterns. These are converted to YARP catch-all routes internally. A path such as `/logs/**` matches `/logs`, `/logs/`, and deeper paths such as `/logs/archive/2026`.

Use specific paths for individual services, and add a broad `/**` fallback only when you want unmatched traffic to go somewhere.

To restrict a route by host or subdomain, add `hosts`:

```yaml
routes:
  - path: /api/**
    target: http://host.docker.internal:5000
    hosts:
      - api.example.test
      - "*.api.example.test"
```

Requests whose `Host` header does not match one of the configured hosts do not match that route. This is useful when several services share the same path shape but are separated by subdomain.

For long-running APIs, configure `timeout` on the specific route:

```yaml
routes:
  - path: /api/generate
    target: http://host.docker.internal:5000
    timeout: 1h
```

The same timeout can also be written as `3600000`, `3600s`, `60m`, or `01:00:00`.
Without this setting, YARP's forwarder activity timeout defaults to 100 seconds while waiting for response headers or other request/response activity. Route `timeout` does not control the TCP connect timeout; use `Proxy:ConnectTimeoutSeconds` for that.

When multiple routes can match the same request, Vakthund uses the highest `priority` first. Routes with the same priority fall back to path and host specificity. This is useful for public exceptions in front of an authenticated catch-all route:

```yaml
routes:
  - path: /identity/v1/external-id
    hosts:
      - api.example.test
    target: http://api
    priority: 10

  - path: /**
    hosts:
      - api.example.test
    target: http://api
    auth: api-auth
```

To restrict a route by client IP, add `ips`:

```yaml
routes:
  - path: /api/generate
    target: http://host.docker.internal:5000
    timeout: 1h
    ips:
      - 203.0.113.*
      - 10.0.0.0/8
```

Requests outside the whitelist are rejected by the proxy before forwarding. Vakthund resolves the client IP from `X-Forwarded-For`, then `X-Real-IP`, then the direct remote address. When running behind Nginx, keep setting those headers at the trusted edge proxy.

When no explicit routes file is configured, `Vakthund.Proxy` automatically checks for `routes.local.yaml` beside the running proxy. During local source runs this is `src/Vakthund.Proxy/routes.local.yaml`; for a published non-Docker app it is a sidecar file next to the published proxy. The source local file is ignored by Git so each developer can keep machine-specific targets and auth expectations locally. Use `src/Vakthund.Proxy/routes.local.example.yaml` as the committed shape.

Explicit route file configuration still wins. Relative paths in `Proxy:RoutesFile` or `ROUTES_FILE` are resolved from the proxy content root.

## Route Auth

Routes can declare the auth contract the target service expects. Without `enforce: true`, this is inspection context only:

```yaml
routes:
  - path: /api/orders/**
    target: http://host.docker.internal:5000
    auth:
      enforce: false
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

If multiple routes use the same auth contract, define it once under `auths` and reference it by name from each route:

```yaml
auths:
  - name: orders-auth
    enforce: true
    issuer: https://login.example.com
    audience: orders-api
    scopes:
      - orders.read
    jwksUrl: https://login.example.com/.well-known/jwks.json
routes:
  - path: /api/orders/**
    target: http://host.docker.internal:5000
    auth: orders-auth
  - path: /api/order-events/**
    target: http://host.docker.internal:5002
    auth: orders-auth
```

Auth names must be non-empty and unique. A route that references an unknown auth name fails startup instead of silently running without auth context.

The UI compares decoded bearer tokens against the matched route and reports subject, issuer, audience, scope, role, expiry, and not-before mismatches in the auth verdict.

Set `enforce: true` to authenticate and authorize at the proxy before traffic reaches the destination:

```yaml
routes:
  - path: /api/orders/**
    target: http://host.docker.internal:5000
    auth:
      enforce: true
      subject: user-123
      issuer: https://login.example.com
      audience: orders-api
      scopes:
        - orders.read
      roles:
        - admin
      jwksUrl: https://login.example.com/.well-known/jwks.json
```

When `enforce` is true, Vakthund configures a YARP authorization policy for the route. Missing, malformed, expired, unsigned, or untrusted bearer JWTs are rejected before forwarding. Subject, scope, and role mismatches are rejected by the route policy.

Use `subject` to require an exact JWT `sub` value:

```yaml
auth:
  subject: f5f9a3a3-dd26-4cc0-8720-51a609d9cf66
```

Enforced auth requires a signing key source. Configure one of:

- `auth.jwksUrl`.
- `auth.openIdConfigurationUrl`.
- `auth.issuer` as an absolute HTTP or HTTPS OIDC issuer URL.

For signature validation, configure either `jwksUrl` directly or `openIdConfigurationUrl` for OIDC discovery:

```yaml
routes:
  - path: /api/orders/**
    target: http://host.docker.internal:5000
    auth:
      enforce: true
      issuer: https://login.example.com
      audience: orders-api
      openIdConfigurationUrl: https://login.example.com/.well-known/openid-configuration
```

If `issuer` is an absolute URL and no key endpoint is set, the proxy and UI try `<issuer>/.well-known/openid-configuration`.
If no key endpoint or route issuer is configured, the UI may also try OIDC discovery from the token's `iss` claim. Token-derived metadata is used only for signature validation enrichment; configured route expectations remain the source of truth for subject, issuer, audience, scope, and role checks.

Signing keys are resolved in this order:

1. `auth.jwksUrl`.
2. `auth.openIdConfigurationUrl`.
3. OIDC metadata derived from `auth.issuer`.
4. OIDC metadata derived from the token `iss` claim.

The auth verdict labels the signing key source so inferred metadata is visible in the UI. Proxy enforcement does not infer metadata from the token issuer; `enforce: true` routes must declare their signing key source through route config.

`auth.jwe` is optional route-level JWE decryption config. It lets the UI decrypt encrypted bearer tokens for the matched route so the claims can be inspected and compared with the route auth contract.

`auth.enforce` decides whether the route auth contract is only inspection context or is also enforced by the proxy. JWE decryption is still for inspection of encrypted tokens; proxy enforcement validates bearer JWT signatures with `jwksUrl`, `openIdConfigurationUrl`, or issuer-based OIDC metadata.

Supported JWE key types are `Rsa`, `Ec`, `Symmetric`, and `Password`. `Rsa` and `Ec` expect PEM private keys, `Symmetric` expects base64-encoded key bytes, and `Password` expects a plain password string.

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

JWE decryption is configured in `routes.yaml` on the route that receives the encrypted bearer token. This keeps decryption keys scoped to the route that needs them.

```yaml
routes:
  - path: /api/orders/**
    target: http://host.docker.internal:5000
    auth:
      enforce: false
      issuer: https://login.example.com
      audience: orders-api
      jwe:
        keyType: Symmetric
        key: base64-encoded-key-bytes
```

```yaml
routes:
  - path: /api/orders/**
    target: http://host.docker.internal:5000
    auth:
      enforce: true
      issuer: https://login.example.com
      audience: orders-api
      jwksUrl: https://login.example.com/.well-known/jwks.json
      jwe:
        keyType: Rsa
        key: |
          -----BEGIN PRIVATE KEY-----
          ...
          -----END PRIVATE KEY-----
```

Use `enforce: false` when the route auth block should only power UI inspection and auth verdicts. Use `enforce: true` when the proxy should reject unauthenticated or unauthorized requests before forwarding.

Supported `auth.jwe.keyType` values:

- `Rsa`: PEM RSA private key.
- `Ec`: PEM EC private key.
- `Symmetric`: base64-encoded symmetric key bytes.
- `Password`: plain password string.

Only configure JWE keys in trusted local environments. The UI process can use route `auth.jwe` to decrypt captured tokens for display. For local development, prefer putting route-specific JWE keys in `src/Vakthund.Proxy/routes.local.yaml`. That file is ignored by Git.

## Capture Limits

Body capture is intentionally bounded.

Set a low limit when testing high-volume or large-payload traffic. Set the value to `0` when bodies should not be captured at all.

Request and response bodies can contain credentials, tokens, personal data, or business data. Treat the UI as sensitive while it is running.

## Storage

By default the UI keeps captured requests in memory. Everything is lost when the container restarts. Switch to disk storage to persist requests and dashboard metrics across restarts.

### Enabling Disk Storage

Set `STORAGE_MODE=Disk` and mount a volume at `/app/data`:

```yaml
services:
  ui:
    environment:
      STORAGE_MODE: "Disk"
    volumes:
      - ui-data:/app/data

volumes:
  ui-data:
```

The UI writes a SQLite database to `/app/data/audit.db` by default. The volume keeps the file alive across container restarts and image rebuilds.

To use a different path:

```yaml
environment:
  STORAGE_MODE: "Disk"
  STORAGE_PATH: "/mnt/storage/audit.db"
volumes:
  - ui-data:/mnt/storage
```

### Retention

By default, entries are only evicted when the count cap is reached (`MAX_AUDIT_ENTRIES`). Set `RETENTION` to also discard entries older than a given age:

```yaml
environment:
  STORAGE_MODE: "Disk"
  RETENTION: "7d"
  MAX_AUDIT_ENTRIES: "0"
```

`MAX_AUDIT_ENTRIES=0` disables the UI's request count cap so retention is the sole eviction policy for captured request detail. Eviction runs on the next incoming request after an entry ages out.

For truly cap-free capture, also set `MAX_QUEUED_ENTRIES=0` on the proxy. The proxy drops the oldest queued entries when its buffer fills up, so entries can be lost before they ever reach the UI regardless of storage settings.

Supported suffixes:

| Suffix | Unit |
| --- | --- |
| `m` | minutes |
| `h` | hours |
| `d` | days |

Retention and the count cap are independent for request detail. Both apply — whichever removes an entry first wins.

Dashboard aggregate metrics are bounded by time, not by `MAX_AUDIT_ENTRIES`. When `RETENTION` is set, disk-backed metric buckets use the same window. When `RETENTION` is empty, metric buckets keep the dashboard's rolling history window instead of growing forever.

### What Persists

In disk mode the UI stores:

- All captured requests and their full detail (headers, bodies, status codes, timings).
- The matched route configuration at the time of capture. Auth analysis in request detail remains accurate even after routes are changed or removed.
- Dashboard aggregate metric buckets, so timing, rate, error, and audit-loss history survives UI restarts even when request rows have been manually deleted.

Stored request count and status-code distribution are calculated from the current request rows. Manual deletion updates those request-row statistics immediately.

Aggregate metrics such as average response time, average target time, request rate history, error rate, and audit loss are stored separately from request rows. Manual deletion does not remove their historical contribution, but `RETENTION` still trims old aggregate metric buckets. Raw request detail is subject to both the count cap and retention.

### Memory Mode

In memory mode (`STORAGE_MODE=Memory`, the default) all data is lost on restart. This is fine for short-lived debugging sessions. Switch to disk mode when you need data to survive container restarts or want a longer retention window without running out of memory.
