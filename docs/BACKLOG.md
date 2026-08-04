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
| REM-004 | Remote worker and session orchestration | In progress | Authenticated package and user caller context now reaches capability providers and audit without trusting payload identity; durable session ownership, runtime allocation, lifecycle and cleanup remain. |
| Phase 6 | Remote and Browser | In progress | REM-001 through REM-003 are complete; REM-004 is in progress. Existing package shells are not complete session implementations. |
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

## Current REM-004 slice

Completed foundation:

- capability requests carry optional control-plane-produced caller metadata;
- the authenticated HTTP endpoint attaches the authorized package identity and authenticated user UUID;
- the broker rejects package-identity substitution before provider invocation;
- providers receive the verified caller context outside operation payloads;
- capability audit records retain the authenticated user identity;
- existing internal capability calls remain compatible through a package-only fallback;
- unit tests cover provider propagation, audit propagation and mismatch rejection.

Remaining scope:

- implement the user-owned Remote session service and exact idempotency;
- authorize provider, target, secret reference and network profile access;
- allocate only allowlisted provider runtimes through Runtime Manager;
- persist REM-001 lifecycle state and revisions;
- publish operation and lifecycle events;
- enforce inactivity and maximum-duration policy;
- implement explicit cancel, disconnect and cleanup behavior;
- map provider/runtime failures to caller-safe failures and operator-visible problems.

Acceptance:

- no provider launch material reaches package JavaScript;
- repeated create requests preserve exact idempotency within user and package ownership scope;
- expired or cancelled sessions cannot reconnect through stale display grants;
- closing or detaching a window follows explicit policy and does not silently kill a session;
- runtime failure leaves no orphan runtime and creates a deduplicated problem;
- implementation, tests and documentation pass the full repository validator.

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
