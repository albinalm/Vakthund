# AGENTS.md

## Working agreement

This section gates everything below it. Satisfy it before writing code.

- **All new development happens on a feature branch.** Never commit to `main`, and never merge into it. Work reaches `main` through a pull request, reviewed and merged by a person - an agent opens the PR and stops there.
- **Every feature branch has a linked GitHub issue.** Look for an existing issue first. If none covers the work, you may create one and branch from it when the user asks for the work - confirm the title and body with them first, then keep going. Never invent an issue number, and never branch without one.
- **Branch name: `<initials>/<issue-id>-<feature-description>`** - the author's initials, lowercase, then the issue number, then a short hyphenated description.

  ```text
  aa/42-fix-audit-retention
  ```

  Git refs cannot contain a colon or a space, so the separator between the issue id and the description is a hyphen.
- Reference the issue as `#<id>` in the pull request body so GitHub links the two, and let the user merge it.

## Repository rules

- Target .NET 10. From the repository root, restore with `dotnet restore src/Vakthund.sln`, build with `dotnet build src/Vakthund.sln --configuration Release --no-restore`, and run tests with `dotnet test src/Vakthund.sln --configuration Release --no-build`.
- The UI uses Node.js 24 or newer and Tailwind CSS. From `src/Vakthund.UI`, run `npm ci` and `npm run css:build` when changing UI styles or markup that affects generated CSS. Edit `Styles/app.css`; regenerate `wwwroot/app.css` rather than editing it by hand.
- Use the existing xUnit project, `Vakthund.Tests`, for behavior checks. Keep Docker/k6 smoke and load checks in `src/Vakthund.Tests/k6`; use the local dummy API and the short smoke settings documented in `docs/development.md` when touching the proxy pipeline, audit streaming, or service integration.
- The proxy needs a target or routes file to start. Use a local test target or `src/Vakthund.Proxy/routes.local.yaml`, based on the tracked example. Keep local routes, `.env` files, credentials, and captured traffic out of Git.
- Use temporary storage paths for persistence verification. Do not exercise retention or deletion against a user's audit database or existing Docker volumes.

## Code style

`src/.editorconfig` is authoritative; ReSharper/Rider formats from it. What it does not encode:

- **No XML documentation.** No `<summary>`, no `<remarks>`, no `<param>`. A one- or two-line `//` comment is allowed only when it states something the code cannot: a framework constraint, an ordering requirement, or a concurrency invariant. If the comment restates the code, remove the ambiguity in the code instead.
- **File-scoped namespaces**, one type per file, folder path matches namespace. Razor components use a matching partial class in `.razor.cs` for nontrivial behavior. Cross-project usings that every file needs go in that project's `GlobalUsings.cs`; retain SDK implicit usings.
- **Explicit types**, except where the type is already written on the right-hand side - `var x = new Thing()`, `var s = provider.GetRequiredService<T>()`. `foreach`, `await`, LINQ results and method returns all name their type.
- **Expression bodies** for methods, properties and local functions whenever the body is a single expression; `switch` expressions over `switch` statements.
- Allman braces. Blank lines are not used to separate fields, properties or members - the file stays dense.
- `sealed` by default on new classes when inheritance is not required; `record` for value-shaped models; primary constructors for dependency injection. Preserve mutable DTO and serialization contracts when changing existing models.
- `string.Empty`, never `""`. Line length 182.
- Use `[UsedImplicitly]` for reflection-created types or members when needed by the analyzer, following the existing JetBrains.Annotations usage.
- Nullable is enabled everywhere. Model a missing value as `null` or a `TryX` pattern rather than throwing for control flow. Invalid startup configuration should fail with an actionable error.
- Apply these conventions to new and changed code; avoid unrelated repository-wide formatting changes.

## Architecture boundaries

- `Vakthund.Proxy` owns YARP routing, authentication and IP enforcement, request/response capture, audit queueing, and management endpoints. Keep composition in the existing `Extensions` classes and keep `Program.cs` small.
- `Vakthund.UI` owns Blazor presentation, audit storage, metrics, token inspection, and explanatory auth verdicts. Components call services; keep storage, HTTP calls, token parsing, and metrics calculations out of markup.
- `Vakthund.Shared` holds models exchanged by the proxy and UI. Keep it independent of both applications; changes to shared payloads must remain consistent with SignalR delivery, management responses, and persisted audit data.
- Keep auth enforcement in the proxy and diagnostic verdicts in the UI aligned. A decoded token or a displayed verdict does not replace signature validation and authorization before forwarding.
- Preserve the separation between proxy traffic and the management port. `/connect/audit`, `/config`, and `/activity` are management endpoints; preserve their host restrictions and middleware ordering.
- Route configuration goes through `RoutesFileResolver`, `RoutesLoader`, and `ProxyRouteConfigFactory`. Preserve named auth/IP references, route priority, host matching, path rewriting, and timeout behavior when changing routing.

## UI and audit rules

- Reuse existing shared components, Radzen controls, Tailwind utilities, and theme tokens before introducing new UI patterns. Keep light/dark behavior consistent with `Styles/app.css` and `wwwroot/theme.js`.
- Keep I/O out of render paths. Use component lifecycle methods and services; marshal background UI updates through `InvokeAsync`, and cancel timers and unsubscribe events when disposing components.
- Resolve `IAuditStore` using the configured `StorageMode`, following the existing keyed registrations. Keep memory and SQLite implementations consistent for filtering, counts, deletion, and retention.
- Preserve capture limits, queue bounds, batching, cancellation, and loss accounting. Request inspection must not consume a body that the upstream still needs or prevent the response from reaching the client.
- Keep metrics and audit-loss reporting consistent across live streaming and persisted history. Preserve thread safety in singleton services and stores.

## Safe changes

- Captured headers, cookies, bodies, and tokens may contain secrets. Do not add their values, signing keys, or JWE decryption keys to application logs or test artifacts; use synthetic fixtures.
- Preserve JWT/JWE validation behavior, trusted-proxy handling, IP policies, and enforced route auth. Cover behavior changes with the relevant existing tests, including rejection paths.
- Consumer and development documentation lives in `docs/`. Keep `docs/configuration.md`, `docs/getting-started.md`, and the local routes example aligned with configuration or deployment changes. Keep agent guidance in this file; `CLAUDE.md` only points here.
