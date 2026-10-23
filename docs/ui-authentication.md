# UI authentication

Vakthund can require OpenID Connect (OIDC) login before anyone can inspect captured traffic. An external identity provider manages accounts, passwords, MFA, and recovery. Vakthund manages its own session and explicitly controls which provider accounts may access this installation.

Authentication is disabled by default for local use. Set `UI:Authentication:Enabled=true` to enable it. Missing or invalid required settings fail startup; an enabled installation never falls back to anonymous access because its provider is unavailable.

## Configure a provider

Register a web application with an OIDC provider using the authorization-code flow. Configure the following on the UI, for example with an application configuration file:

```json
{
  "UI": {
    "Authentication": {
      "Enabled": true,
      "Authority": "https://auth.example.test/application/o/vakthund/",
      "ClientId": "vakthund",
      "ClientSecretFile": "/run/secrets/vakthund-oidc-client-secret",
      "AllowedSubjects": ["your-provider-subject-id"],
      "SessionMinutes": 480
    },
    "ForwardedHeaders": {
      "KnownProxies": ["10.0.0.10"],
      "AllowedHosts": ["audit.example.test"]
    }
  },
  "AllowedHosts": "audit.example.test",
  "DataProtection": {
    "KeysPath": "/app/keys"
  }
}
```

Mount the client secret as a file and persist the data-protection key directory. `ClientSecret` is also supported for configuration systems that supply secrets directly; set either it or `ClientSecretFile`. Both must not be set together. Public OIDC clients can leave both empty if the provider permits them; use a confidential web client with a secret for a typical server deployment.

The authority must be an HTTPS issuer URL. Vakthund uses the provider's discovery document, validates issuer, audience, signature, lifetime, state and nonce, and uses PKCE. It requests the `openid` and `profile` scopes. Login does not require an API access token or refresh token to be retained. Only the ID token needed for provider logout is retained, in the server-side session.

Register these exact URLs with the provider, substituting your UI origin:

| Purpose | URL |
| --- | --- |
| Login callback | `https://audit.example.test/signin-oidc` |
| Logout callback | `https://audit.example.test/signout-callback-oidc` |

If the UI uses `PATH_BASE=/audit`, include `/audit` in both callback URLs. The provider should advertise an OIDC end-session endpoint for logout. The application clears its local session before starting provider logout.

## Allow accounts explicitly

`AllowedSubjects` contains exact, case-sensitive `sub` values from this provider. At least one nonempty subject is required when authentication is enabled. Use the subject as issued to the Vakthund client; some providers use different subjects for different applications.

A successful provider login alone does not grant access. Accounts outside the list see an access-denied page and receive no Vakthund session. Email addresses, display names, and provider roles do not implicitly grant access. Look up your subject in the provider's account/application details or its OIDC claim preview.

Every allowed account has operator access to all captured traffic and configuration in the installation, including request deletion. This feature does not isolate tenants or provide per-application permissions. Changes to the allowlist require restarting the UI.

## Environment variables and Kubernetes

ASP.NET Core's standard double-underscore environment names work without additional aliases:

```yaml
env:
  - name: UI__Authentication__Enabled
    value: "true"
  - name: UI__Authentication__Authority
    value: "https://auth.example.test/application/o/vakthund/"
  - name: UI__Authentication__ClientId
    value: "vakthund"
  - name: UI__Authentication__ClientSecretFile
    value: "/run/secrets/oidc/client-secret"
  - name: UI__Authentication__AllowedSubjects__0
    value: "your-provider-subject-id"
  - name: UI__ForwardedHeaders__KnownProxies__0
    value: "10.0.0.10"
  - name: UI__ForwardedHeaders__AllowedHosts__0
    value: "audit.example.test"
  - name: AllowedHosts
    value: "audit.example.test"
  - name: DataProtection__KeysPath
    value: "/app/keys"
volumeMounts:
  - name: oidc-client
    mountPath: /run/secrets/oidc
    readOnly: true
  - name: keys
    mountPath: /app/keys
volumes:
  - name: oidc-client
    secret:
      secretName: vakthund-oidc
  - name: keys
    persistentVolumeClaim:
      claimName: vakthund-ui-keys
```

