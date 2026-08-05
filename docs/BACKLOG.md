# Backlog

This file is the current high-level implementation state. Detailed future work belongs in `WORK_BREAKDOWN.md` and GitHub issues.

Status values: `Planned`, `Ready`, `In progress`, `Blocked`, `Done`.

## Current state

| ID | Work item | Status | Notes |
|---|---|---|---|
| Phase 0 | Repository and engineering foundation | Done | Solution, architecture gates, validation, local stack, CI and version metadata. |
| Phase 1 | Core platform model | Done | Packages, applications, layouts, sessions, observability, permissions and Agents. |
| Phase 2 | Persistence, authentication and core APIs | Done | PostgreSQL, auth, authorization, profile, errors, operations, secrets, audit and events. |
| DESK-001..012 | Desktop shell | Done | Shell, windows, pointer interactions, snapping, taskbar, launcher, persistence, responsive modes, observability, widgets and accessibility. |
| Phase 3 | Desktop shell | Done | Complete Desktop foundation is implemented. |
| PKG-001 | Package manifest schema | Done | Versioned strict manifest, runtime, permissions, apps, widgets, capabilities, migrations and frontend declarations. |
| PKG-002 | Artifact verification | Done | Digest and trusted publisher signature verification reject modified or untrusted artifacts. |
| PKG-003 | Runtime Manager | Done | Authenticated sidecar controls only allowlisted JulOS-owned Docker runtimes. |
| PKG-004 | Package storage isolation | Done | Restricted PostgreSQL schema and role are provisioned per package. |
| PKG-005 | Worker control contract | Done | Validation, configure, register, start, stop and health contracts include deadlines. |
| PKG-006 | Install and configure lifecycle | Done | Verified idempotent install and recoverable configuration flow are implemented. |
| PKG-007 | Enable, disable and fault handling | Done | Worker lifecycle isolates failures from Core and preserves diagnosis. |
| PKG-008 | Update and removal | Done | Migration disclosure, bounded rollback and explicit data deletion are implemented. |
| PKG-009 | Capability broker | Done | Provider resolution, caller grants, deadlines and audit are implemented without package references. |
| PKG-010 | Package frontend host | Done | Same-origin integrity verification, closed Shadow DOM and token-free host context. |
| PKG-011 | Package Manager UI/API | Done | Read/manage permissions and lifecycle state including safe mode and fault visibility. |
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
| REM-003 | Shared transport implementation | Done | `JulOS.Remote.Transport` 0.1.0 is the single tested, immutable and attested implementation consumed by JulOS Remote and Julgate; both repositories validate successfully. |
| REM-004 | Remote worker and session orchestration | Done | Exact-idempotent ownership, policy-gated runtimes, authenticated provider events, lifecycle enforcement, cleanup, explicit detach and active-session resume authorization are implemented and validated. |
| REM-005 | Functional Remote display client and same-origin transport | In progress | PR #39 adds the official Apache Guacamole 1.6.0 client, token-free same-origin proxy, exact `guacamole` WebSocket negotiation, one keyboard/InputSink path, pointer/touch input, resize, full-screen, reconnect and teardown. Deployed provider validation remains. |
| Phase 6 | Remote and Browser | In progress | REM-001 through REM-004 are complete; REM-005 is functionally implemented in PR #39 and awaits final validation plus deployed provider testing. Browser remains planned. |
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

REM-005 has a token-free graphical descriptor and authenticated same-origin WebSocket proxy. PR #39 builds the existing Remote custom element with the official Apache Guacamole 1.6.0 browser artifact, pinned by ZIP and library SHA-256. The browser, JulOS proxy and hidden provider must all negotiate the exact `guacamole` WebSocket subprotocol before frames are forwarded. One `Guacamole.Keyboard` receives desktop and mobile `InputSink` events, exactly one pointer or touchscreen adapter is active, resize uses the protocol size instruction, and reconnect, full-screen, failure and teardown states are visible. The remaining REM-005 work is final repository validation, deployed provider validation and any provider-specific UX defects found there.

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
