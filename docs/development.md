# Development

Vakthund is a .NET solution under `src/`.

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

In development, the proxy target defaults to `https://httpbin.org`, and the UI hub defaults to:

```text
https://localhost:7270/connect/audit
```

## Run With Docker Compose

There is a Compose file at `src/docker-compose.yml`. From `src/`, run:

```bash
docker compose up --build
```

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

The tests cover route loading, audit storage, JWT parsing, HTTP styling helpers, and metrics behavior.
