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
| FND-007 | Version metadata | Ready | Next implementation task. |
| Phase 1 | Core platform model | Planned | Starts after Phase 0 gate. |
| Phase 2 | Persistence, authentication and core APIs | Planned | Depends on Core domain model. |
| Phase 3 | Desktop shell | Planned | Depends on authentication, APIs and frontend foundation. |
| Phase 4 | Package platform | Planned | Depends on stable Desktop host and Core contracts. |
| Phase 5 | Agent and host observability | Planned | Depends on package and event foundations. |
| Phase 6 | Remote and Browser | Planned | Depends on capability broker and Runtime Manager. |
| Phase 7 | Docker and Proxmox | Planned | Depends on Agent, packages, widgets and Remote for console. |
| Phase 8 | Files and Caddy | Planned | Includes separate Caddy UI integration API work. |
| Phase 9 | Discovery and operational hardening | Planned | Depends on stable Agent and package runtime. |
| Phase 10 | Release and Julgate migration | Planned | Requires all 1.0 release gates. |

## Next issue

### FND-007 — Add version and release metadata

Scope:

- one repository version source
- assembly and image version propagation
- version reachable in diagnostics and in the Desktop
- release-note template

Acceptance:

- one version change updates all build outputs
- no `latest` dependency is required

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

These do not block FND-001:

- final license
- final public JulOS domain
- final package signing key custody procedure
- final public package-registry host
- whether public third-party packages are supported after 1.0
- exact Remote transport implementation selected after Julgate inventory

## Backlog maintenance rule

Every implementation commit must update this file. Keep it focused on current status and the next actionable work. GitHub issues and releases remain the detailed historical record.
