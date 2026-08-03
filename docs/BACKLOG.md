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
| AGT-002 | Agent identity and outbound connection | In progress | The executable transport, heartbeat, bounded reconnect and revocation behavior exist; first-run enrollment and protected local credential persistence remain. |
| AGT-003 | Agent command dispatcher | In progress | Typed polling, deadlines and `diagnostics.snapshot` execution exist; server-side advertised-command enforcement remains. |
| AGT-004 | Linux system metrics collectors | In progress | CPU, memory, load, uptime, storage and network collection exists; deployed-host validation remains. |
| AGT-005 | Host metrics package and widgets | Blocked | Frontend requests `host.metrics.read`, but no runtime capability provider currently exists. |
| AGT-006 | Agent diagnostics and update foundation | In progress | Diagnostics snapshot and explicit update policy exist; compatibility diagnostics and installation/update runbook remain. |
| Phase 5 | Agent and host observability | In progress | Server control plane and executable Agent runtime are present, but enrollment bootstrap and the Host Metrics provider block phase completion. |
| Phase 6 | Remote and Browser | Planned | Depends on capability broker and Runtime Manager. Existing package shells are not complete session implementations. |
| Phase 7 | Docker and Proxmox | Planned | Depends on Agent, packages, widgets and Remote for console. |
| Phase 8 | Files and Caddy | Planned | Includes separate Caddy UI integration API work. |
| Phase 9 | Discovery and operational hardening | Planned | Depends on stable Agent and package runtime. |
| Phase 10 | Release and Julgate migration | Planned | Requires all 1.0 release gates. |

## Next issue

### AGT-002 — Complete first-run enrollment and protected identity persistence

Scope:

- accept one short-lived enrollment token only for an unprovisioned Agent
- redeem it over the configured HTTPS endpoint
- persist the returned Agent ID and credential atomically with owner-only access
- restart from the protected identity without retaining the enrollment token
- fail explicitly on malformed, insecure or unreadable identity state

Acceptance:

- a clean Agent installation enrolls once and reconnects after restart
- the enrollment token and plaintext credential are never logged
- non-owner access to the persisted identity is rejected on supported Linux hosts
- revoked credentials cannot reconnect

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
- exact Remote transport selected after Julgate inventory

## Backlog maintenance rule

Every implementation commit must update this file. GitHub issues and releases remain the detailed historical record.
