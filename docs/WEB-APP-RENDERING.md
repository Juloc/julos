# Web application rendering

Status: In progress. D035 defines transparent proxy rendering and D042 defines the unified Browser product. The static proxy, dynamic encoded-origin proxy, WebSocket transport and proxy address-bar foundation are implemented. Server-owned Browser workspace persistence, broader proxy compatibility and Host Connector reachability remain open.

## 1. Goal

A web target must render in the user's own browser, inside a movable JulOS desktop window, so that video, canvas, WebGL and other interactive content decode and run locally with hardware acceleration.

Pixel-streaming a remote Browser is not part of the current Browser architecture.

Concrete targets that must work in local mode:

- a real-time single-page control panel with WebSocket telemetry (for example a UniFi Network application);
- a metrics UI that forbids framing (for example Grafana);
- a virtualization UI (for example Proxmox);
- a media application whose video must play locally (for example a home media server).

## 2. One Browser, proxy first

JulOS exposes exactly one user-facing application named **Browser**.

Its normal mode is **Proxy**:

- the target is fetched through JulOS;
- HTTP/HTTPS and WebSockets pass through the JulOS proxy;
- the response is rendered by the user's own browser inside the JulOS window;
- local GPU, video decoding, canvas and WebGL remain local to the device.

The old Browser and Adaptive Browser packages/runtimes were removed. The Core proxy surface is the
only Browser implementation. Configured/static web targets are destinations handled by this Browser;
they are not synthesized into separate Desktop launcher applications.

### 2.1 Browser workspace continuity

Browser state is owned by JulOS per authenticated user, not by one device. The server-side workspace
must retain at least:

- tab identifiers, order, URL, title and favicon metadata;
- active tab;
- navigation history needed for JulOS Back/Forward;
- window/browser workspace metadata required to resume on another device;
- proxy-side site session metadata where JulOS owns it.

A second device opening Browser receives the same workspace and can continue directly. Client-only
state such as arbitrary third-party IndexedDB, service-worker state or page JavaScript memory cannot
be guaranteed by a transparent proxy and must not be advertised as fully synchronized. Exact process-memory continuation is not promised.

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
- **Reachability:** when the target is not directly reachable from Server, the proxy reaches it through a target-bound `host.stream/1` grant on the outbound Host Connector tunnel, so the target is never exposed publicly. When the target shares Server's network, the proxy connects directly.
- **Authentication:** target credentials are injected on the server side through a short-lived secret lease (see the secret model in [`SECURITY_AND_OPERATIONS.md`](SECURITY_AND_OPERATIONS.md)). No target credential is ever sent to the client.
- **Rendering:** the desktop window embeds an iframe whose source is the target host. Because JulOS controls that host's response headers, the shell origin is allowed to frame it. Several windows embed several iframes; the footprint is a normal browser tab.

### 3.3 Dynamic address-bar targets

The **Browser proxy mode** core application can encode a typed HTTP or HTTPS origin into one host label under `WebApps:Dynamic:ProxyZone`:

```text
https://grafana.lan:3000
        ↓
wa<base32-origin>.p.os.juloc.de
```

The encoded host is reversible and carries no credential. Browser proxy mode supports ordinary
public Internet destinations by default while keeping private/non-public address space protected
against SSRF. DNS is resolved by JulOS and the connection is pinned to the validated address set.

`WebApps:Dynamic:AllowedHosts` remains the explicit policy for internal/private targets. The
allowlist has two independent meanings:

- a DNS suffix entry such as `.lan` authorizes a DNS **name**;
- a CIDR entry such as `192.168.0.0/16` authorizes a resolved **network address**.

A private literal IP target must be covered by a CIDR entry. A private DNS-name target must match an
allowed DNS suffix and, after resolution, at least one resolved address must be covered by an
explicit CIDR entry. Public DNS names may be browsed without an allowlist entry, but any resolved
private/loopback/link-local address is discarded unless explicitly allowlisted.

For every dynamic request JulOS resolves the target before opening a connection, discards addresses outside the configured CIDRs and passes only the validated addresses to the transport. HTTP and WebSocket connections are opened directly to one of those validated addresses while the original target host remains the HTTP/TLS authority. The transport therefore cannot perform a second uncontrolled DNS lookup between authorization and connect. If DNS fails the request reports the upstream unavailable; if the name resolves only outside the configured networks the request is denied.

This resolved-address rule is the SSRF boundary for dynamic mode. Adding a broad CIDR is therefore an explicit administrator decision, not an inferred private-network fallback.

### 3.4 Compatibility boundary

Browser proxy mode is a transparent-proxy compatibility mode, not a general browser engine. It can still fail for applications that hard-code their original scheme/origin, depend on browser behavior tied to the original host, or use client-side mechanisms that cannot be made transparent by response-header handling alone.

The Browser UI reports proxy incompatibility inside the same application. It must not send the user to another Browser app. A later explicit Remote mode is the compatibility path when full Chromium behavior is required.

### 3.5 Why this renders locally

The proxy is a transparent byte transport, not a remote browser. The application's HTML, JavaScript, WebSocket frames and media streams reach the user's own browser, which parses, executes and hardware-decodes them. A media stream plays with local hardware decoding; an interactive canvas runs on the local GPU.

## 4. Compatibility boundary

