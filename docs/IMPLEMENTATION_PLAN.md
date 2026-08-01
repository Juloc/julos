# Implementation plan

This plan defines the mandatory delivery order. `WORK_BREAKDOWN.md` contains the individual junior-ready issues for each phase.

Do not start a later phase while an earlier phase has unresolved architecture, security or acceptance failures. Limited parallel work is allowed only when the listed dependencies are already stable and the branches do not require temporary incompatible contracts.

## Phase 0 — Repository and engineering foundation

Work items: `FND-001` through `FND-007`.

Deliver:

- solution and project boundaries
- pinned toolchains
- TypeScript ES-module frontend build
- architecture tests
- repository validation commands
- development Compose stack
- pull-request CI
- version metadata foundation

Gate:

- clean checkout builds and tests with one documented command
- architecture tests enforce dependency direction
- development stack reaches healthy state
- no feature placeholder or product-specific Core dependency exists

## Phase 1 — Core platform model

Work items: `CORE-001` through `CORE-008`.

Deliver:

- identifiers, clock and revisions
- package lifecycle
- applications and launch targets
- layouts, windows and widgets
- session references
- Agents
- permissions and scopes
- problems, notifications and audit metadata

Gate:

- all domain invariants have unit tests
- Domain references only base libraries
- no Docker, Proxmox, Caddy, protocol or file-provider type appears in Core

## Phase 2 — Persistence, authentication and core APIs

Work items: `API-001` through `API-010`.

Deliver:

- PostgreSQL mappings and migrations
- optimistic concurrency
- local authentication
- backend authorization
- profile preferences
- common errors and correlation IDs
- background operations
- encrypted secret references
- audit service
- SignalR event hub

Gate:

- authenticated desktop foundation is reachable
- every mutation is permission-protected
- secrets are absent from responses and logs
- migration, concurrency and API integration tests pass

## Phase 3 — Desktop shell

Work items: `DESK-001` through `DESK-012`.

Deliver:

- Fluent 2 design system
- taskbar, launcher and command palette
- independent windows
- drag, resize, focus and z-order
- snapping
- layout persistence
- responsive desktop/tablet/mobile behavior
- notifications and problem center
- widget host
- accessibility and keyboard behavior

Gate:

- five simultaneous windows remain usable
- reload restores layout
- mobile mode uses safe task switching
- window interactions meet performance budget
- keyboard and accessibility checklist passes

## Phase 4 — Package platform

Work items: `PKG-001` through `PKG-012`.

Deliver:

- signed manifest schema
- artifact verification
- Runtime Manager
- package storage isolation
- package worker contract
- install, configure, enable, disable, update and remove
- capability broker
- signed frontend module host
- Package Manager UI
- reference test package

Gate:

- a deliberately faulted package does not stop Server or Desktop
- Runtime Manager cannot control unrelated containers
- package schemas are isolated
- packages communicate only through declared contracts and capabilities

## Phase 5 — Agent and host observability

Work items: `AGT-001` through `AGT-006`.

Deliver:

- one-time enrollment
- durable Agent identity
- outbound authenticated connection
- typed command dispatcher
- Linux host metrics
- host application and widgets
- Agent diagnostics

Gate:

- revoked Agents cannot reconnect
- no arbitrary shell command exists
- unavailable metrics are unknown rather than zero
- offline state appears without page reload

## Phase 6 — Remote and Browser

Work items: `REM-001` through `REM-008` and `BRW-001` through `BRW-005`.

Deliver:

- protocol-neutral Remote contracts
- evidence-based Julgate inventory
- shared transport extraction
- Remote worker and display client
- RDP, VNC and SSH adapters
- isolated Chromium runtime
- persistent, temporary and fixed-application browser modes

Gate:

- Julgate and JulOS share extracted implementation rather than copied code
- Browser reaches internal DNS and private addresses without public exposure
- session lifecycle remains separate from window lifecycle
- temporary profiles are cleaned
- supported protocol parity tests pass

## Phase 7 — Docker and Proxmox

Work items: `DKR-001` through `DKR-005` and `PVE-001` through `PVE-005`.

Deliver:

- Agent Docker capability
- Docker inventory, application, discovery and problems
- Proxmox connection, inventory, application, widgets and control
- Proxmox console through Remote capability

Gate:

- Docker socket is not publicly exposed
- read-only connections cannot mutate
- discovered applications require approval
- stable identities survive container recreation
- Proxmox write actions remain disabled by default

## Phase 8 — Files and Caddy

Work items: `FILE-001` through `FILE-007` and `CAD-001` through `CAD-002`.

Deliver:

- provider-neutral file contracts
- File Manager and transfer queue
- Agent-local, SMB, SFTP and WebDAV providers
- Remote and Browser transfer integration
- Caddy UI integration API
- JulOS Caddy package

Gate:

- path traversal and provider-root escape tests pass
- provider errors remain actionable
- Caddy package works without Docker package
- Caddy package uses only versioned Caddy UI APIs

## Phase 9 — Discovery and operational hardening

Work items: `DISC-001` through `DISC-005` and `OPS-001` through `OPS-007`.

Deliver:

- ARP, ICMP, mDNS, SSDP and optional SNMP observations
- discovery approval lifecycle
- safe mode
- backup and restore
- retention and cleanup
- security hardening
- performance and accessibility passes

Gate:

- discovery never grants management automatically
- ignored devices remain ignored
- clean restore test succeeds
- safe mode works without optional packages
- no high-severity unresolved security blocker remains
- documented performance and accessibility budgets pass

## Phase 10 — Release and migration

Work items: `REL-001` through `REL-008`.

Deliver:

- installation wizard and setup guide
- operational runbooks
- fresh-install validation
- supported-upgrade validation
- Julgate parity and migration
- signed release pipeline
- final backup/restore release test
- JulOS 1.0 release

Gate:

- every success criterion in `PRODUCT.md` passes
- all release artifacts use immutable versions or digests
- release notes and migration notes are complete
- documentation matches the release
- no critical or high release blocker remains

## Parallelization rules

Allowed examples:

- pure Desktop geometry tests may proceed while unrelated API implementation is reviewed, after the window contract is stable
- Caddy UI integration API can be developed in its repository while Files work proceeds
- RDP, VNC and SSH adapters may proceed in parallel after common Remote contracts and worker lifecycle are stable

Forbidden examples:

- building Docker UI before package frontend host and Agent contracts exist
- copying Julgate code while shared extraction design is unresolved
- implementing package installation with raw Docker access before Runtime Manager
- adding file providers before path and permission contracts
- starting release polish while restore is untested

## Milestone completion process

At the end of every phase:

1. run full repository validation
2. run the phase-specific integration checklist
3. review architecture, security and operational documents
4. update `BACKLOG.md`
5. update `WORK_BREAKDOWN.md` from implementation evidence
6. close completed issues
7. create only the next actionable phase issues
8. publish an internal milestone note when runtime behavior changed

## Change control

When implementation reveals that this order or architecture is incorrect:

1. stop the affected implementation
2. document the evidence
3. update or add an accepted decision
4. update every affected specification
5. adjust issue dependencies
6. continue only after the new design is internally consistent

Do not route around a blocked dependency with a temporary implementation.
