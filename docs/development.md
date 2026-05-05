# Development

Vakthund is a .NET solution under `src/`.

## Development Suite Setup

Use this setup when working on Vakthund itself.

Prerequisites:

- .NET SDK 10.0.x.
- Node.js 24 or newer for Tailwind CSS builds.
- Docker Desktop or another Docker Compose compatible runtime for the source Compose stack and k6 smoke/load tests.

From the repository root, restore and build the application assets:

```bash
cd src/Vakthund.UI
npm ci
npm run css:build
cd ..
dotnet restore Vakthund.sln
dotnet build Vakthund.sln
```

Configure a development target before starting the proxy. The proxy needs either a route file or a `TARGET` value:

- Create `src/Vakthund.Proxy/routes.local.yaml` from `src/Vakthund.Proxy/routes.local.example.yaml` for local path-based routing. This file is ignored by Git.
- Or set `TARGET` in the shell before running the proxy.

Example single-target setup:

```bash
export TARGET=https://httpbin.org
```

On PowerShell:

```powershell
$env:TARGET = "https://httpbin.org"
```

Run the suite checks:

```bash
dotnet test Vakthund.sln
```

For a Docker-based smoke test that exercises the proxy, UI, and a local dummy API through k6:

```bash
cd Vakthund.Tests/k6
K6_VUS=2 K6_DURATION=10s docker compose --profile load up --build --abort-on-container-exit --exit-code-from k6
docker compose --profile load down --volumes
```

On PowerShell:

```powershell
cd Vakthund.Tests/k6
$env:K6_VUS = "2"
$env:K6_DURATION = "10s"
docker compose --profile load up --build --abort-on-container-exit --exit-code-from k6
docker compose --profile load down --volumes
```

The same k6 suite can be used for heavier local load checks. See `src/Vakthund.Tests/k6/README.md` for the exposed ports, tunables, and summary output.

## Projects

- `Vakthund.Proxy`: ASP.NET Core reverse proxy and audit stream.
- `Vakthund.UI`: Blazor Server frontend.
- `Vakthund.Shared`: shared models.
- `Vakthund.Tests`: unit tests.

## Run Locally With .NET

From `src/`, run the proxy:

```bash
dotnet run --project Vakthund.Proxy
```

In another terminal, run the UI:

```bash
dotnet run --project Vakthund.UI
```

Development launch settings use:

- Proxy traffic: `https://localhost:7269`
- Proxy management: `https://localhost:7270`
- UI: `https://localhost:7271`

In development, the UI hub defaults to:

```text
https://localhost:7270/connect/audit
```

The proxy does not have a development target in `appsettings.Development.json`. Set `TARGET` or create `src/Vakthund.Proxy/routes.local.yaml` before running it.

## Run With Docker Compose

There is a Compose file at `src/docker-compose.yml`. From `src/`, run:

```bash
docker compose up --build
```

The source Compose stack reads `TARGET` from the shell or from `src/.env` when present. Set it before starting Compose, for example:

```bash
TARGET=https://httpbin.org docker compose up --build
```

On PowerShell:

```powershell
$env:TARGET = "https://httpbin.org"
docker compose up --build
```

The stack exposes the proxy on `http://localhost:8080`, management on `http://localhost:8081`, and the UI on `http://localhost:8082`.

For day-to-day use, a Compose file in the repository root or in your own application stack is often more convenient. See [Getting Started](getting-started.md).

## CSS

The UI uses Tailwind through the local `package.json` in `src/Vakthund.UI`.

Install dependencies:

```bash
npm install
```

Build CSS:

```bash
npm run css:build
```

Watch CSS during UI work:

```bash
npm run css:watch
```

## Tests

Run tests from `src/`:

```bash
dotnet test Vakthund.sln
```

The tests cover route loading, audit queueing, memory and disk audit storage, JWT parsing and signature validation, auth verdicts, HTTP styling helpers, and metrics behavior.
