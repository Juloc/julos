# Remote provider runtime

## Purpose

REM-006 through REM-008 require a deployed Apache Guacamole provider behind `Remote:Providers` so RDP, VNC and SSH sessions can be validated for real. `docs/BACKLOG.md` listed "exact provider runtime composition" as an open product decision that did not block implementation. This document records that composition and the provider-side Guacamole authentication boundary.

The provider runtime is one container image, launched once per Remote session by Runtime Manager under the existing `julos.remote` protocol-neutral policy (`docs/JULGATE-REMOTE-EXTRACTION.md`). It bundles unmodified `guacd` and the unmodified official Guacamole web application; no Remote transport, protocol or authentication logic is reimplemented.

## Image composition

`packages/JulOS.Remote/runtime/Dockerfile` builds three things and combines them in one final Ubuntu 24.04 (`tomcat:9-jdk21`) image:

- **guacd** is compiled from the official Apache `guacamole-server-1.6.0` source release. The stock `guacamole/guacd` image is Alpine (musl) based, while the Guacamole web application's own official image is Ubuntu (glibc) based; the two are not binary-compatible, so guacd cannot be copied from its own upstream image into this one. Building it from source on the exact same Ubuntu 24.04 base as the final image keeps every shared library version aligned. This is still the unmodified upstream implementation, not a fork.
- **The Guacamole web application** is copied verbatim (`COPY --from=guacamole/guacamole:1.6.0 /opt/guacamole/ /opt/guacamole/`) from the official image: the built `guacamole.war`, every bundled extension (including `guacamole-auth-json`, already present, not downloaded separately) and the official `entrypoint.d/*.sh` scripts. Those scripts run unmodified inside the launcher; they generate `GUACAMOLE_HOME` and a temporary Tomcat `CATALINA_BASE`, enable the JSON-auth extension because `JSON_ENABLED=true` is set, and start Tomcat. `WEBAPP_CONTEXT=ROOT` deploys it at `/`, so the internal token endpoint is `/api/tokens` and the WebSocket tunnel is `/websocket-tunnel`.
- **`JulOS.Remote.ProviderBridge`** (`packages/JulOS.Remote/runtime/JulOS.Remote.ProviderBridge`) is a small self-contained .NET tool, published single-file so the final image needs no .NET runtime. It contains no new transport or protocol code; it is a thin translator between the generic `JULOS_REMOTE_*` environment contract (`PostgresRemoteSessionProvisioner`) and the already-published `JulOS.Remote.Transport` library's `GuacamoleJsonLaunchEncoder`.

## Runtime flow

`packages/JulOS.Remote/runtime/remote-provider-runtime.sh` is the container entrypoint (mirroring the launcher-script pattern already used by `packages/JulOS.Browser/runtime/browser-runtime.sh`):

1. Validates every required `JULOS_REMOTE_*` variable is present.
2. Generates a random 16-byte JSON-auth secret key, local to this one container's lifetime. It is never shared with or coordinated with JulOS.Server; nothing outside the container needs to construct provider authentication material, because the container serves exactly one session against exactly one target.
3. Starts `guacd` on `127.0.0.1:4822` and waits for it.
4. Runs the official `/opt/guacamole/bin/entrypoint.sh` in the background (unmodified) and waits for Tomcat to listen on `8080`.
5. Runs the bridge's `finalize` command. The bridge decodes the opaque `JULOS_REMOTE_TARGET_CREDENTIAL` secret (D030), builds a `GuacamoleLaunchRequest`, and uses `GuacamoleJsonLaunchEncoder` to create the encrypted JSON-auth `data` value.
6. The bridge submits that encrypted `data` locally as form data to `http://127.0.0.1:8080/api/tokens` and requires a successful JSON response containing `authToken`. The encrypted JSON-auth `data` is never used directly as the WebSocket tunnel `token`.
7. The bridge writes only the URI-encoded Guacamole `authToken` into the private nginx include. The runtime then renders `nginx.conf` and starts nginx on `JULOS_PROVIDER_LISTEN_PORT` (`8081`).
8. The launcher waits until nginx is actually accepting connections on `8081`. Only after that readiness check succeeds does it invoke the bridge's `connected` command, which reports the provider event to `JULOS_REMOTE_CALLBACK_ENDPOINT`.

