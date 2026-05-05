# Using the UI

Open the UI at the port you mapped for `Vakthund.UI`. In the recommended Compose setup, that is:

```text
http://localhost:8082
```

## Dashboard

The dashboard shows the current in-memory traffic session:

- Total requests.
- Requests per minute.
- Average response time.
- Error rate.
- Requests over time.
- Status code distribution.
- Latest captured request.

The dashboard updates as the UI receives audit entries from the proxy.

## Requests

The requests page lists captured requests in a searchable grid. You can filter by method, status, host, path, or query.

Open a row to inspect the full request.

## Request Details

The request detail page shows:

- Method, URL, status, timestamp, duration, host, and scheme.
- Request body and response body when captured.
- Query parameters.
- Parsed tokens.
- Headers.
- Cookies.

Bearer JWTs are decoded into header and payload JSON. Basic auth is decoded into username and password. JWE tokens show their protected header, and can show a decrypted payload when JWE settings are configured.

## Configuration

The configuration page reads the proxy management endpoint and shows:

- Active routes.
- Request body capture limit.
- Response body capture limit.
- Proxy queue size.
- JWE key type configured in the UI.

If the configuration page cannot reach the proxy, check the UI `HUB` setting and make sure the proxy management port is reachable from the UI container.

## Sensitive Data

Vakthund is an inspection tool. It can display authorization headers, cookies, request bodies, response bodies, and decrypted tokens.

Run it only in environments where that is acceptable. Do not expose the UI or management port to untrusted networks.
