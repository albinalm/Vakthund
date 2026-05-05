# How Vakthund Works

Vakthund has two running services.

The proxy receives client traffic, records request and response details, forwards the request to the configured target, then publishes an audit entry.

The UI connects to the proxy management hub, stores audit entries in memory, and renders dashboards and request detail screens.

## Request Flow

```text
client
  -> Vakthund.Proxy on port 8080
  -> configured target service
  -> response returns through Vakthund.Proxy
  -> audit entry is queued
  -> SignalR broadcasts to Vakthund.UI
  -> UI updates live
```

The proxy also runs management endpoints on a separate port:

```text
Vakthund.Proxy management port, default 8081
  /config
  /activity
  /connect/audit
```

The UI reads `/config` to show active routes and capture limits. It connects to `/connect/audit` to receive live request batches.

## Proxy

`Vakthund.Proxy` is an ASP.NET Core service built on YARP. It configures routes from either:

- `TARGET`, for one catch-all upstream.
- `Vakthund:RoutesFile` or `ROUTES_FILE`, for YAML-based path routing.

The request interceptor captures:

- Scheme, host, path, query, and method.
- Headers, cookies, and query parameters.
- Request body, when body capture is enabled and within the configured size limit.
- Response body, when response body capture is enabled and within the configured size limit.
- Status code and duration.

Captured entries are written to a bounded in-memory queue. If the queue is full, the oldest entries are dropped.

## UI

`Vakthund.UI` is a Blazor Server app. It connects to the proxy SignalR hub specified by `HUB` or `Proxy:AuditHubUrl`.

The UI keeps captured requests in memory for the current UI process. It provides:

- A dashboard with request count, rate, response timing, error rate, and status code distribution.
- A searchable requests table.
- A request detail page with body, headers, cookies, query parameters, tokens, response, and timing.
- A configuration page showing active proxy routes and capture limits.

## Token Handling

Vakthund tries to parse authentication data from captured headers.

For bearer JWTs, it decodes and formats the header and payload, and marks the token as expired when the `exp` claim is in the past.

For JWE tokens, it can show the token header without a key. If a JWE key is configured, it attempts to decrypt the payload.

For basic auth, it decodes the username and password from the captured header.

## Storage Model

Vakthund does not write captured traffic to a database or file. Captured entries live in memory:

- The proxy queue is bounded by `MaxQueuedEntries`.
- The UI request store is bounded by `MaxAuditEntries`.
- Restarting either service clears its in-memory state.

This keeps local development simple and avoids creating a permanent copy of sensitive traffic by default.
