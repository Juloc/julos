# Web application rendering

Status: In progress. Design accepted in decision `D035`. The static transparent proxy, dynamic encoded-origin proxy and address-bar browser foundation are implemented; Agent-tunnel reachability, credential injection, compatibility fallback and the remaining release gates are still open.

## 1. Goal

An internal web application must render in the user's own browser, inside a movable JulOS desktop window, so that video, canvas, WebGL and other interactive content decode and run locally with hardware acceleration.

Pixel-streaming a remote browser is heavy, discards local hardware decoding and is the wrong transport for media and interactive local content. It is retained only as a fallback and for isolation and true remote desktops.

Concrete targets that must work in local mode:

- a real-time single-page control panel with WebSocket telemetry (for example a UniFi Network application);
- a metrics UI that forbids framing (for example Grafana);
- a virtualization UI (for example Proxmox);
- a media application whose video must play locally (for example a home media server).

## 2. Two rendering modes

Every web-application target resolves to one of two modes:

- **Local (default):** JulOS reverse-proxies the target and the user's browser renders it in a desktop-window iframe. Local, fast, hardware-accelerated.
- **Streamed (fallback):** JulOS runs the target in an isolated browser runtime and streams its display, as specified in [`BROWSER-RUNTIME.md`](BROWSER-RUNTIME.md) and decision `D005`. Used only when local mode is incompatible, when isolation is required, or for RDP, VNC and SSH.

A window is presentation state; the rendering mode is a property of the target, not of the window (see the window and session separation in [`ARCHITECTURE.md`](ARCHITECTURE.md)).

## 3. Local mode: per-target transparent host proxy

### 3.1 Why application URLs are not rewritten

Serving a foreign web application under a shared path prefix (`/proxy/app/...`) requires rewriting every absolute URL, script path, stylesheet reference, cookie path and WebSocket endpoint in the response. Modern single-page applications use absolute root paths and open their WebSocket at a fixed root path (for example `/wss/`), so they break under a path prefix. Rewriting a live single-page application is fragile and never complete.

JulOS therefore does not move an application underneath a shared path prefix. Each proxied target is served at its own host, at the root of that host, exactly where the application expects to be. Dynamic address-bar mode may still need bounded origin/reference rewriting when an application emits its original absolute upstream origin; that work is part of the remaining dynamic compatibility slice, not a path-prefix proxy.

### 3.2 Mechanism

- Each approved target receives a stable hostname `<slug>.<julos-domain>` (for example `unifi.os.juloc.de`).
- Wildcard DNS (`*.<julos-domain>`) and a wildcard TLS certificate resolve and terminate every target host. Certificate issuance and renewal use the Caddy integration.
- The JulOS reverse proxy at that host forwards the request to the internal target and streams the response back, including media and chunked bodies. It performs only the compatibility and security work owned by the proxy:
  - remove `X-Frame-Options`;
  - narrow `Content-Security-Policy` `frame-ancestors` to the JulOS shell origin and drop directives that would block embedding;
  - adjust `Set-Cookie` `Domain` and `SameSite` so the application's own session cookies stay first-party inside the window iframe;
  - rewrite proxy-owned redirect/location headers when they point back at the same upstream origin;
  - pass the WebSocket `Upgrade` handshake through and proxy the socket for the life of the connection;
  - apply request and idle timeouts and a per-target request-rate budget.
- **Reachability:** when the target is not directly reachable from Server, the proxy reaches it through the outbound Agent tunnel, so the target is never exposed publicly. When the target shares Server's network, the proxy connects directly.
- **Authentication:** target credentials are injected on the server side through a short-lived secret lease (see the secret model in [`SECURITY_AND_OPERATIONS.md`](SECURITY_AND_OPERATIONS.md)). No target credential is ever sent to the client.
- **Rendering:** the desktop window embeds an iframe whose source is the target host. Because JulOS controls that host's response headers, the shell origin is allowed to frame it. Several windows embed several iframes; the footprint is a normal browser tab.

### 3.3 Dynamic address-bar targets

The Web Browser core application can encode a typed HTTP or HTTPS origin into one host label under `WebApps:Dynamic:ProxyZone`:

```text
https://grafana.lan:3000
        ↓
wa<base32-origin>.p.os.juloc.de
```

The encoded host is reversible and carries no credential. Dynamic mode remains default-deny through `WebApps:Dynamic:AllowedHosts`.

The allowlist has two independent meanings:

- a DNS suffix entry such as `.lan` authorizes a DNS **name**;
- a CIDR entry such as `192.168.0.0/16` authorizes a resolved **network address**.

A literal IP target must be covered by a CIDR entry. A DNS-name target must match an allowed DNS suffix and, after resolution, at least one resolved address must be covered by an explicit CIDR entry. A suffix alone never grants network reachability.

For every dynamic request JulOS resolves the target before opening a connection, discards addresses outside the configured CIDRs and passes only the validated addresses to the transport. HTTP and WebSocket connections are opened directly to one of those validated addresses while the original target host remains the HTTP/TLS authority. The transport therefore cannot perform a second uncontrolled DNS lookup between authorization and connect. If DNS fails the request reports the upstream unavailable; if the name resolves only outside the configured networks the request is denied.

This resolved-address rule is the SSRF boundary for dynamic mode. Adding a broad CIDR is therefore an explicit administrator decision, not an inferred private-network fallback.

### 3.4 Why this renders locally

The proxy is a transparent byte transport, not a remote browser. The application's HTML, JavaScript, WebSocket frames and media streams reach the user's own browser, which parses, executes and hardware-decodes them. A media stream plays with local hardware decoding; an interactive canvas runs on the local GPU.

## 4. Streamed mode

