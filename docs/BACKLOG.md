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
| AGT-004 | Linux system metrics collectors | In progress | CPU, memory, load, uptime, storage and network collection plus valid/missing/malformed fixture tests are complete; deployed Debian validation remains. |
| AGT-005 | Host metrics package and widgets | In progress | Persisted-metric provider, signed-manifest authorization, authenticated frontend bridge and live/stale/offline/error view logic are implemented; installed-package end-to-end validation remains. |
| AGT-006 | Agent diagnostics and update foundation | Done | Exact protocol negotiation, shared capability inventory, bounded reconnect diagnostics and a manual-only digest-verified update preparation contract are implemented, documented and fully validated. |
| Phase 5 | Agent and host observability | In progress | Agent enrollment, transport, command authorization, Host Metrics provider and compatibility diagnostics are implemented; deployed-host and installed-package validation remain in issues #14 and #7. |
| REM-001 | Protocol-neutral Remote session contracts | Done | Core owns generic contracts, validation, lifecycle and exact idempotency; concrete protocol identities remain in the Remote package. |
| REM-002 | Julgate inventory and extraction boundaries | Done | Verified Julgate responsibilities are mapped to shared transport, Remote, Runtime Manager, Desktop, Files, Browser, migration-only code or explicit rejection. |
| REM-003 | Shared transport implementation | Done | `JulOS.Remote.Transport` has immutable published releases consumed by JulOS Remote and Julgate; additive RDP/VNC/SSH policy versions preserve the shared transport boundary. |
| REM-004 | Remote worker and session orchestration | Done | Exact-idempotent ownership, policy-gated runtimes, authenticated provider events, lifecycle enforcement, cleanup, explicit detach and active-session resume authorization are implemented and validated. |
| REM-005 | Functional Remote display client and same-origin transport | In progress | Repository acceptance is complete through PRs #39 and #40; deployed provider, browser and Android validation remains in #37. |
| REM-006 | RDP provider integration | In progress | PR #42 adds additive explicit Guacamole 1.6.0 security, certificate, resize and clipboard policy plus distinct account-unavailable failure handling; real provider integration and deployed validation remain in #41. |
| REM-007 | VNC provider integration | In progress | PR #44 passes full repository validation with explicit VNC authentication, resize, clipboard, display and bounded retry policy; deployed VNC evidence remains in #43. |
| REM-008 | SSH provider integration | In progress | PR #46 passes full repository validation and published transport 0.3.0 with explicit password/private-key/NONE authentication, host-key verification, timeout, keepalive and terminal policy; deployed SSH evidence remains in #45. |
| BRW-001 | Isolated Chromium runtime image | In progress | PR #48 adds a pinned unprivileged Chromium image, internal VNC endpoint, bounded Runtime Manager definition, health/cleanup logic and immutable attested GHCR publication; deployed/publication evidence remains. |
| BRW-002 | Browser profiles and network profiles | In progress | User-bound persistent/application profile policy, non-persistent temporary mode, exact administrator-allowlisted Runtime Manager networks, opaque proxy secret references and provider-neutral SQLite/PostgreSQL package metadata storage are implemented and unit-tested; creating a profile or a network profile has no reachable caller yet (issue #60). |
| BRW-003 | Browser package worker/session orchestration | Planned | Runtime Manager allocation, generic session reference, policy, secret leasing and cleanup remain. |
| BRW-004 | Full Browser application | Planned | Tabs, address field, navigation, downloads and session status remain. |
| BRW-005 | Fixed web-application mode | Planned | App-branded fixed targets and optional minimal chrome remain. |
| Phase 6 | Remote and Browser | In progress | Remote repository acceptance is largely complete with deployed evidence open. BRW-001 is implemented, BRW-002 policy and storage are implemented pending issue #60; BRW-003 through BRW-005 remain. |
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

BRW-001 adds one runtime image rather than a Browser-specific container subsystem. The image pins its Debian base by digest and Chromium by exact package version, runs Xvfb, Openbox, x11vnc and Chromium as UID/GID 10001, requires a runtime-only VNC secret, exposes only internal port 5900 and removes all temporary state during shutdown. One JSON definition declares the Runtime Manager limits and secret input. One integration-branch workflow performs the full validator, lifecycle smoke test, immutable multi-architecture publication and image provenance attestation.

BRW-002 keeps Browser profile state inside the Browser package boundary. Retained profile metadata is owner-scoped and stored in the package database, temporary profiles have no persistent volume, runtime-network selection is restricted to an administrator allowlist, and optional proxy credentials remain opaque secret references. The generic process-worker supervisor now passes the package database provider explicitly, so the Browser worker supports both SQLite and PostgreSQL without inferring provider type from schema names or connection strings. `tests/JulOS.Browser.Worker.Tests` covers `BrowserProfilePolicy` validation, ownership enforcement and deterministic runtime storage naming. Creating a profile or a network profile has no reachable caller yet; BRW-003 only reads existing profiles and network profiles when it resolves a session plan. Issue #60 tracks exposing profile and network-profile creation through the existing capability/package-worker-command pattern.

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
- exact provider runtime composition within the accepted REM-004 boundaries

## Backlog maintenance rule

Every implementation commit must update this file. GitHub issues and releases remain the detailed historical record.
