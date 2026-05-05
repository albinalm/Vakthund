<p align="center">
  <img src="media/banner.png" alt="Vakthund logo" width="300">
</p>

<p align="center">
  <a href="https://github.com/albinalm/Vakthund/actions/workflows/qa.yml"><img src="https://github.com/albinalm/Vakthund/actions/workflows/qa.yml/badge.svg?branch=main" alt="QA"></a>
  <a href="https://github.com/albinalm/Vakthund/releases/latest"><img src="https://img.shields.io/github/v/release/albinalm/Vakthund" alt="Latest release"></a>
  <a href="https://github.com/albinalm/Vakthund/pulse"><img src="https://img.shields.io/github/commit-activity/m/albinalm/Vakthund" alt="Commit activity"></a>
  <a href="https://github.com/albinalm/Vakthund/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0-blue" alt="License"></a>
</p>

Vakthund is a local HTTP inspection proxy for development. Put it between a client and the service you are building, send traffic through it, and watch the requests appear in a Blazor UI as they happen.

It is useful when you need to see what a browser, mobile app, integration, webhook sender, or test client is really sending. Vakthund captures headers, query parameters, cookies, request bodies, response bodies, status codes, timings, and authorization metadata without requiring changes to the target application.

## What It Does

- Proxies HTTP traffic with YARP.
- Captures live request and response details.
- Streams captured requests from the proxy to the UI over SignalR.
- Decodes bearer JWTs and basic auth headers in the request detail view.
- Explains common auth failures with route-level issuer, audience, scope, role, and signature checks.
- Can decrypt JWE tokens when a key is configured.
- Shows dashboard metrics, request history, route configuration, and per-request details.

Vakthund keeps captured traffic in memory for the current run. It is designed for local development and controlled environments, not for long-term storage or production traffic retention.

## Documentation

- [Getting Started](docs/getting-started.md)
- [How Vakthund Works](docs/how-it-works.md)
- [Configuration](docs/configuration.md)
- [Using the UI](docs/using-the-ui.md)
- [Development](docs/development.md)

## Quick Start

The recommended setup is Docker Compose with two services:

- `proxy`, which accepts client traffic and forwards it to your target service.
- `ui`, which connects to the proxy management hub and displays captured requests.

For a single upstream service, set `TARGET` on the proxy:

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
      TARGET: "http://host.docker.internal:5000"

  ui:
    build:
      context: ./src
      dockerfile: Vakthund.UI/Dockerfile
    ports:
      - "8082:8080"
    environment:
      HUB: "http://proxy:8081/connect/audit"
    depends_on:
      - proxy
```

Then send client traffic to `http://localhost:8080` and open the UI at `http://localhost:8082`.

You can also open `http://localhost:8081` in a browser to confirm the proxy management port is running.

For multiple upstreams or path-based routing, mount a routes file instead. See [Getting Started](docs/getting-started.md) for the full Compose example and the `routes.yaml` format.

## Projects

- `Vakthund.Proxy`: reverse proxy, request capture middleware, management endpoints, and SignalR audit hub.
- `Vakthund.UI`: Blazor Server frontend for dashboard, request list, request details, and configuration.
- `Vakthund.Shared`: shared models used by both services.
- `Vakthund.Tests`: unit tests for parsing, routing, metrics, and storage behavior.

## License

Vakthund is completely FOSS and licensed under the terms in [LICENSE](LICENSE).
