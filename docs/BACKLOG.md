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
| FND-001 / M0.2 | Solution skeleton | Ready | GitHub issue #2 is the next implementation task. |
| FND-002 | Architecture enforcement | Planned | Depends on solution skeleton. |
| FND-003 | Frontend toolchain | Planned | Depends on solution skeleton. |
| FND-004 | Validation entrypoints | Planned | Depends on backend and frontend skeleton. |
| FND-005 | Local development stack | Planned | Depends on solution skeleton. |
| FND-006 | Pull-request CI | Planned | Depends on validation commands. |
| FND-007 | Version metadata | Planned | Can follow solution skeleton. |
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

### FND-001 / M0.2 — Create the solution skeleton

Existing GitHub issue: #2.

Scope:

- create Domain, Application, Contracts, Infrastructure, Server, Desktop, Package SDK, Agent, Runtime Manager and test project foundations as defined in `TECHNICAL_SPECIFICATION.md`
- pin the supported .NET SDK
- enable nullable reference types and repository-wide warnings
- establish project references according to architecture
- document exact restore, build and test commands
- add no product feature logic

The issue may be updated before implementation so its project names match the complete specification. Do not implement later foundation items inside it unless they are necessary to make the skeleton build and validate.

Acceptance:

- clean checkout builds successfully
- all initial tests pass
- Domain has no outer-layer references
- project structure matches `ARCHITECTURE.md` and `TECHNICAL_SPECIFICATION.md`
- README and this backlog reflect the completed state

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
