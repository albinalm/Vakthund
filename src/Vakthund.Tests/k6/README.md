# Vakthund k6 Load Test

This suite starts a local load-test environment with Docker Compose:

- `dummy-api`: a small local ASP.NET Core API used as the proxy target.
- `proxy`: the real Vakthund proxy image built from this repository.
- `ui`: the real Vakthund UI image built from this repository.
- `k6`: the load generator.

The test sends traffic only to the local proxy. It does not call httpbin or any external API.

## Run

From this directory:

```bash
docker compose --profile load up --build --abort-on-container-exit --exit-code-from k6
```

The services are exposed on host ports for manual inspection while the test is running:

```text
Proxy traffic:      http://localhost:18000
Proxy management:  http://localhost:18001
UI:                http://localhost:18002
Dummy API:         http://localhost:18080
```

Open `http://localhost:18001` to confirm the proxy management port is alive. Open `http://localhost:18002` to watch the UI while k6 is running.

k6 prints a timing summary when the run ends and writes the same data to:

```text
results/summary.json
```

The most useful values for comparison are:

- `http_req_duration`: total k6-observed request duration.
- `http_req_waiting`: time waiting for the first response byte.
- `vakthund_fast_duration`: k6 duration for `/api/fast`.
- `vakthund_data_duration`: k6 duration for `/api/data/{id}`.
- `vakthund_echo_duration`: k6 duration for `/api/echo`.

## Tune The Load

The defaults are intentionally heavy for a local machine:

```bash
K6_VUS=200 K6_DURATION=2m docker compose --profile load up --build --abort-on-container-exit --exit-code-from k6
```

The Compose file raises proxy and dummy API logging to `Warning` during load tests. High-volume ASP.NET Core and YARP info logs can distort the test because Docker stdout can become a bottleneck under heavy traffic.

Use lower values when you want a quick smoke test:

```bash
K6_VUS=25 K6_DURATION=30s docker compose --profile load up --build --abort-on-container-exit --exit-code-from k6
```

## Routing

The proxy is configured with `routes.yaml` mounted into `/etc/vakthund/routes.yaml`:

```yaml
routes:
  - path: /**
    target: http://dummy-api:8080
```

All load-test traffic goes through the proxy and lands on the local dummy API.

## Cleanup

Stop and remove the environment:

```bash
docker compose --profile load down --volumes
```
