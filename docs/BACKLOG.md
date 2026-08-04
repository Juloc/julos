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
| REM-003 | Shared transport implementation | In progress | Packable protocol catalog and Guacamole JSON-auth encoder are implemented with behavior tests and Remote worker consumption; publication and Julgate migration remain. |
| Phase 6 | Remote and Browser | In progress | REM-001 and REM-002 are complete; REM-003 is in progress. Existing package shells are not complete session implementations. |
| Phase 7 | Docker and Proxmox | Planned | Depends on Agent, packages, widgets and Remote for console. |
| Phase 8 | Files and Caddy | Planned | Includes separate Caddy UI integration API work. |
| Phase 9 | Discovery and operational hardening | Planned | Depends on stable Agent and package runtime. |
| Phase 10 | Release and Julgate migration | Planned | Requires all 1.0 release gates. |

## Current REM-003 slice

Scope:

- build and test `JulOS.Remote.Transport` from one JulOS source location
- consume the shared protocol catalog from the Remote worker
- preserve the existing Guacamole JSON-auth behavior through verifiable payload vectors
- document the immutable package and consumer boundary

Acceptance:

- solution build, tests and architecture gates pass
- the shared library is packable from the repository version
- no duplicated protocol catalog remains in the Remote worker
- secret-bearing intermediate buffers are cleared by the encoder
- the next slice publishes the immutable artifact before Julgate changes

## Remaining REM-003 slices

1. publish the validated package with digest and provenance;
2. update Julgate to consume that exact package version;
3. remove the original Julgate payload/signing/encryption implementation;
4. validate and deploy-test Julgate;
5. mark REM-003 done only when both repositories use one implementation.

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
- exact Remote runtime composition after shared extraction and parity evidence

## Backlog maintenance rule

Every implementation commit must update this file. GitHub issues and releases remain the detailed historical record.
