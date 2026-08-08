# Remote provider runtime

## Purpose

REM-006 through REM-008 require a deployed Apache Guacamole provider behind `Remote:Providers` so RDP, VNC and SSH sessions can be validated for real. `docs/BACKLOG.md` listed "exact provider runtime composition" as an open product decision that did not block implementation. This document records that composition once it was built and validated end to end against a real SSH target.

The provider runtime is one container image, launched once per Remote session by Runtime Manager under the existing `julos.remote` protocol-neutral policy (`docs/JULGATE-REMOTE-EXTRACTION.md`). It bundles unmodified `guacd` and the unmodified official Guacamole web application; no Remote transport, protocol or authentication logic is reimplemented.

## Image composition

`packages/JulOS.Remote/runtime/Dockerfile` builds three things and combines them in one final Ubuntu 24.04 (`tomcat:9-jdk21`) image:

- **guacd** is compiled from the official Apache `guacamole-server-1.6.0` source release. The stock `guacamole/guacd` image is Alpine (musl) based, while the Guacamole web application's own official image is Ubuntu (glibc) based; the two are not binary-compatible, so guacd cannot be copied from its own upstream image into this one. Building it from source on the exact same Ubuntu 24.04 base as the final image keeps every shared library version aligned. This is still the unmodified upstream implementation, not a fork.
- **The Guacamole web application** is copied verbatim (`COPY --from=guacamole/guacamole:1.6.0 /opt/guacamole/ /opt/guacamole/`) from the official image: the built `guacamole.war`, every bundled extension (including `guacamole-auth-json`, already present, not downloaded separately) and the official `entrypoint.d/*.sh` scripts. Those scripts run unmodified inside the launcher; they generate `GUACAMOLE_HOME` and a temporary Tomcat `CATALINA_BASE`, enable the JSON-auth extension because `JSON_ENABLED=true` is set, and start Tomcat. `WEBAPP_CONTEXT=ROOT` deploys it at `/` so the internal proxy needs no path prefix.
- **`JulOS.Remote.ProviderBridge`** (`packages/JulOS.Remote/runtime/JulOS.Remote.ProviderBridge`) is a small self-contained .NET tool, published single-file so the final image needs no .NET runtime. It contains no new transport or protocol code; it is a thin translator between the generic `JULOS_REMOTE_*` environment contract (`PostgresRemoteSessionProvisioner`) and the already-published `JulOS.Remote.Transport` library's `GuacamoleJsonLaunchEncoder`.

## Runtime flow

`packages/JulOS.Remote/runtime/remote-provider-runtime.sh` is the container entrypoint (mirroring the launcher-script pattern already used by `packages/JulOS.Browser/runtime/browser-runtime.sh`):

1. Validates every required `JULOS_REMOTE_*` variable is present.
2. Generates a random 16-byte JSON-auth secret key, local to this one container's lifetime. It is never shared with or coordinated with JulOS.Server; nothing outside the container needs to construct a valid token, because the container serves exactly one session against exactly one target.
3. Starts `guacd` on `127.0.0.1:4822` and waits for it.
4. Runs the official `/opt/guacamole/bin/entrypoint.sh` in the background (unmodified) and waits for Tomcat to listen on `8080`.
5. Runs the bridge's `finalize` command, which decodes the opaque `JULOS_REMOTE_TARGET_CREDENTIAL` secret (D030), builds a `GuacamoleLaunchRequest` and encodes a JSON-auth token, then reports the `connected` provider event to `JULOS_REMOTE_CALLBACK_ENDPOINT`. This is a readiness signal (guacd and the web application are ready to accept the display connection), not a per-connection authentication event; a fresh runtime exists per session.
6. Renders `nginx.conf` from `nginx.conf.template` and starts nginx on `JULOS_PROVIDER_LISTEN_PORT` (`8081`).

`RemoteDisplayGateway.ProviderEndpoint` resolves one fixed `wss://{runtimeId}/...` template with no room for a per-session query string (by design: JulOS.Server proxies this connection itself and never exposes it to the browser). nginx is the piece that reconciles this with Tomcat's token-based WebSocket tunnel: its single `location /` block injects the token the bridge already computed as a fixed upstream query string and forwards the negotiated WebSocket subprotocol unchanged. `RemoteDisplayEndpoints.ConnectAsync` requires the provider to negotiate back the exact subprotocol the browser requested; this was verified directly (`curl` Upgrade request against the running container) to return `101 Switching Protocols` with `Sec-WebSocket-Protocol: guacamole` echoed back and live guacd protocol bytes streaming immediately after.

## Credential shape

The opaque `JULOS_REMOTE_TARGET_CREDENTIAL` secret (D030) is a UTF-8 JSON object with optional `username`, `password`, `domain`, `privateKey` and `passphrase` fields. The bridge maps these onto `GuacamoleLaunchRequest`:

- RDP always gets explicit `GuacamoleRdpOptions` (`Any` security, `Ignore` certificate policy, `Reconnect` resize, bidirectional clipboard) because arbitrary homelab targets are not expected to present a pinned or CA-trusted certificate.
- VNC omits `VncOptions`, which still authenticates with the password and omits only the additive display/clipboard/retry policy from REM-007.
- SSH uses password authentication by default; a `privateKey` in the credential switches to explicit `GuacamoleSshOptions` with `PublicKey` authentication and `Disabled` host-key verification, since no known-hosts entry is available for an arbitrary target.

## Validated

Built and run directly (outside Runtime Manager) against a real `linuxserver/openssh-server` container on a shared Docker network, with a stand-in HTTP endpoint receiving the callback:

- `guacd` compiled with RDP, SSH, Telnet and VNC protocol support.
- guacd, the web application and nginx all reach a healthy, listening state (`remote-provider-runtime-health.sh`, matching the Browser runtime's health-check pattern).
- the bridge posted a correctly-shaped `connected` event carrying the deterministic `remote-{sessionId:N}` runtime ID and the `JULOS_REMOTE_EXPECTED_REVISION` value.
- a raw WebSocket upgrade against the container's external port returned `101 Switching Protocols`, echoed the `guacamole` subprotocol, and streamed live guacd protocol bytes for the SSH target.

Not yet done: wiring the image into a full JulOS deployment (`Remote:Providers`, `Remote:Display:ProviderEndpointTemplate`, Runtime Manager network) and validating through the real JulOS Server, Runtime Manager and Remote frontend end to end, for all three protocols. Publishing the image is likewise not yet done.
