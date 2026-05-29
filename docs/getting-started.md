# Getting Started

The easiest way to run Vakthund is Docker Compose. Run the proxy and the UI together, point your client at the proxy, and let the proxy forward traffic to the real target.

## Recommended Compose Setup

Create a `docker-compose.yml` next to your own development stack:

```yaml
services:
  proxy:
    image: ghcr.io/albinalm/vakthund-proxy:latest
    ports:
      - "8080:8080"
      - "8081:8081"
    environment:
      TARGET: "http://host.docker.internal:5000"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8081/"]
      interval: 5s
      timeout: 3s
      retries: 10
      start_period: 10s

  ui:
    image: ghcr.io/albinalm/vakthund-ui:latest
    user: root
    ports:
      - "8082:8080"
    environment:
      HUB: "http://proxy:8081/connect/audit"
    volumes:
      - ui-keys:/home/app/.aspnet/DataProtection-Keys
    depends_on:
      proxy:
        condition: service_healthy

volumes:
  ui-keys:
```

Start it:

```bash
docker compose up
```

If you are working on Vakthund itself and want to run from source, replace the `image:` lines with `build:` blocks pointing at the Dockerfiles:

```yaml
  proxy:
    build:
      context: ./src
      dockerfile: Vakthund.Proxy/Dockerfile
```

Open the UI:

```text
http://localhost:8082
```

Check that the proxy management port is alive:

```text
http://localhost:8081
```

This opens the proxy management landing page. It is a quick way to confirm the proxy service is running before you start sending traffic through it.

Send traffic through the proxy:

```text
http://localhost:8080
```

If your target application is running on the host machine, `host.docker.internal` is usually the cleanest target host from inside Docker Desktop. For example, if your API runs at `http://localhost:5000` on the host, set:

```yaml
environment:
  TARGET: "http://host.docker.internal:5000"
```

## Persisting Requests Across Restarts

The default compose setup keeps requests in memory. Restarting the UI container clears everything. To persist requests, switch to disk storage and mount a volume:

```yaml
services:
  ui:
    environment:
      HUB: "http://proxy:8081/connect/audit"
      STORAGE_MODE: "Disk"
      RETENTION: "7d"
      MAX_AUDIT_ENTRIES: "0"
    volumes:
      - ui-keys:/home/app/.aspnet/DataProtection-Keys
      - ui-data:/app/data

volumes:
  ui-keys:
  ui-data:
```

The UI writes a SQLite database to `/app/data/audit.db` inside the container. The `ui-data` named volume keeps the file alive across restarts and image rebuilds.

`RETENTION=7d` removes requests older than seven days. `MAX_AUDIT_ENTRIES=0` disables the UI's count cap so retention is the only eviction policy. For truly cap-free capture, also set `MAX_QUEUED_ENTRIES=0` on the proxy — the proxy drops the oldest queued entries when its buffer fills up, so entries can be lost before they reach the UI regardless of storage settings. Adjust all three to suit the expected traffic volume.

## Target URL

`TARGET` is the simplest routing mode. When no routes file is configured, Vakthund creates one catch-all proxy route:

```text
/**
```

Every request that reaches the proxy is forwarded to the `TARGET` value.

For example:

```yaml
environment:
  TARGET: "https://api.example.test"
```

A client request to:

```text
http://localhost:8080/orders/123?include=lines
```

is captured by Vakthund and forwarded to:

```text
https://api.example.test/orders/123?include=lines
```

Use `TARGET` when all traffic should go to one upstream service.

## Routes File

Use a routes file when different paths should go to different upstream services. Vakthund reads a YAML file with a top-level `routes` list:

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

If the upstream service uses a different path than the one clients call through Vakthund, add `to` on that route:

```yaml
routes:
  - path: /foobar/**
    target: http://host.docker.internal:5001
    to: /api/foobar
```

For example, `/foobar/items/123` is forwarded to `http://host.docker.internal:5001/api/foobar/items/123`.

The `path` value is matched by YARP. Vakthund accepts common catch-all paths such as `/**` and `/api/**`, and converts them to YARP catch-all patterns internally. A route such as `/logs/**` matches `/logs`, `/logs/`, and deeper paths such as `/logs/archive/2026`.

Add `hosts` when the same proxy should split traffic by host or subdomain. Exact hosts such as `api.example.test`, wildcard subdomains such as `*.api.example.test`, and `*` are supported.

When a routes file is present, it takes priority over `TARGET`.

Routes can also include auth expectations. By default these give the UI enough context to explain why a request failed authentication or authorization:

```yaml
routes:
  - path: /api/orders/**
    target: http://host.docker.internal:5000
    auth:
      enforced: false
      issuer: https://login.example.com
      audience: orders-api
      scopes:
        - orders.read
      jwksUrl: https://login.example.com/.well-known/jwks.json
      jwe:
        keyType: Symmetric
        key: base64-encoded-key-bytes
```

Open a captured request and check the auth verdict to see whether the bearer token matches the configured route expectations.

Set `enforced: true` when the proxy should reject unauthenticated or unauthorized requests before forwarding them:

```yaml
routes:
  - path: /api/orders/**
    target: http://host.docker.internal:5000
    auth:
      enforced: true
      issuer: https://login.example.com
      audience: orders-api
      scopes:
        - orders.read
      jwksUrl: https://login.example.com/.well-known/jwks.json
```

Enforced route auth validates bearer JWT signatures and local claims. An enforced route must configure `jwksUrl`, `openIdConfigurationUrl`, or an absolute URL `issuer` that supports OIDC discovery.

For local debugging without Docker, create `src/Vakthund.Proxy/routes.local.yaml`. When no explicit `ROUTES_FILE` or `Proxy:RoutesFile` is set, Vakthund loads `routes.local.yaml` from the proxy content root automatically. In a published non-Docker app, place it beside the published proxy. The source local file is ignored by Git; `src/Vakthund.Proxy/routes.local.example.yaml` shows the expected shape.

## Mounting a Routes File

Create `routes.yaml` beside your Compose file:

```yaml
routes:
  - path: /api/**
    target: http://host.docker.internal:5000
  - path: /**
    target: http://host.docker.internal:3000
```

Mount it into the proxy container:

```yaml
services:
  proxy:
    build:
      context: ./src
      dockerfile: Vakthund.Proxy/Dockerfile
    ports:
      - "8080:8080"
      - "8081:8081"
    environment:
      ROUTES_FILE: "/etc/vakthund/routes.yaml"
    volumes:
      - ./routes.yaml:/etc/vakthund/routes.yaml:ro
```

`ROUTES_FILE` tells Vakthund where to read the routes from. In Docker, Vakthund also checks `/etc/vakthund/routes.yaml` automatically when no explicit routes file is configured, so this mount path is a good default.

Do not rely on `TARGET` and a mounted routes file at the same time. If the routes file is valid and contains routes, Vakthund uses it and ignores `TARGET`.

## Ports

- `8080`: proxy traffic. Point your client here.
- `8081`: proxy management endpoints and SignalR audit hub. Open this in a browser to confirm the proxy is running. The UI also uses it internally.
- `8082`: UI in the Compose examples.

The management port exposes `/config`, `/activity`, and `/connect/audit`. It is meant for the UI and local inspection tooling.

## First Request

After Compose is running:

1. Open `http://localhost:8082`.
2. Send a request to `http://localhost:8080`.
3. Check the dashboard or the requests page.
4. Open a request to inspect the auth verdict, headers, tokens, query parameters, cookies, bodies, status code, and timing.

If nothing appears, confirm that the UI `HUB` value points to the proxy management hub and that your client is sending traffic through the proxy port, not directly to the target app.
