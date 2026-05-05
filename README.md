# Vakthund

A developer tool for inspecting HTTP request traffic in real time. Vakthund sits as a YARP reverse proxy and captures incoming requests, exposing headers, authorization tokens, JWT payloads, and other auth-related metadata through a Blazor Server UI.

Built for local dev sessions where you want visibility into what your client is actually sending without touching your application code.

## Features

- Real-time request log via SignalR
- Header and authorization inspection
- JWT payload decoding
- Request history for the current session

## Stack

- ASP.NET Core + YARP (proxy)
- Blazor Server (UI)
- SignalR (live updates)

## Projects

- `Vakthund.Proxy` — YARP middleware, audit capture, SignalR hub
- `Vakthund.UI` — Blazor Server frontend