`connected` therefore means the complete provider-facing display path is ready: guacd is listening, the Guacamole web application has issued a valid auth token, and the same-origin provider listener is accepting connections. Preparing JSON-auth data alone is not a connected state.

`RemoteDisplayGateway.ProviderEndpoint` resolves one fixed `wss://{runtimeId}/...` template with no room for a per-session query string (by design: JulOS.Server proxies this connection itself and never exposes it to the browser). nginx reconciles this with Tomcat's token-based WebSocket tunnel: its single `location /` block injects the returned Guacamole `authToken` as a fixed private upstream query string and forwards the negotiated WebSocket subprotocol unchanged.

The encrypted JSON-auth `data`, Guacamole `authToken`, target credential and callback token remain runtime-private and are never returned through the JulOS browser API or written to normal logs. The token exchange has a bounded timeout and fails the runtime with a stable provider error instead of falling through to a tunnel that can only exchange keepalive traffic.

## Credential shape

The opaque `JULOS_REMOTE_TARGET_CREDENTIAL` secret (D030) is a UTF-8 JSON object with optional `username`, `password`, `domain`, `privateKey` and `passphrase` fields. The bridge maps these onto `GuacamoleLaunchRequest`:

- RDP always gets explicit `GuacamoleRdpOptions` (`Any` security, `Ignore` certificate policy, `Reconnect` resize, bidirectional clipboard) because arbitrary homelab targets are not expected to present a pinned or CA-trusted certificate.
- VNC omits `VncOptions`, which still authenticates with the password and omits only the additive display/clipboard/retry policy from REM-007.
- SSH uses password authentication by default; a `privateKey` in the credential switches to explicit `GuacamoleSshOptions` with `PublicKey` authentication and `Disabled` host-key verification, since no known-hosts entry is available for an arbitrary target.

## Validation

Repository regression coverage in `tests/JulOS.Architecture.Tests/RemoteProviderRuntimeTests.cs` enforces the provider-specific boundary that previously failed in deployment:

- JSON-auth `EncryptedData` must be exchanged through `/api/tokens`;
- the response must provide `authToken`, and nginx must use that token rather than `EncryptedData`;
- `FinalizeAsync` must not report `connected`;
- the launcher must report `connected` only after the `8081` readiness loop has completed.

A WebSocket `101 Switching Protocols` or Guacamole `ping` frames alone are not functional display acceptance. Deployed acceptance requires actual Guacamole protocol/display traffic and a rendered target session. RDP, VNC, browser/Android display and SSH re-confirmation remain deployment gates after a provider image containing this fix is published.

## Published and wired

The previously published image is `ghcr.io/juloc/julos-remote-provider:0.4.0-beta.4`, immutable index digest `sha256:e9c9d61adb82e56370a5fdaa76344dab686b4afd90a2ce41fc82cfe3a510b643` (its `linux/amd64` manifest is `sha256:3191f8115eddb13e27f50f190fe98e4f9a24d370b80fb9240340c21b72c8fb17`), with a provenance attestation that `gh attestation verify oci://ghcr.io/juloc/julos-remote-provider:0.4.0-beta.4 --owner juloc` accepts. It is built for `linux/amd64` only: `guacd` is compiled from source against the Ubuntu base, so `linux/arm64` is a deliberate, documented non-goal for this release rather than an oversight.

That published digest predates the `/api/tokens` exchange/readiness correction described above and must not be treated as evidence that the corrected display-authentication path is deployed.

The opt-in `remote` Compose profile wires the image into a full deployment through `deploy/compose/compose.remote.yaml` (`Remote__Providers__0__*` and `Remote__NetworkProfiles__0__*`, plus the `runtime-manager` service) layered over `deploy/compose/compose.yaml`; `deploy/compose/README.md` documents the invocation and the required `.env` values.

Still open: publish a provider image containing this correction, then repeat end-to-end deployed validation through the real JulOS Server, Runtime Manager and Remote frontend for RDP (REM-006), VNC (REM-007), browser/Android display (REM-005) and SSH close-out (REM-008).
