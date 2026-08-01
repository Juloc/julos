# Backlog

This file is the current high-level implementation state. Detailed work belongs in GitHub issues after the corresponding milestone begins.

Status values:

- `Planned`
- `Ready`
- `In progress`
- `Blocked`
- `Done`

## Current state

| ID | Work item | Status | Notes |
|---|---|---|---|
| M0.1 | Documentation baseline | Done | Foundation documents and repository rules are established. |
| M0.2 | Solution skeleton | Ready | First production-code task after documentation merge. |
| M0.3 | Local development stack | Planned | Depends on solution skeleton. |
| M0.4 | Continuous integration | Planned | Depends on build and test commands. |
| M1 | Core and authentication | Planned | Starts after repository foundation. |
| M2 | Desktop shell | Planned | Requires core app and persistence contracts. |
| M3 | Package runtime | Planned | Manifest design is documented; implementation follows desktop foundation. |
| M4 | Agent and widgets | Planned | Requires capability broker. |
| M5 | Browser and Remote | Planned | Julgate extraction starts only after remote contracts. |
| M6 | Docker and Proxmox | Planned | Requires agent, widgets and package runtime. |
| M7 | Files and Caddy | Planned | Caddy UI integration API is a separate repository change. |
| M8 | Discovery and hardening | Planned | Requires stable agents and problem model. |
| M9 | JulOS 1.0 | Planned | Requires all release acceptance criteria. |

## Next issue

### M0.2 — Create the solution skeleton

Scope:

- add Core, Server, Desktop, Contracts, Package SDK, Agent and test projects
- pin the supported SDK
- enable nullable reference types and analyzers
- add architecture tests that prevent product-specific dependencies in Core
- document the exact local build command

Out of scope:

- authentication
- database persistence
- desktop window behavior
- package implementations
- frontend design beyond a minimal host page

Acceptance criteria:

- clean checkout builds successfully
- all tests pass
- Core has no references to package or infrastructure projects
- project structure matches `docs/ARCHITECTURE.md`
- README and this backlog reflect the completed state

## Open product decisions

These do not block M0.2:

- final license
- final public JulOS domain
- package signing authority for external packages
- whether a public marketplace will exist after 1.0

## Backlog maintenance rule

Every merged implementation pull request must update this file. Completed detailed work can be summarized rather than retained as an ever-growing task log; GitHub issues and releases remain the historical record.
