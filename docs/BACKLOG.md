# Backlog

This file is the current high-level implementation state. Detailed future work belongs in `WORK_BREAKDOWN.md` and GitHub issues.

Status values: `Planned`, `Ready`, `In progress`, `Blocked`, `Done`.

## Current state

| ID | Work item | Status | Notes |
|---|---|---|---|
| Phase 0 | Repository and engineering foundation | Done | Solution, architecture gates, validation, local stack, CI and version metadata. |
| Phase 1 | Core platform model | Done | Packages, applications, layouts, sessions, observability, permissions and Agents. |
| Phase 2 | Persistence, authentication and core APIs | Done | PostgreSQL/SQLite core persistence, auth, authorization, profile, errors, operations, secrets, audit and events. |
| DESK-001..012 | Desktop shell foundation | Done | Shell, windows, pointer interactions, snapping, taskbar, launcher, persistence, responsive modes, observability, widgets and accessibility. |
| DESK-013 | Browser first-run and sign-in | Done | Fresh deployments can create the initial administrator and subsequent sessions can sign in entirely through the production Desktop shell. Login/setup now issue a persistent root-scoped cookie with a 48-hour default lifetime, and authenticated status on Desktop boot renews that lifetime so responsive mobile/desktop mode changes on the same origin keep the same login. The `JulOsShell` custom element now has JSDOM regression coverage (`src/JulOS.Desktop/src/shell.test.ts`) for authenticated-name persistence through the `#applyLanguage()` sweep, profile theme/motion application, and `onProfileChanged` reapply after a settings save (issue #62). |
| DESK-014 | Production shell composition | Done | Enabled package apps and persisted widgets use the existing launcher/window/taskbar/frontend/persistence stack; Core Settings, Package Manager, Agent status, notifications and problems are normal desktop windows, and package lifecycle changes refresh the catalog live. |
| DESK-015 | Cross-platform desktop interaction pass | Done | Shared responsive rules, Pointer Events, full-screen state, minimized taskbar state, platform-adaptive desktop window chrome, independent compact JulOS mobile controls, taskbar-excluded usable bounds, shell keyboard handling and the existing Alt-Tab switcher are wired into production; deployed Windows/macOS/touch acceptance remains a release gate. |
| DESK-016 | Appearance and personalization completion | Done | System/light/dark theme, reduced motion, Fluent-derived tokens and the JulOS accent system are active; server-confirmed theme and motion changes apply without reload. Deferred beyond this iteration: a user-selectable accent, the Full/Balanced/Simple presets and the wallpaper/density controls from `UI_DESIGN_SYSTEM.md`; the Settings surface currently exposes language, theme, motion and time zone only. |
| REL-PKG-001 | Official package artifact and signing pipeline | In progress | Reproducible Host Metrics/Remote ZIP builds and full-archive SHA-256/ECDSA-P256-P1363 verification are implemented; a stable private signing key, matching trusted public-key configuration and the first real signed release run remain. |
| REL-ALPHA-007 | Published Desktop web root | Done | The container publishes Desktop assets through the Server web root and the release smoke test verifies `/` plus the main ES module. |
| Phase 3 | Desktop shell foundation | Done | DESK-013 through DESK-016 are implemented; installable PWA, device layouts, Phone Split, Tablet multi-window defaults, Surface lifecycle and Shell Back remain explicitly planned as MOB-001..010. |
| PKG-001 | Package manifest schema | Done | Versioned strict manifest, runtime, permissions, apps, widgets, capabilities, migrations and frontend declarations. |
| PKG-002 | Artifact verification | Done | Complete package archives are digest- and trusted-publisher-signature-verified before opening. This is the implemented high-trust path; PKG-013/014 add isolated unknown code and optional-signature warning behavior without weakening integrity. |
| PKG-003 | Runtime Manager | Done | Authenticated sidecar controls only allowlisted JulOS-owned Docker runtimes. |
| PKG-004 | Package storage isolation | Done | Provider-aware package storage supports isolated PostgreSQL schemas/roles and isolated SQLite package files. |
| PKG-005 | Worker control contract | Done | Validation, configure, register, start, stop and health contracts include deadlines; process workers receive explicit package database provider identity. |
| PKG-006 | Install and configure lifecycle | Done | Verified idempotent install and recoverable configuration flow are implemented. |
| PKG-007 | Enable, disable and fault handling | Done | Worker lifecycle isolates failures from Core and preserves diagnosis. |
| PKG-008 | Update and removal | Done | Migration disclosure, bounded rollback and explicit data deletion are implemented. |
| PKG-009 | Capability broker | Done | Provider resolution, caller grants, deadlines and audit are implemented without package references. |
| PKG-010 | Package frontend host | Done | Same-origin integrity verification, closed Shadow DOM and token-free host context. |
| PKG-011 | Package Manager UI/API | Done | Signed package upload, configuration and enable/disable/remove lifecycle are available through the production Desktop Package Manager with safe-mode and fault visibility. |
| PKG-012 | Reference test package | Done | App, widget, worker, settings, capability and intentional fault mode are included. |
| Phase 4 | Package platform | Done | Complete package platform is implemented. |
| AGT-001 | Enrollment tokens and server identity issuance | Done | One-time hashed token redemption, durable credentials, audit, reuse rejection and HTTP integration coverage are implemented. |
| AGT-002 | Agent identity and outbound connection | Done | First-run enrollment, recoverable exact retries, protected local identity persistence, restart loading and full repository validation are complete. |
| AGT-003 | Agent command dispatcher | Done | Typed polling, deadlines, diagnostics execution and server-side advertised-command authorization are implemented and integration-tested. |
| AGT-004 | Linux system metrics collectors | Done | CPU, memory, load, uptime, storage and network collection plus valid/missing/malformed fixture tests are complete; the released Agent was enrolled and run against a real Linux host, confirming all required metric names reach the Server, the first CPU sample is unknown, and later samples carry a delta. See issue #14 for evidence. |
| AGT-005 | Host metrics package and widgets | Done | Persisted-metric provider, signed-manifest authorization, authenticated frontend bridge and live/stale/offline/error view logic are implemented; the signed official package artifact was installed, configured and enabled through the real Package Manager lifecycle, its application and widget loaded in the running Desktop, and live/offline and capability-revocation-on-disable behavior were verified end to end. Real deployment testing found and fixed several package-management defects along the way (see below). See issue #7 for evidence. |
| AGT-006 | Agent diagnostics and update foundation | Done | Exact protocol negotiation, shared capability inventory, bounded reconnect diagnostics and a manual-only digest-verified update preparation contract are implemented, documented and fully validated. |
| Phase 5 | Legacy Agent and host-observability foundation | Done | Enrollment, transport, command authorization, Host Metrics and diagnostics are implemented and deployed under the historical Agent name. HCON-002 performs the atomic current-product migration; no new AGT work is allowed. |
| REM-001 | Protocol-neutral Remote session contracts | Done | Core owns generic contracts, validation, lifecycle and exact idempotency; concrete protocol identities remain in the Remote package. |
| REM-002 | Julgate inventory and extraction boundaries | Done | Verified Julgate responsibilities are mapped to shared transport, Remote, Runtime Manager, Desktop, Files, Browser, migration-only code or explicit rejection. |
| REM-003 | Shared transport implementation | Done | `JulOS.Remote.Transport` has immutable published releases consumed by JulOS Remote and Julgate; additive RDP/VNC/SSH policy versions preserve the shared transport boundary. |
| REM-004 | Remote worker and session orchestration | Done | Exact-idempotent ownership, policy-gated runtimes, authenticated provider events, lifecycle enforcement, cleanup, explicit detach and active-session resume authorization are implemented and validated. |
| REM-005 | Functional Remote display client and same-origin transport | In progress | Repository acceptance is complete through PRs #39 and #40. The complete JSON-auth token-exchange/readiness/tunnel-selector correction is published in `0.4.0-beta.15` and the immutable provider digest is pinned in the Remote Compose overlay. Browser/Android rendered-display validation remains in #37. |
| REM-006 | RDP provider integration | In progress | PR #42 adds additive explicit Guacamole 1.6.0 security, certificate, resize and clipboard policy plus distinct account-unavailable failure handling; real provider integration and deployed validation remain in #41. |
| REM-007 | VNC provider integration | In progress | PR #44 passes full repository validation with explicit VNC authentication, resize, clipboard, display and bounded retry policy; deployed VNC evidence remains in #43. |
| REM-008 | SSH provider integration | In progress | PR #46 passes full repository validation and published transport 0.3.0 with explicit password/private-key/NONE authentication, host-key verification, timeout, keepalive and terminal policy. Earlier deployed SSH evidence reached the provider and same-origin WebSocket, but the provider JSON-auth exchange defect invalidates that run as final display acceptance; SSH must be re-confirmed with the corrected provider image before issue #45 closes. |
| BRW-001 | Unified proxy Browser | In progress | One user-facing Browser. The Core proxy address-bar is the Browser surface; legacy Browser/Adaptive Browser packages are deleted and configured web targets are no longer synthesized as separate launcher applications. Proxy mode remains default. |
| BRW-002 | Browser proxy compatibility | In progress | Transparent host proxy compatibility includes framing/CSP handling, cookie/redirect behavior, WebSockets, HTTPS preservation behind Caddy and cross-origin redirect re-encoding. Dynamic/public targets no longer receive JulOS forwarding metadata, relative redirects resolve against the actual upstream request, and iframe navigation fetch metadata is normalized to a top-level document navigation. Redirect diagnostics now expose sanitized source/target paths. Media/origin/body compatibility and real-site acceptance remain. |
| BRW-003 | Browser workspace continuity | Ready | Persist per-user tabs, order, active tab, navigation metadata and resumable Browser workspace server-side; expose read/update APIs and realtime revision handling so another device resumes the same workspace. Proxy-owned site-session persistence is included where technically possible. Arbitrary third-party IndexedDB/service-worker/JS memory is explicitly not promised. Depends on DB-001 for durable schema migration. |
| Phase 6 | Remote and Browser | In progress | Remote covers RDP/VNC/SSH only. Browser is the Core transparent-proxy Browser from D035/D042, followed by server-owned workspace continuity. No remote Browser package/runtime remains. |
| WEB-001 | Browser proxy rendering (D035/D042) | In progress | Transparent/dynamic proxy, WebSockets and SSRF controls are implemented. Remaining work includes broader origin/body compatibility, Host Connector target-bound reachability, operation-bound credential injection and release validation. |
| SPEC-001 | Reconcile Host Connector, open apps and Mobile/PWA concept | Done | Target specifications, decisions, junior-ready dependencies and acceptance criteria are recorded across the authoritative documentation. No runtime behavior is claimed. |
| STAB-001 | Integrate package route fallback fix | Done | Integrated on `main` as `0ef293c`, released in `0.4.0-beta.19`, and verified through the real-Kestrel package-action smoke stage plus full repository validation. The source branch was deleted; do not reimplement it. |
| DB-001 | Supported SQLite schema upgrades | Ready | Must land before Host Connector or workspace schema migrations. Replaces production `EnsureCreated` upgrade behavior with ordered fixture-tested migrations. |
| HCON-001 | Host Connector contracts and migration constants | Ready | Depends on SPEC-001. |
| HCON-002 | Atomic Agent-to-Host-Connector replacement | Planned | Depends on HCON-001 and DB-001; includes code/API/database/identity/UI/Host Metrics and no dual runtime. |
| HCON-003..005 | Connector upgrade validation, typed adapters and streams | Planned | HCON-003 and HCON-004 follow HCON-002 in parallel; HCON-005 depends on both and gates WEB/terminal transport. |
| HCON-006 | Remove legacy 426 tombstone | Planned | Release-phase cleanup after one announced transition release; historical data/names remain readable but no legacy runtime route survives 1.0. |
| MOB-002 | Installable PWA assets and disconnected/update UX | In progress | Web manifest (standalone, theme/background, maskable icon), Shell icons, a service worker that caches only versioned immutable Shell assets plus a truthful `offline.html`, and best-effort registration are implemented and wired into the Shell entry. The worker is served from a dedicated no-cache `/sw.js` endpoint (the fingerprinted static-asset pipeline cannot serve a registrable worker). Manifest and worker delivery are verified against the real published host; the full section-14 layout-flush update handshake lands with `MOB-004`, and installed-PWA/offline acceptance on real Android/iOS devices is part of the `MOB-010` release gate. |
| MOB-001, MOB-003..010 | PWA decisions and device workspaces | Planned | Decisions record, client-device registration, layout-identity migration, Phone/Tablet, Surface lifecycle, Browser/Remote migration, Operations, Back and cross-device release gate. `MOB-004` depends on `DB-001`. |
| CAT-001 | Catalog schemas and validator | Ready | SPEC-001, STAB-001 and FND-004 are complete on `main`; implement the exact schemas, canonicalization and fixtures from `APPLICATION_CATALOG.md`. |
| CAT-002 | Catalog sources, atomic cache and trust | Planned | Depends on CAT-001, DB-001 and the existing operation/secret foundations. |
| PKG-013..014 | Unknown native isolation and optional signatures | Planned | Unknown frontend/worker code is isolated before unsigned/unknown native installation is enabled. |
| CONN-001 / APP-001 / API-011 | Connection, App Installation and secret leases | Planned | Establish provider-neutral connection, mutation-free preview/apply and operation-bound out-of-band Connector secret delivery. |
| DKR-001..008 | Docker inventory, deployment, terminal and applications | Planned | `origin/agent/docker-phase-completion` is not merged as-is; useful bounded behavior/tests are ported after HCON. Includes image/Compose apply, ownership, UI, discovery, problems and container terminal. |
| APP-002..006 | Connection delivery, update, backup, uninstall and adoption | Planned | Backup precedes destructive update/removal; adoption depends on stable Docker inventory/deployment. |
| CAT-003 / REL-CAT-001 | Store, App Builder and official catalog release | Planned | Depends on catalog/app/Docker/trust foundations and replaces the embedded catalog authority in one cutover. |
| REM-009 | Provider-neutral terminal presentation | Planned | Depends on HCON-005 and existing Remote orchestration/display foundation; DKR-006 consumes it. |
| Phase 6A | Product realignment foundations | Planned | SPEC-001 is done; DB, HCON-001..005, MOB, CAT schema/source and native isolation are mandatory before Phase 7. HCON-006 is later release cleanup. |
| Phase 7 | Docker, application delivery and Proxmox | Planned | Depends on Phase 6A and completion of rendered Remote/Browser/WEB gates. |
| Phase 8 | Files and Caddy | Planned | Includes separate Caddy UI integration API work. |
| Phase 9 | Discovery and operational hardening | Planned | Depends on stable Host Connector and package/application runtimes. |
| Phase 10 | Release and Julgate migration | Planned | Requires all 1.0 release gates. |

## REM-003 completion evidence

- the sole implementation lives in the JulOS monorepo as `JulOS.Remote.Transport`;
- JulOS Remote consumes the library through a project reference;
- immutable package version `0.1.0` was published to GitHub Packages;
- package and symbol artifacts have SHA-256 evidence and GitHub provenance attestations;
- a verification probe restored the exact package from GitHub Packages and verified its digests and attestations;
- Julgate `main` consumes exactly `JulOS.Remote.Transport` `0.1.0`;
- Julgate no longer contains its own Guacamole payload, HMAC or AES implementation;
- Julgate passes restore, build, 123 unit tests, RDP/VNC parity tests, real SFTP/FTP/SMB roundtrips, hardened Compose smoke tests, migration tests, Playwright, Trivy, CodeQL, NuGet audit and backup/restore validation;
- no PAT, package credential, copied source tree or fallback transport implementation is committed.

## REM-004 completion evidence

- capability requests expose optional provider-visible caller metadata but untrusted callers must leave it empty;
- the authenticated HTTP endpoint passes the authorized package identity and authenticated user UUID as separate trusted broker inputs;
- the broker rejects request-supplied caller context and creates the provider-visible package/user context itself;
- capability audit records retain the authenticated user identity;
- durable PostgreSQL sessions enforce user/package ownership, exact create idempotency, bounded listing, optimistic revisions and cancellation idempotency;
- the `remote.session/1` capability provider maps create, read, list, cancel, disconnect, detach and resume to caller-safe responses;
- configured protocol providers require semantic versions, digest-pinned images and bounded CPU, memory and process limits;
- configured network profiles authorize exact runtime networks, target host patterns and target ports before allocation;
- secret-reference metadata must be present, package-owned by the caller and restricted to a Remote purpose before allocation;
- Runtime Manager calls use an authenticated narrow HTTP contract and verify package, version, instance and image identity on every create or recovery path;
- deterministic runtime identities make provisioning retries exact and prevent duplicate allocations;
- runtime failures expose no secret material, attempt idempotent cleanup and persist caller-safe terminal failures;
- lifecycle changes publish `remote.session.changed` events without provider-specific payloads;
- synchronous create and lifecycle responses plus lifecycle events provide the required operation visibility, so no duplicate operation-progress subsystem is introduced;
- inactivity and maximum-duration expiry clear presentation access and move sessions into a terminal state;
- explicit disconnect clears presentation access immediately and removes the provider runtime idempotently;
- one Server background worker retries terminal runtime cleanup in bounded passes;
- cleanup failures create one deduplicated operator-visible problem, increment observations on retry and resolve after successful cleanup;
- one flat connection service applies exact runtime-bound `connecting` to `connected` and active-session to `failed` transitions;
- trusted provider failures are restricted to stable caller-safe codes and bounded detail;
- connected activity uses the Server clock and coalesces frequent writes before persistence;
- one private Server endpoint routes `connected`, `failed` and `activity` events to the existing connection service;
- provider callbacks use expiring HMAC tokens bound to exact session and runtime identities;
- callback credentials are supplied only to the provider runtime through a separate bounded secret-environment channel;
- Runtime Manager writes secret environment values to a user-only temporary file, passes only its path to Docker and removes it on close;
- normal runtime environment values remain non-secret and continue to reject secret-like names;
- window detach requires the caller to select `keep-active` or `disconnect` explicitly;
- `keep-active` revokes the current presentation descriptor without changing provider activity or removing the runtime;
- `disconnect` reuses the existing disconnect and runtime-cleanup path instead of adding another cleanup implementation;
- resume requires exact authenticated user, caller package, active state, runtime identity and optimistic revision;
- every accepted resume advances the session revision and clears any stale presentation descriptor while preserving runtime and provider activity;
- terminal sessions cannot resume, so expired, cancelled, disconnected or failed sessions cannot regain access through stale presentation state;
- provider ingress, callback authentication, secret-environment policy, allocation, detach and resume are covered by unit and PostgreSQL integration tests;
- the complete repository validator passes with 381 .NET tests, 91 Desktop tests, architecture checks, frontend build, manifest validation, container build and a clean working tree.

REM-005 has a token-free graphical descriptor and authenticated same-origin WebSocket proxy. PR #39 builds the existing Remote custom element with the official Apache Guacamole 1.6.0 browser artifact, pinned by ZIP and library SHA-256. The browser, JulOS proxy and hidden provider negotiate one exact WebSocket subprotocol before frames are forwarded. One `Guacamole.Keyboard` receives desktop and mobile `InputSink` events, `Ctrl+Alt+Shift+Escape` releases capture and resets pressed keys, exactly one pointer or touchscreen adapter is active, resize is collapsed through one 150 ms scheduler, and reconnect, full-screen, failure and teardown states are visible. Deployed browser testing later showed that a successful WebSocket upgrade and `ping` frames are insufficient acceptance while the provider uses the wrong Guacamole tunnel token. The provider fix now exchanges JSON-auth `data` through `/api/tokens`, defers `connected` until nginx is ready and supplies the JSON connection selectors; `0.4.0-beta.15` publishes that complete correction. The remaining REM-005 work is deployed browser/Android rendered-display validation and any UX defects found there.

REM-006 keeps RDP ownership inside `JulOS.Remote.Transport` and the Remote package. The additive options contract preserves the published 0.1.0 constructor while validating exact Guacamole security modes, strict/ignore/TOFU/pinned certificate policy, display-update/reconnect resize policy and directional clipboard settings. NLA-based modes require credentials before launch. `remote.account_unavailable` remains distinct from `remote.authentication_failed` when a trusted provider can classify the target account state.

REM-007 keeps VNC ownership in the same provider boundary and adds no new orchestration layer. The existing JSON-auth encoder validates and maps password authentication, dynamic or fixed display resize, clipboard direction and encoding, cursor behavior, color depth, read-only and local-input policy, compression, quality and a bounded retry count. Omitting `VncOptions` preserves the previous payload, while VNC options supplied to RDP or SSH fail closed. Transport version `0.2.0` contains the additive RDP and VNC provider-policy release and avoids overwriting immutable `0.1.0`.

REM-008 uses the same flat encoder and one additive options record. It validates password, OpenSSH private-key and NONE authentication; strict single-entry host-key verification; bounded timeout and keepalive; and explicit terminal font settings. Caller-owned private-key and passphrase memory remains provider-local, omitted `SshOptions` preserve the previous SSH payload, and SSH options supplied to RDP or VNC fail closed. Transport version `0.3.0` contains this additive SSH policy and was published after full repository validation.

Deployed REM-005..008 provider validation started by tracing how a Remote session's target credential reaches the provider runtime and found that it never did: `PostgresRemoteSessionProvisioner` validated `SecretReferenceId` metadata but only ever called `ISecretReferenceService.ReadAsync`, which by design returns non-secret metadata; the decrypting `ISecretLeaseService.AcquireAsync` (D029) had zero callers anywhere in the repository, for Remote or for the Browser display credential it was already documented as backing. See decision D030: the provisioner now creates (or idempotently reuses) a durable `remote.session.credential` Operation scoped to the session owner and caller package, drives it to `Running`, leases the secret, and forwards the decrypted bytes as one opaque Base64 `JULOS_REMOTE_TARGET_CREDENTIAL` runtime secret-environment entry. `tests/JulOS.Integration.Tests/Remote/RemoteSessionServiceTests.cs` asserts the credential round-trips through the full HTTP-backed provisioning flow.

The "exact provider runtime composition" open decision is resolved by D031 and `docs/REMOTE-PROVIDER-RUNTIME.md`: one image bundles guacd (built from official source against the same Ubuntu base as the web application, since the upstream guacd image is Alpine and not binary-compatible with it), the unmodified official Guacamole web application (copied verbatim, including its own entrypoint scripts and bundled JSON-auth extension), and a minimal `JulOS.Remote.ProviderBridge` tool that maps the existing `JULOS_REMOTE_*` environment contract onto the already-published `JulOS.Remote.Transport` library's `GuacamoleJsonLaunchEncoder`. The deployed display investigation found a root-cause bug in that bridge: the encoder's encrypted JSON-auth `data` was written directly as the WebSocket `token`, bypassing Guacamole's `/api/tokens` exchange, and `connected` was reported before nginx had even started. The corrected bridge exchanges `data` locally for `authToken`; nginx receives only that returned auth token; the launcher emits `connected` only after the `8081` readiness loop. No Docker or network topology change is part of this fix.

An earlier SSH run proved the real Server, Runtime Manager, signed Remote package, provider runtime and target could be allocated and could negotiate the same-origin WebSocket. It is no longer treated as final display acceptance because the provider-side token exchange was wrong. Repository regression tests enforce the corrected auth/readiness/tunnel-selection sequence; SSH must now be re-run with the pinned `0.4.0-beta.15` provider image before REM-008 closes.

VNC and RDP exercise the same provider runtime, credential path and display transport and were not independently re-run end to end. The corrected provider image is published and provenance-attested as `ghcr.io/juloc/julos-remote-provider@sha256:dc0960cab89219df1347d5a98a6321087adb8d1bf0fe5021a2d23c8b3f2f376f` and is pinned by default in `deploy/compose/compose.remote.yaml`. Publication and pinning are complete; deployed RDP/VNC/SSH/browser display acceptance must now exercise this exact image through newly created sessions. See `docs/REMOTE-PROVIDER-RUNTIME.md`.

AGT-004 and AGT-005 were validated by enrolling the released Agent against a running JulOS Server, confirming every required host metric reaches the Server with the documented first-sample-unknown/later-sample-delta pattern, then building, signing and installing the official Host Metrics package through the real Package Manager lifecycle (install, configure, enable), opening its application and widget in the running Desktop, and confirming live, offline and disabled-package capability-revocation states end to end. That deployed check found and fixed defects that no existing test had reached, because the Package Manager HTTP API had no integration coverage before `tests/JulOS.Integration.Tests/Packages/PackageEndpointTests.cs`:

- the antiforgery cookie and Identity session cookie used `CookieSecurePolicy.Always`, which makes ASP.NET Core's antiforgery middleware throw on every request when the connection is not HTTPS; the documented single-container alpha deployment binds Kestrel to plain loopback HTTP, so every antiforgery-protected mutation failed (see decision D027);
- `AgentEndpoints` and `PackageCapabilityEndpoints` passed the raw ASP.NET Core `HttpContext.TraceIdentifier` as an audit correlation identifier; its default format contains a colon, which the audit correlation-identifier validator rejects, crashing agent enrollment-token creation and package capability invocation; both now use the existing `CorrelationId.Get` helper, and its own fallback path is now sanitized the same way as a caller-supplied value;
- the `permission:core.package.read` and `permission:core.package.manage` authorization policies were referenced by every package endpoint but never registered, so the entire Package Manager HTTP API failed with an authorization-policy-not-found error;
- listing, installing or removing a package whose installation faulted before it wrote its metadata file crashed with an unhandled `FileNotFoundException`; the metadata reader and its callers now treat missing metadata as an expected, caller-safe state instead;
- extracting a package archive rejected valid ZIP directory entries (a path ending in `/` splits into a trailing empty segment) as a path-traversal attempt;
- artifact-signature and manifest-validation failures raised their own exception types instead of the package-management service's `PackageManagementException`, so a bad signature, digest, publisher or manifest crashed the request instead of returning a caller-safe error;
- removing a previously faulted installation violated the database check constraint that fault fields must be null outside the `Faulted` state, because removal never cleared them;
- `tools/build-package-artifact.sh` read `manifest.json` as raw JSON without stripping its required UTF-8 BOM and used the wrong (camelCase) property names, so it could not build the packages it was written for; the official package publish workflow that exercises it had never been run.


## Specification status

Authoritative documents cover product, architecture, UX, security, operations, testing, migration, phase gates and the issue blueprint through JulOS 1.0.

Implementation must not invent alternate behavior outside these specifications without updating `DECISIONS.md`.

## Open product decisions

These do not block feature implementation. The package signing key custody procedure blocks only the official signed-release gate (`REL-PKG-001`): the first official release cannot run until private-key custody and matching trusted public-key configuration are decided. It does not reintroduce a signed-only rule for custom content.

- final license
- final public JulOS domain
- final package signing key custody procedure (blocks the `REL-PKG-001` signed-release gate)
- final public package-registry host

## Backlog maintenance rule

Every implementation commit must update this file. GitHub issues and releases remain the detailed historical record.