The `vakthund-oidc` Secret must contain a `client-secret` key. Keep the actual secret in your deployment's secret-management system, such as SOPS; the example contains no credentials. Configure liveness/readiness probes against `/health`, which is anonymous and returns only process health. Static assets and account status pages are also anonymous; audit pages and Blazor endpoints require an allowed account.

## Behind an ingress

Serve the UI using HTTPS with a valid certificate. Configure `UI:ForwardedHeaders:KnownProxies` with the ingress addresses the UI actually sees. `KnownNetworks` accepts explicitly trusted CIDRs when necessary. The UI accepts one forwarded hop; untrusted peers cannot replace the request's scheme, host, or client address. Configure `AllowedHosts` for forwarded hosts and the application's host filter as shown above.

For a host-networked ingress, the peer may be a node or CNI bridge address rather than a pod IP. These UI trust settings are separate from `Proxy:ForwardedHeaders`, which configures the inspection proxy. Correct scheme and host handling is required for HTTPS callback URLs and secure cookies.

Keep the UI reachable only through the intended ingress, and keep the inspection proxy's management port private. UI authentication does not authenticate `/config`, `/activity`, or `/connect/audit` on the proxy. It does not change the authentication of the applications being inspected.

## Sessions and logout

The UI uses a secure, HTTP-only, host-only cookie with SameSite=Lax. The cookie contains an opaque reference to a server-side ticket. Tickets expire after eight hours by default; `SessionMinutes` accepts 1 through 1440. Expiration is absolute and is not extended by activity. Signing in again creates a new session.

Sessions are stored in memory in the single UI process. Restarting it ends existing sessions, even if its data-protection keys persist. Multiple UI replicas require a shared ticket store before this design can be scaled.

Sign out uses an antiforgery-protected POST, revokes the server ticket, and starts the provider's logout flow. Replaying the old cookie cannot restore the revoked session. Other open tabs revalidate their session at least every 30 seconds and return to sign-in after revocation. Active Blazor connections close when their authentication expires. Already displayed content may remain in browser memory; the server no longer authorizes subsequent requests with an expired session.

Login forms also require antiforgery validation. Return navigation is limited to local paths inside the configured path base. Provider errors are shown as a generic sign-in failure without exposing protocol details or tokens to the browser.

## Authentik example

Authentik is a suitable shared self-hosted provider for several internal applications. Its deployment belongs in infrastructure; Vakthund uses the same OIDC integration for Authentik and other providers.

1. Install Authentik and complete its administrator setup using its [Kubernetes installation guide](https://docs.goauthentik.io/install-config/install/kubernetes). Give it its own HTTPS hostname and database credentials. It can use a separately managed PostgreSQL database; it does not need a database shared with Vakthund's audit storage.
2. Create an application/provider pair using an OAuth2/OIDC provider and a confidential client. See [Create an OAuth2 provider](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/create-oauth2-provider).
3. Use an application slug such as `vakthund`, enable authorization-code flow with PKCE, and select a signing key. Add the exact login and logout callbacks above and the standard `openid`/`profile` scope mappings.
4. Use the per-provider issuer URL, for example `https://auth.example.test/application/o/vakthund/`, as `Authority`. Copy the client ID and store the client secret in your secret manager. If you change Authentik's issuer mode, verify its discovery/issuer configuration before using the new URL.
5. Inspect the claims issued for your operator account to obtain its `sub`, and add that exact value to `AllowedSubjects`. Provider-side application restrictions and MFA can further restrict who reaches the login callback.
6. Enable Vakthund authentication, verify an allowed login and a denied account, then verify login/logout and expiry through the real HTTPS ingress before removing a network-based access restriction.