There is no streamed/remote Browser fallback. A target that cannot be made compatible with the
transparent proxy fails explicitly in the Browser surface. A future full remote mode, if required,
is a new feature and is not backed by retained legacy Browser runtime code.

## 5. Security model

- Transparent proxying with header stripping and server-side credential injection is a credentialed intermediary. Static targets are enabled explicitly; dynamic targets are constrained by both hostname and resolved-address policy.
- Because the proxy can reach internal infrastructure, authentication alone is not enough: the proxy and the `/api/v1/webapps` discovery endpoints require the `core.webapp.use` permission, which the administrator role holds by default. An authenticated account without it receives `403` (`webapp.not_authorized`), so least-privileged users cannot reach allowlisted internal targets unless an administrator grants the permission.
- Dynamic DNS names require an allowed suffix plus an allowed resolved CIDR, and the actual HTTP/WebSocket connection is pinned to the validated address set.
- Target credentials live only in the encrypted secret store and are leased for the proxied connection; they never reach the client.
- JulOS authentication/antiforgery cookies and inbound authorization/forwarding headers are stripped before a request is forwarded upstream. Dynamic/public Browser targets receive their real target `Host` but no JulOS `X-Forwarded-Host` or `X-Forwarded-Proto` metadata, so external canonical-host logic cannot redirect against the encoded JulOS proxy host.
- Every proxied session and every credential lease creates an audit event.
- A per-target request-rate budget limits abuse of an exposed proxy host.
- The proxy never publishes the internal target. Reaching JulOS itself from outside the home network is the responsibility of an authenticated reverse proxy or an overlay network and is out of scope for this component; it is covered by the remote-access guidance in [`SECURITY_AND_OPERATIONS.md`](SECURITY_AND_OPERATIONS.md).

## 6. Relationship to decision D005

D005 is superseded for Browser rendering. The isolated Chromium Browser runtime has been removed.
RDP, VNC and SSH remain separate Remote-package concerns.

## 7. Prerequisite

Local mode depends on wildcard DNS for `*.<julos-domain>` and a wildcard TLS certificate, issued and renewed through the Caddy integration and a supported DNS-provider API. Where wildcard hostnames are unavailable, proxy mode is unavailable. A path-based proxy is not adopted as a substitute, because it cannot serve the target applications reliably.

The JulOS session cookie must also be scoped to the deployment's parent domain (`Authentication:CookieDomain`, for example `.os.juloc.de`) so the authenticated session reaches each target subdomain. It is host-only by default, and without the parent-domain scope the embedded target would receive no session and the proxy would reject it.

## 8. Milestones

- **M0 — Done:** accept the design (`D035`) and record this plan. Define the target rendering-policy field and the per-target hostname scheme.
- **M1 — In progress:** transparent proxy, WebSocket transport, framing/cookie/redirect policy, dynamic encoded-origin backend and Browser address bar are implemented. Cross-origin redirects stay in the proxy, public HTTPS behind Caddy is preserved, dynamic targets do not receive JulOS forwarding metadata, and the old synthetic WebApp launcher/iframe path has been removed. Real target/browser acceptance and remaining compatibility work are still required.
- **M2 — Open, depends on `HCON-005`:** reach targets through a target-bound Host Connector stream and inject target credentials through an operation-bound secret lease, with nothing secret reaching the client. Dynamic origin/reference compatibility work belongs here where required.
- **M3 — Partially complete:** resolved-IP SSRF validation and connection pinning are implemented. Server-owned Browser workspace persistence remains.
- **M4 — Open:** remaining cookie/redirect edge cases; wildcard-TLS automation through the Caddy integration; per-target rate budget and audit; verified local media playback and multiple simultaneous windows.
- **M5 — Open:** security and footprint review and the remote-access runbook, as release gates before local mode is enabled by default.

## 9. Configuration

Static targets can be supplied from configuration during the current slice:

```text
WebApps:Targets:0:Host           unifi.os.juloc.de
WebApps:Targets:0:Upstream       https://10.0.0.5:8443
WebApps:AllowInvalidUpstreamCertificates  false   # opt-in for self-signed internal upstreams
Authentication:CookieDomain      .os.juloc.de     # parent-domain scope so the session reaches target subdomains
```

Dynamic Browser address-bar mode is explicit. Public Internet access defaults on; private targets remain default-deny:

```text
WebApps:Dynamic:Enabled          true
WebApps:Dynamic:ProxyZone        p.os.juloc.de
WebApps:Dynamic:AllowPublicInternet true
WebApps:Dynamic:AllowedHosts:0   .lan
WebApps:Dynamic:AllowedHosts:1   192.168.0.0/16
WebApps:Dynamic:AllowedHosts:2   10.0.0.0/8
```

A DNS suffix allows names but must be paired with the CIDR ranges those names are permitted to resolve into. Literal IP targets require only a matching CIDR. As environment variables the same keys use the double-underscore form, for example `WebApps__Dynamic__AllowedHosts__1`.

Database-backed targets and per-target credential references replace the static target list in a later milestone.

## 10. Open questions

- Confirm wildcard DNS and a wildcard TLS certificate are available for the deployment domain.
- No ownership question remains for tunneling: the owning Web/Browser capability authorizes the target, `HCON-005` transports the bound stream, and the operation-bound Secret lease injects credentials server-side.
