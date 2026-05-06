# Using the UI

Open the UI at the port you mapped for `Vakthund.UI`. In the recommended Compose setup, that is:

```text
http://localhost:8082
```

## Dashboard

The dashboard shows the current captured traffic set. In memory mode, that is the current UI process session. In disk mode, it includes requests reloaded from the SQLite store:

- Total requests.
- Requests per minute.
- Average response time.
- Error rate.
- Requests over time.
- Status code distribution.
- Latest captured request.

The dashboard updates as the UI receives audit entries from the proxy. In disk mode, historical aggregate metrics are loaded from SQLite when the UI starts.

Deleting requests removes them from the requests table and updates stored request count and status-code distribution. Aggregate timing, request-rate, error-rate, and audit-loss metrics remain part of the dashboard history until the configured retention window trims them.

## Requests

The requests page lists captured requests in a searchable grid. You can filter by method, status, host, path, or query.

Open a row to inspect the full request.

## Request Details

The request detail page shows:

- Method, URL, status, timestamp, duration, response origin, matched route, host, and scheme.
- Whether a 401/403 was denied by proxy-enforced auth before reaching the upstream target.
- Request body and response body when captured.
- Query parameters.
- Auth verdict.
- Parsed tokens.
- Headers.
- Cookies.

Bearer JWTs are decoded into header and payload JSON. Basic auth is decoded into username and password. JWE tokens show their protected header, and can show a decrypted payload when JWE settings are configured.

### Auth Verdict

The auth verdict summarizes what Vakthund can prove about a request's bearer token:

- Missing bearer token on a route or response that indicates auth failure.
- Malformed bearer token.
- Expired token.
- Token that is not valid yet because of `nbf`.
- JWE payload that is encrypted or failed to decrypt.
- Issuer, audience, scope, and role mismatches against route auth expectations.
- Signature validation problems such as unknown `kid`, unsupported algorithm, invalid signature, or failed metadata/key loading.

When a route has auth expectations configured, those expectations are treated as the source of truth. If no key endpoint is configured, Vakthund may infer OIDC metadata from the token's `iss` claim to validate the signature, but token-derived metadata is shown as enrichment and is not treated as the API contract.

The verdict also shows common claim values such as subject, issuer, audience, scopes, roles, and client id when they are present.

## Configuration

The configuration page reads the proxy management endpoint and shows:

- Active routes.
- Request body capture limit.
- Response body capture limit.
- Proxy queue size.
- JWE key type configured in the UI.

Routes with auth expectations show issuer, audience, scope, and role chips in the configuration page. Routes that enforce auth at the proxy also show an `enforced` chip.

If the configuration page cannot reach the proxy, check the UI `HUB` setting and make sure the proxy management port is reachable from the UI container.

## Sensitive Data

Vakthund is an inspection tool. It can display authorization headers, cookies, request bodies, response bodies, and decrypted tokens.

Run it only in environments where that is acceptable. Do not expose the UI or management port to untrusted networks.
