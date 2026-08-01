# Backlog

This file is the current high-level implementation state. Detailed future work belongs in `WORK_BREAKDOWN.md` and GitHub issues.

Status values:

- `Planned`
- `Ready`
- `In progress`
- `Blocked`
- `Done`

## Current state

| ID | Work item | Status | Notes |
|---|---|---|---|
| M0.1 | Documentation baseline | Done | Product, architecture and contributor rules established. |
| M0.1a | Complete project specification | Done | UX, technical design, data/API, security, operations, testing, Julgate migration and issue blueprint documented. |
| FND-001 / M0.2 | Solution skeleton | Done | Solution, central build configuration and the architecture test project build and pass. |
| FND-002 | Architecture enforcement | Done | Dependency graph, forbidden namespaces, product terminology and composition root are enforced by tests. |
| FND-003 | Frontend toolchain | Done | `src/JulOS.Desktop` type checks, tests and builds native ES modules without a bundler. |
| FND-004 | Validation entrypoints | Done | `tools/validate.sh` and `tools/validate.ps1` wrap one shared implementation. |
| FND-005 | Local development stack | Done | Server and PostgreSQL reach a healthy state; readiness verifies the database. |
| FND-006 | Pull-request CI | Done | One workflow runs `tools/validate.sh`, the same entry point developers run. |
| FND-007 | Version metadata | Done | The root `VERSION` file drives assemblies, the image label and the diagnostics endpoint. |
| Phase 0 | Repository and engineering foundation | Done | Gate passed: one command builds, tests and validates a clean checkout. |
| CORE-001 | Core primitives | Done | Revision, entity identifiers, identifier generator and the shared domain failure type. |
| CORE-002 | Package lifecycle | Done | The documented transition graph; a fault cannot be recorded without a reason. |
| CORE-003 | Applications and launch targets | Done | Identity is a stable key; the record holds no display text at all. |
| CORE-004 | Desktop layout | Done | Z-order is derived and gap-free; a layout belongs to one viewport class. |
| CORE-005 | Session references | Done | Protocol-neutral states; a closing window can never implicitly terminate a session. |
| CORE-007 | Problems, notifications and audit | Done | One problem per condition; the audit event exposes no way to change it. |
| CORE-008 | Permissions and scopes | Done | Pure evaluation, default deny; read never implies control. |
| CORE-006 | Agent model | Done | Revocation is terminal; a heartbeat cannot carry a measurement. |
| Phase 1 | Core platform model | Done | Gate passed: every domain invariant has tests and Domain references only base libraries. |
| API-006 | Problem Details and correlation IDs | Done | One failure shape for every path; correlation identifier on every response. |
| Phase 2 | Persistence, authentication and core APIs | In progress | `API-006` is done; the rest depends on the Core domain model. |
| Phase 3 | Desktop shell | Planned | Depends on authentication, APIs and frontend foundation. |
| Phase 4 | Package platform | Planned | Depends on stable Desktop host and Core contracts. |
| Phase 5 | Agent and host observability | Planned | Depends on package and event foundations. |
| Phase 6 | Remote and Browser | Planned | Depends on capability broker and Runtime Manager. |
| Phase 7 | Docker and Proxmox | Planned | Depends on Agent, packages, widgets and Remote for console. |
| Phase 8 | Files and Caddy | Planned | Includes separate Caddy UI integration API work. |
| Phase 9 | Discovery and operational hardening | Planned | Depends on stable Agent and package runtime. |
| Phase 10 | Release and Julgate migration | Planned | Requires all 1.0 release gates. |

## Next issue

### API-001 — Add PostgreSQL core persistence

Scope:

- a DbContext and mappings for the Phase 1 domain model
- the first migration and a migration command
- integration tests against a real PostgreSQL instance

Acceptance:

- an empty database migrates successfully
- constraints reflect domain invariants

## Specification status

Authoritative documents now cover:

- complete product concept
- desktop and window behavior
- package artifact, worker and lifecycle
- Runtime Manager boundary
- Agent model
- Browser and Remote model
- core data entities
- API and event conventions
- security and permissions
- deployment, backup, restore and safe mode
- test strategy and definition of done
- Julgate migration and archive criteria
- phase gates and individual issue blueprint through JulOS 1.0

Implementation must not invent alternate behavior outside these specifications without updating `DECISIONS.md`.

## Open product decisions

These block no current work item:

- final license
- final public JulOS domain
- final package signing key custody procedure
- final public package-registry host
- whether public third-party packages are supported after 1.0
- exact Remote transport implementation selected after Julgate inventory

## Backlog maintenance rule

Every implementation commit must update this file. Keep it focused on current status and the next actionable work. GitHub issues and releases remain the detailed historical record.
