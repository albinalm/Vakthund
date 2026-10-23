# One Vakthund for several applications

Run one proxy and one UI to inspect several applications in the same environment. The proxy already routes by hostname and path; the UI collects all of its audit entries in one store.

```text
Ingress / TLS termination -> Vakthund proxy -> application A
                                           -> application B
                            |
                            +-- management hub -> Vakthund UI -> audit storage
```

Keep TLS termination, public access rules, and certificates at the ingress. Send each application's traffic to the proxy's traffic port. Give the UI private access to its management port and restrict access to the UI itself.

For an operator UI reachable outside a private network, configure [UI Authentication](ui-authentication.md). OIDC login and an explicit account allowlist protect the dashboard; the proxy's management port stays internal. Configure UI ingress trust separately with `UI:ForwardedHeaders`.

## Routes across namespaces

In Kubernetes, the proxy and UI can live in a dedicated `vakthund` namespace. Target application Services by their namespace-qualified DNS names. For a cluster using the default `cluster.local` domain:

```yaml
routes:
  - path: /**
    hosts:
      - api.example.test
    target: http://api.orders.svc.cluster.local
    timeout: 60s

  - path: /**
    hosts:
      - shop.example.test
    target: http://web.shop.svc.cluster.local
    timeout: 60s
```

Mount this file and set `ROUTES_FILE=/etc/vakthund/routes.yaml`. Set the UI's `HUB` to `http://vakthund-proxy:8081/connect/audit` when both components share a namespace.

Configure your ingress controller's supported cross-namespace backend mechanism. Standard Kubernetes Ingress backends reference Services in their own namespace. For Traefik IngressRoutes, cross-namespace Service references require the `kubernetesCRD.allowCrossNamespace` provider option. Keep TLS secrets and access middleware in the ingress route's namespace. Allow application ingress from the shared proxy in any NetworkPolicies.

Configure only explicit hosts and targets. Unknown hosts should not fall through to an application. Internal service-to-service traffic is captured only when it also passes through the proxy.

## Trusted ingress headers

When an ingress terminates HTTPS, it usually talks HTTP to Vakthund. Configure the ingress addresses Vakthund may trust so upstreams receive the original scheme, hostname, and client address. For example, in the proxy's application configuration:

```json
{
  "Proxy": {
    "ForwardedHeaders": {
      "KnownProxies": ["10.0.0.10"],
      "ForwardLimit": 1,
      "AllowedHosts": ["api.example.test", "shop.example.test"]
    }
  }
}
```

The equivalent environment variables are `Proxy__ForwardedHeaders__KnownProxies__0`, `Proxy__ForwardedHeaders__ForwardLimit`, and indexed `Proxy__ForwardedHeaders__AllowedHosts__0` entries. When ingress addresses vary within a controlled subnet, `Proxy__ForwardedHeaders__KnownNetworks__0=10.0.0.0/24` accepts that CIDR. Use the address Vakthund actually sees as its TCP peer; a host-networked ingress may appear as the node or CNI bridge address.

Only loopback peers are trusted by default. Additional peers must be explicitly configured. `ForwardLimit` defaults to one and must be positive. Increase it only for a known proxy chain, with every intermediary configured as trusted. The middleware reads forwarded lists from the nearest hop, preventing an extra client-supplied first address from overriding that hop.

Vakthund processes trusted `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host` before host routing, audit capture, and route authorization. YARP then generates upstream forwarded headers from the resolved request context. Destination applications must also trust Vakthund as their immediate proxy.

Client IP inspection and route IP policies use the resolved remote address. Unprocessed `X-Forwarded-For` and `X-Real-IP` headers are not treated as client identity. Existing installations relying on raw forwarded headers must configure their trusted ingress addresses.

## Storage and scaling

Use one UI replica with `STORAGE_MODE=Disk`, a persistent `/app/data` volume, and a retention window. All applications share the store, retention policy, and queue capacity. Request grid filters and saved views can separate traffic by hostname or target.

Persist UI data-protection keys separately. Set `DataProtection__KeysPath` to the mounted keys directory, for example `/home/app/.aspnet/DataProtection-Keys`. This explicit path also works when running the container as root; mounting a directory under another user's home does not change the framework's default key location. Without this setting, ASP.NET Core uses its normal key storage selection.

Start with one proxy replica. A UI currently connects to one proxy hub; placing multiple proxy replicas behind one Service does not aggregate their audit streams. Events emitted while the UI is disconnected are not durably replayed. Multiple proxies need explicit aggregation support before scaling this topology.

A shared proxy is a dependency of every application routed through it. Keep management endpoints private and retain ingress access controls such as admin IP allowlists. A shared UI provides access to all captured applications and is intended for operators who may inspect all of them.

Moving an existing installation to another namespace requires copying its audit database and data-protection keys to new PVCs. Stop the old UI before copying SQLite files, including any journal/WAL files. Keep the source volumes until the new installation and historical request details have been verified. Do not delete PVCs to rename or move them.