Streamed mode is unchanged from [`BROWSER-RUNTIME.md`](BROWSER-RUNTIME.md) and `D005`: an isolated browser runtime in the target network, displayed through the Remote transport. It remains the correct choice for:

- a target that still does not function after transparent proxying (rare: applications that hard-code their own scheme and host in client script, or reject any modified header);
- a target that must be isolated from the user's browser for containment;
- RDP, VNC and SSH sessions, which are not web applications.

## 5. Mode selection

Each target carries a rendering policy: `local`, `streamed` or `auto`.

- `local` and `streamed` force the mode.
- `auto` attempts local mode first. If a readiness probe or the initial load fails in a way that indicates the application cannot be proxied transparently, the target falls back to streamed mode, and the fallback is recorded on the target so the next open is immediate.

Fallback is an explicit, observable transition, not a hidden retry (see the no-silent-fallback rule, `D011`).

## 6. Security model

- Transparent proxying with header stripping and server-side credential injection is a credentialed intermediary. Static targets are enabled explicitly; dynamic targets are constrained by both hostname and resolved-address policy.
- Because the proxy can reach internal infrastructure, authentication alone is not enough: the proxy and the `/api/v1/webapps` discovery endpoints require the `core.webapp.use` permission, which the administrator role holds by default. An authenticated account without it receives `403` (`webapp.not_authorized`), so least-privileged users cannot reach allowlisted internal targets unless an administrator grants the permission.
- Dynamic DNS names require an allowed suffix plus an allowed resolved CIDR, and the actual HTTP/WebSocket connection is pinned to the validated address set.
- Target credentials live only in the encrypted secret store and are leased for the proxied connection; they never reach the client.
- JulOS authentication/antiforgery cookies and inbound authorization/forwarding headers are stripped before a request is forwarded upstream.
- Every proxied session and every credential lease creates an audit event.
- A per-target request-rate budget limits abuse of an exposed proxy host.
- The proxy never publishes the internal target. Reaching JulOS itself from outside the home network is the responsibility of an authenticated reverse proxy or an overlay network and is out of scope for this component; it is covered by the remote-access guidance in [`SECURITY_AND_OPERATIONS.md`](SECURITY_AND_OPERATIONS.md).

## 7. Relationship to decision D005

`D005` rejects iframes as the general application runtime because a foreign origin can forbid framing and because internal services must not be exposed. Local mode does use an iframe, but only for a JulOS-controlled host whose framing headers JulOS itself sets, reached through the Agent tunnel and never publicly exposed. The reasons behind `D005` do not apply to this case. Framing a foreign origin directly remains forbidden. Decision `D035` records this boundary.

## 8. Prerequisite

Local mode depends on wildcard DNS for `*.<julos-domain>` and a wildcard TLS certificate, issued and renewed through the Caddy integration and a supported DNS-provider API. Where wildcard hostnames are unavailable, only streamed mode is offered until the prerequisite is met. A path-based proxy is not adopted as a substitute, because it cannot serve the target applications reliably.

The JulOS session cookie must also be scoped to the deployment's parent domain (`Authentication:CookieDomain`, for example `.os.juloc.de`) so the authenticated session reaches each target subdomain. It is host-only by default, and without the parent-domain scope the embedded target would receive no session and the proxy would reject it.

## 9. Milestones

- **M0 — Done:** accept the design (`D035`) and record this plan. Define the target rendering-policy field and the per-target hostname scheme.
- **M1 — In progress:** transparent local proxy, WebSocket transport, framing/cookie/redirect policy, configured-target launcher integration, dynamic encoded-origin backend and address-bar browser are implemented. Real target/browser acceptance and the remaining dynamic compatibility work are still required.
- **M2 — Open:** reach targets through the Agent tunnel and inject target credentials through a secret lease, with nothing secret reaching the client. Dynamic origin/reference compatibility work belongs here where required.
- **M3 — Partially complete:** resolved-IP SSRF validation and connection pinning are implemented. Rendering-policy resolution with `auto` and an observable fallback to streamed mode remains.
- **M4 — Open:** remaining cookie/redirect edge cases; wildcard-TLS automation through the Caddy integration; per-target rate budget and audit; verified local media playback and multiple simultaneous windows.
- **M5 — Open:** security and footprint review and the remote-access runbook, as release gates before the mode is enabled by default.

## 10. Configuration

Static targets can be supplied from configuration during the current slice:

```text
WebApps:Targets:0:Host           unifi.os.juloc.de
WebApps:Targets:0:Upstream       https://10.0.0.5:8443
WebApps:Targets:0:RenderingMode  local            # local (default) | streamed | auto
WebApps:AllowInvalidUpstreamCertificates  false   # opt-in for self-signed internal upstreams
Authentication:CookieDomain      .os.juloc.de     # parent-domain scope so the session reaches target subdomains
```

Dynamic address-bar mode is explicit and default-deny:

```text
WebApps:Dynamic:Enabled          true
WebApps:Dynamic:ProxyZone        p.os.juloc.de
WebApps:Dynamic:AllowedHosts:0   .lan
WebApps:Dynamic:AllowedHosts:1   192.168.0.0/16
WebApps:Dynamic:AllowedHosts:2   10.0.0.0/8
```

A DNS suffix allows names but must be paired with the CIDR ranges those names are permitted to resolve into. Literal IP targets require only a matching CIDR. As environment variables the same keys use the double-underscore form, for example `WebApps__Dynamic__AllowedHosts__1`.

Database-backed targets and per-target credential references replace the static target list in a later milestone.

## 11. Open questions

- Confirm wildcard DNS and a wildcard TLS certificate are available for the deployment domain.
- Final ownership of Agent-tunnel proxy reachability and credential injection within the existing Core/Browser capability boundaries.
- Optional later extension: named, shareable workspaces that group several windows.
