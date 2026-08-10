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
| DESK-013 | Browser first-run and sign-in | Done | Fresh deployments can create the initial administrator and subsequent sessions can sign in entirely through the production Desktop shell. |
| DESK-014 | Production shell composition | Done | Enabled package apps and persisted widgets use the existing launcher/window/taskbar/frontend/persistence stack; Core Settings, Package Manager, Agent status, notifications and problems are normal desktop windows, and package lifecycle changes refresh the catalog live. |
| DESK-015 | Cross-platform desktop interaction pass | Done | Shared responsive rules, Pointer Events, full-screen state, minimized taskbar state, shell keyboard handling and the existing Alt-Tab switcher are wired into production; deployed Windows/macOS/touch acceptance remains a release gate. |
| DESK-016 | Appearance and personalization completion | Done | System/light/dark theme, reduced motion, Fluent-derived tokens and the JulOS accent system are active; server-confirmed theme and motion changes apply without reload. |
| REL-PKG-001 | Official package artifact and signing pipeline | In progress | Reproducible Host Metrics/Remote/Browser ZIP builds and full-archive SHA-256/ECDSA-P256-P1363 verification are implemented; a stable private signing key, matching trusted public-key configuration and the first real signed release run remain. |
| REL-ALPHA-007 | Published Desktop web root | Done | The container publishes Desktop assets through the Server web root and the release smoke test verifies `/` plus the main ES module. |
| Phase 3 | Desktop shell | Done | DESK-013 through DESK-016 are implemented; deployed cross-platform acceptance remains part of the release gate. |
| PKG-001 | Package manifest schema | Done | Versioned strict manifest, runtime, permissions, apps, widgets, capabilities, migrations and frontend declarations. |
| PKG-002 | Artifact verification | Done | Complete package archives are digest- and trusted-publisher-signature-verified before they are opened or extracted. |
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
| Phase 5 | Agent and host observability | Done | Agent enrollment, transport, command authorization, Host Metrics provider and compatibility diagnostics are implemented and validated against a real deployed Agent, Server and installed package. |
| REM-001 | Protocol-neutral Remote session contracts | Done | Core owns generic contracts, validation, lifecycle and exact idempotency; concrete protocol identities remain in the Remote package. |
| REM-002 | Julgate inventory and extraction boundaries | Done | Verified Julgate responsibilities are mapped to shared transport, Remote, Runtime Manager, Desktop, Files, Browser, migration-only code or explicit rejection. |
| REM-003 | Shared transport implementation | Done | `JulOS.Remote.Transport` has immutable published releases consumed by JulOS Remote and Julgate; additive RDP/VNC/SSH policy versions preserve the shared transport boundary. |
| REM-004 | Remote worker and session orchestration | Done | Exact-idempotent ownership, policy-gated runtimes, authenticated provider events, lifecycle enforcement, cleanup, explicit detach and active-session resume authorization are implemented and validated. |
| REM-005 | Functional Remote display client and same-origin transport | In progress | Repository acceptance is complete through PRs #39 and #40; deployed provider, browser and Android validation remains in #37. |
| REM-006 | RDP provider integration | In progress | PR #42 adds additive explicit Guacamole 1.6.0 security, certificate, resize and clipboard policy plus distinct account-unavailable failure handling; real provider integration and deployed validation remain in #41. |
| REM-007 | VNC provider integration | In progress | PR #44 passes full repository validation with explicit VNC authentication, resize, clipboard, display and bounded retry policy; deployed VNC evidence remains in #43. |
| REM-008 | SSH provider integration | In progress | PR #46 passes full repository validation and published transport 0.3.0 with explicit password/private-key/NONE authentication, host-key verification, timeout, keepalive and terminal policy. Deployed SSH is validated end to end (a real Server, Runtime Manager, signed `de.juloc.julos.remote` package and provider image reached `connected` with live guacd bytes over the same-origin display endpoint; see the evidence below and `docs/REMOTE-HANDOVER.md`). Re-confirmation on current `main` with the published provider image and formal close-out of issue #45 remain. |
| BRW-001 | Isolated Chromium runtime image | In progress | PR #48 adds a pinned unprivileged Chromium image, internal VNC endpoint, bounded Runtime Manager definition, health/cleanup logic and immutable attested GHCR publication. Publication is verified: the digest-pinned multi-architecture image (`ghcr.io/juloc/julos-browser-runtime@sha256:933a2a8…`, `linux/amd64` `sha256:7a1b75c…` + `linux/arm64` `sha256:a00039f…`) is published, provenance-attested (`gh attestation verify` succeeds) and staged into the official package catalog. The image enforces a required eight-character display password checked before any display process starts, an unprivileged `10001:10001` user and a single exposed port `5900` (`packages/JulOS.Browser/runtime/Dockerfile`, `browser-runtime.sh`). A live container smoke run (deploy test) and the missing publish-time lifecycle smoke test remain; see `docs/BROWSER-RUNTIME.md`. |
| BRW-002 | Browser profiles and network profiles | In progress | User-bound persistent/application profile policy, non-persistent temporary mode, exact administrator-allowlisted Runtime Manager networks, opaque proxy secret references and provider-neutral SQLite/PostgreSQL package metadata storage are implemented and unit-tested. The Browser worker now handles create/list/delete for profiles and network profiles through the generic `interactive.profiles` worker-command contract (`InteractiveProfilesWorkerCommands`, owner-scoped, proxy secret values never returned), covered by `tests/JulOS.Browser.Worker.Tests`. The Core `InteractiveProfilesCapabilityProvider` dispatches those commands through the generic `interactive.profiles/1.0.0` capability (registered in the broker, granted to the Browser package by its signed manifest), so profile and network-profile creation is now reachable end to end through `POST /api/v1/packages/de.juloc.julos.browser/capabilities/interactive.profiles/{operation}`; `tests/JulOS.Infrastructure.Tests` covers the provider's dispatch, contract, caller and failure paths. The Desktop management UI is the remaining piece (issue #60). |
| BRW-003 | Browser package worker/session orchestration | In progress | The generic `interactive.session/1.0.0` capability, package-worker command boundary, Runtime Manager allocation, HMAC-bound provider callbacks, secret-environment credential handoff and terminal-session cleanup are implemented and repository-validated; deployed provider/browser evidence and orchestration test coverage remain (issue #61). |
| BRW-004 | Full Browser application | Planned | Tabs, address field, navigation, downloads and session status remain. |
| BRW-005 | Fixed web-application mode | Planned | App-branded fixed targets and optional minimal chrome remain. |
| Phase 6 | Remote and Browser | In progress | Remote repository acceptance is largely complete with deployed evidence open. BRW-001 and BRW-003 are repository-complete pending deployed evidence, BRW-002 policy and storage are implemented pending issue #60; BRW-004 and BRW-005 remain. |
| Phase 7 | Docker and Proxmox | Planned | Depends on Agent, packages, widgets and Remote for console. |
| Phase 8 | Files and Caddy | Planned | Includes separate Caddy UI integration API work. |
| Phase 9 | Discovery and operational hardening | Planned | Depends on stable Agent and package runtime. |
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

REM-005 has a token-free graphical descriptor and authenticated same-origin WebSocket proxy. PR #39 builds the existing Remote custom element with the official Apache Guacamole 1.6.0 browser artifact, pinned by ZIP and library SHA-256. The browser, JulOS proxy and hidden provider negotiate one exact WebSocket subprotocol before frames are forwarded. One `Guacamole.Keyboard` receives desktop and mobile `InputSink` events, `Ctrl+Alt+Shift+Escape` releases capture and resets pressed keys, exactly one pointer or touchscreen adapter is active, resize is collapsed through one 150 ms scheduler, and reconnect, full-screen, failure and teardown states are visible. The remaining REM-005 work is deployed provider validation and any provider-specific UX defects found there.

REM-006 keeps RDP ownership inside `JulOS.Remote.Transport` and the Remote package. The additive options contract preserves the published 0.1.0 constructor while validating exact Guacamole security modes, strict/ignore/TOFU/pinned certificate policy, display-update/reconnect resize policy and directional clipboard settings. NLA-based modes require credentials before launch. `remote.account_unavailable` remains distinct from `remote.authentication_failed` when a trusted provider can classify the target account state.

REM-007 keeps VNC ownership in the same provider boundary and adds no new orchestration layer. The existing JSON-auth encoder validates and maps password authentication, dynamic or fixed display resize, clipboard direction and encoding, cursor behavior, color depth, read-only and local-input policy, compression, quality and a bounded retry count. Omitting `VncOptions` preserves the previous payload, while VNC options supplied to RDP or SSH fail closed. Transport version `0.2.0` contains the additive RDP and VNC provider-policy release and avoids overwriting immutable `0.1.0`.

REM-008 uses the same flat encoder and one additive options record. It validates password, OpenSSH private-key and NONE authentication; strict single-entry host-key verification; bounded timeout and keepalive; and explicit terminal font settings. Caller-owned private-key and passphrase memory remains provider-local, omitted `SshOptions` preserve the previous SSH payload, and SSH options supplied to RDP or VNC fail closed. Transport version `0.3.0` contains this additive SSH policy and was published after full repository validation.

Deployed REM-005..008 provider validation started by tracing how a Remote session's target credential reaches the provider runtime and found that it never did: `PostgresRemoteSessionProvisioner` validated `SecretReferenceId` metadata but only ever called `ISecretReferenceService.ReadAsync`, which by design returns non-secret metadata; the decrypting `ISecretLeaseService.AcquireAsync` (D029) had zero callers anywhere in the repository, for Remote or for the Browser display credential it was already documented as backing. See decision D030: the provisioner now creates (or idempotently reuses) a durable `remote.session.credential` Operation scoped to the session owner and caller package, drives it to `Running`, leases the secret, and forwards the decrypted bytes as one opaque Base64 `JULOS_REMOTE_TARGET_CREDENTIAL` runtime secret-environment entry. `tests/JulOS.Integration.Tests/Remote/RemoteSessionServiceTests.cs` asserts the credential round-trips through the full HTTP-backed provisioning flow.

The "exact provider runtime composition" open decision is resolved by D031 and `docs/REMOTE-PROVIDER-RUNTIME.md`: one image bundles guacd (built from official source against the same Ubuntu base as the web application, since the upstream guacd image is Alpine and not binary-compatible with it), the unmodified official Guacamole web application (copied verbatim, including its own entrypoint scripts and bundled JSON-auth extension), and a new minimal `JulOS.Remote.ProviderBridge` tool that maps the existing `JULOS_REMOTE_*` environment contract onto the already-published `JulOS.Remote.Transport` library's `GuacamoleJsonLaunchEncoder` — no new transport or protocol logic.

REM-008 SSH is now validated fully deployed: a real JulOS Server, a real Runtime Manager container with a Docker-socket mount, the real signed and installed `de.juloc.julos.remote` package, and the provider runtime image were run together against a real `linuxserver/openssh-server` target. Creating a session through the real `remote.session` capability reached `connected` with a real `connectedAtUtc`, and a WebSocket upgrade against JulOS Server's own `/api/v1/remote/sessions/{id}/display` endpoint (real authentication, real revision-matched display descriptor) returned `101 Switching Protocols` with the negotiated `guacamole` subprotocol echoed back and live guacd protocol bytes streaming from the real target. That run found and fixed two further Runtime Manager defects, both structural rather than incidental — see D032: `docker container ls` always reports a bare short image ID for any digest-referenced container (verified on both a local and a registry-pulled image), so the identity check that compares against the requested digest reference rejected every digest-pinned runtime unconditionally; and `RuntimePolicy.LooksLikeSecretName` did not recognize `CREDENTIAL`, so the D030 credential entry never passed Runtime Manager's own validation. It also found that `deploy/compose/compose.yaml`'s `migrate` service passed only `--migrate-database` as the container command, which the image's `exec gosu "$@"` entrypoint cannot resolve to an executable; it now passes the full `dotnet /application/JulOS.Server.dll --migrate-database` invocation.

VNC and RDP exercise the same provider runtime, credential path and display transport and were not independently re-run end to end; only SSH was. The provider image is published to GHCR (`ghcr.io/juloc/julos-remote-provider@sha256:e9c9d61adb82e56370a5fdaa76344dab686b4afd90a2ce41fc82cfe3a510b643`, `linux/amd64`, provenance-attested and verified) and wired into a deployment through the opt-in `remote` Compose profile in `deploy/compose/compose.remote.yaml`; see `docs/REMOTE-PROVIDER-RUNTIME.md`. That published digest is now pinned as the committed default of the `remote` profile (overridable via `JULOS_REMOTE_PROVIDER_0_IMAGE`). The end-to-end deployed RDP/VNC and browser/Android validation remains open.

AGT-004 and AGT-005 were validated by enrolling the released Agent against a running JulOS Server, confirming every required host metric reaches the Server with the documented first-sample-unknown/later-sample-delta pattern, then building, signing and installing the official Host Metrics package through the real Package Manager lifecycle (install, configure, enable), opening its application and widget in the running Desktop, and confirming live, offline and disabled-package capability-revocation states end to end. That deployed check found and fixed defects that no existing test had reached, because the Package Manager HTTP API had no integration coverage before `tests/JulOS.Integration.Tests/Packages/PackageEndpointTests.cs`:

- the antiforgery cookie and Identity session cookie used `CookieSecurePolicy.Always`, which makes ASP.NET Core's antiforgery middleware throw on every request when the connection is not HTTPS; the documented single-container alpha deployment binds Kestrel to plain loopback HTTP, so every antiforgery-protected mutation failed (see decision D027);
- `AgentEndpoints` and `PackageCapabilityEndpoints` passed the raw ASP.NET Core `HttpContext.TraceIdentifier` as an audit correlation identifier; its default format contains a colon, which the audit correlation-identifier validator rejects, crashing agent enrollment-token creation and package capability invocation; both now use the existing `CorrelationId.Get` helper, and its own fallback path is now sanitized the same way as a caller-supplied value;
- the `permission:core.package.read` and `permission:core.package.manage` authorization policies were referenced by every package endpoint but never registered, so the entire Package Manager HTTP API failed with an authorization-policy-not-found error;
- listing, installing or removing a package whose installation faulted before it wrote its metadata file crashed with an unhandled `FileNotFoundException`; the metadata reader and its callers now treat missing metadata as an expected, caller-safe state instead;
- extracting a package archive rejected valid ZIP directory entries (a path ending in `/` splits into a trailing empty segment) as a path-traversal attempt;
- artifact-signature and manifest-validation failures raised their own exception types instead of the package-management service's `PackageManagementException`, so a bad signature, digest, publisher or manifest crashed the request instead of returning a caller-safe error;
- removing a previously faulted installation violated the database check constraint that fault fields must be null outside the `Faulted` state, because removal never cleared them;
- `tools/build-package-artifact.sh` read `manifest.json` as raw JSON without stripping its required UTF-8 BOM and used the wrong (camelCase) property names, so it could not build the packages it was written for; the official package publish workflow that exercises it had never been run.

BRW-001 adds one runtime image rather than a Browser-specific container subsystem. The image pins its Debian base by digest and Chromium by exact package version, runs Xvfb, Openbox, x11vnc and Chromium as UID/GID 10001, requires a runtime-only VNC secret, exposes only internal port 5900 and removes all temporary state during shutdown. One JSON definition declares the Runtime Manager limits and secret input. One integration-branch workflow performs the full validator, lifecycle smoke test, immutable multi-architecture publication and image provenance attestation.

BRW-002 keeps Browser profile state inside the Browser package boundary. Retained profile metadata is owner-scoped and stored in the package database, temporary profiles have no persistent volume, runtime-network selection is restricted to an administrator allowlist, and optional proxy credentials remain opaque secret references. The generic process-worker supervisor now passes the package database provider explicitly, so the Browser worker supports both SQLite and PostgreSQL without inferring provider type from schema names or connection strings. `tests/JulOS.Browser.Worker.Tests` covers `BrowserProfilePolicy` validation, ownership enforcement and deterministic runtime storage naming. Creating a profile or a network profile has no reachable caller yet; BRW-003 only reads existing profiles and network profiles when it resolves a session plan. Issue #60 tracks exposing profile and network-profile creation through the existing capability/package-worker-command pattern.

BRW-003 adds the generic `interactive.session/1.0.0` capability instead of a Browser-specific Core contract, so any package needing one isolated interactive runtime can reuse it. `IJulOsPackageCommandHandler`/`PackageWorkerCommand` give Core one bounded private channel to ask an already-running package worker to resolve its own URL/profile/network/runtime-image/display policy into a runtime plan; the Browser worker's `ResolveInteractiveSessionPlan` handler enforces profile ownership, profile-mode match and the administrator network allowlist before producing a plan, and `tests/JulOS.Browser.Worker.Tests/BrowserWorkerCommandTests.cs` covers the success and every rejection path. `InteractiveSessionCapabilityProvider` allocates the runtime through the existing Runtime Manager and Remote presentation path, restricts the Remote target to the narrow `julos-interactive-*` prefix, and hands the short-lived display credential to the runtime only through Runtime Manager's secret-environment channel and an encrypted Secret Reference. `InteractiveSessionCleanupService` runs inside the existing `RemoteSessionLifecycleWorker` reconciliation pass instead of a second scheduler or session table. Deployed provider/browser validation and orchestration test coverage for the Core-side capability provider and cleanup service remain open in issue #61.

## Specification status

Authoritative documents cover product, architecture, UX, security, operations, testing, migration, phase gates and the issue blueprint through JulOS 1.0.

Implementation must not invent alternate behavior outside these specifications without updating `DECISIONS.md`.

## Open product decisions

These do not block current implementation:

- final license
- final public JulOS domain
- final package signing key custody procedure
- final public package-registry host
- public third-party package support after 1.0

## Backlog maintenance rule

Every implementation commit must update this file. GitHub issues and releases remain the detailed historical record.
