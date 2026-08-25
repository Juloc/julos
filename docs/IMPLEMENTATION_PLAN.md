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
- the legacy Agent domain that becomes Host Connector through `HCON-002`
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

Work items: `DESK-001` through `DESK-016`.

Deliver:

- Fluent 2 design system
- taskbar, launcher and command palette
- independent windows
- drag, resize, focus and z-order
- snapping
- layout persistence
- responsive desktop/tablet/mobile foundation
- notifications and problem center
- widget host
- accessibility and keyboard behavior

Gate:

- five simultaneous windows remain usable
- reload restores layout
- current mobile mode uses safe task switching; PWA, device layouts, Phone Split and surface suspension remain in `MOB-001` through `MOB-010`
- window interactions meet performance budget
- keyboard and accessibility checklist passes

## Phase 4 — JulOS extension-package platform

Work items: `PKG-001` through `PKG-012`.

Deliver:

- immutable manifest schema and the initial signed-artifact policy
- artifact verification
- Runtime Manager
- package storage isolation
- package worker contract
- install, configure, enable, disable, update and remove
- capability broker
- trusted frontend module host
- Package Manager UI
- reference test package

Gate:

- a deliberately faulted package does not stop Server or Desktop
- Runtime Manager cannot control unrelated containers
- package schemas are isolated
- packages communicate only through declared contracts and capabilities

## Phase 5 — Legacy Agent and host-observability foundation

Work items: `AGT-001` through `AGT-006`.

Deliver:

- one-time enrollment
- durable host-side identity
- outbound authenticated connection
- typed command dispatcher
- Linux host metrics
- host application and widgets
- legacy Agent diagnostics

Gate:

- revoked enrolled hosts cannot reconnect
- no arbitrary shell command exists
- unavailable metrics are unknown rather than zero
- offline state appears without page reload

This phase is complete under the former Agent terminology. `HCON-002` performs the one atomic product/API/process cutover; HCON-001 and HCON-003 through HCON-005 prepare, validate and extend that cutover. The completed Agent behavior remains the migration source, not a parallel runtime.

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

## Phase 6A — Product realignment foundations

Work items: `SPEC-001`, `STAB-001`, `DB-001`, `HCON-001` through `HCON-005`, `MOB-001` through `MOB-010`, `CAT-001`, `CAT-002`, `PKG-013` and `PKG-014`.

Delivery uses dependency lanes, not one phase-wide serial blockade:

1. record the accepted target concepts and reconcile every specification (`SPEC-001`), then integrate the real-Kestrel regression fix (`STAB-001`);
2. start `DB-001`, `HCON-001` and `MOB-001` in parallel; `MOB-002` follows MOB-001 and does not wait for Host Connector;
3. after STAB-001, CAT-001 may run independently; after DB-001, HCON-002, MOB-003/004 and CAT-002 may advance according to their Work Breakdown dependencies;
4. HCON-003 deployment validation and HCON-004 typed adapters can proceed in parallel after HCON-002; HCON-005 waits for both;
5. MOB-005 through MOB-010 follow their own layout/Surface/Browser/Remote dependencies and do not wait for unrelated HCON work;
6. PKG-013 follows CAT-001; PKG-014 follows isolation plus CAT-002. No unsigned unknown native code runs before both gates.

Gate:

- no current public/product Agent path remains and upgraded identities/data survive PostgreSQL and SQLite;
- Host Connector has no generic shell, command API, arbitrary TCP proxy or Docker proxy;
- PWA caching cannot retain authenticated/API/session content;
- Phone never exposes more than two foreground apps and Tablet supports multiple visible apps;
- shared/device/fresh layout resolution and logical multi-display slots pass upgrade and concurrency tests;
- suspended Browser/Remote surfaces do not terminate their runtime Sessions;
- unsigned/unknown definitions warn, invalid claimed signatures fail, and unknown native code cannot execute in the Shell origin.

The remaining Host Connector tunnel slice of `WEB-001` and rendered Remote/Browser deployment validation complete after `HCON-005`; Phase 7 cannot start before those gates are green.

### Open remote-branch disposition

- `origin/agent/fix-package-route-fallback` contains one current fix commit (`31a11ba`) for Kestrel package-action routing plus its real-host smoke stage. Integrate it only as `STAB-001`, resolve the documentation overlap against current `QUALITY_AND_TESTING.md`, run the real smoke and full validation, then delete the remote branch after `main` contains the verified commit.
- `origin/agent/docker-phase-completion` diverges from merge base `6efca54` and has two unique Agent-era commits (`7c2659f`, `4804758`) without later `main` work. Do not merge or rebase it as a product branch. During `DKR-001`, port only reviewed Docker client validation, bounded-operation behavior and tests that fit `docker.inventory/1` or `docker.control/1`; during `DKR-007`/`DKR-008`, implement the new typed app-deployment contract from the specification. Record which old tests were ported or rejected, then delete the branch.

Local linked worktree branches are contributor tooling state, not release inputs. They are never merged merely because they exist; cleanup is an explicit local maintenance action after verifying that their commits are reachable or superseded.

## Phase 7 — Open application catalog, Docker and Proxmox

Work items: `CONN-001`, `APP-001` through `APP-006`, `API-011`, `CAT-003`, `REL-CAT-001`, `DKR-001` through `DKR-008`, `REM-009` and `PVE-001` through `PVE-005`.

Deliver:

- Host Connector Docker adapter with strict read/control/app/terminal contracts
- catalog connection, image, standard Compose and optional native-extension delivery
- Docker inventory, managed/adopted/external ownership, application, discovery and problems
- Store, custom-source management and App Builder
- app update diff, backup/restore and safe uninstall
- provider-neutral terminal transport and explicit container terminal
- Proxmox connection, inventory, application, widgets and control
- Proxmox console through Remote capability

Gate:

- Docker socket is not exposed to Server, Runtime Manager or clients
- read-only connections cannot mutate
- a Connector mutates only resources carrying the selected installation's stable ownership
- unsigned definitions remain installable after warning and image tags are locked to digests
- backup precedes destructive update/removal and shared/external data is retained
- Hermes uses the generic Compose/inventory/terminal path with no special platform code
- discovered applications require approval
- stable identities survive container recreation
- Proxmox write actions remain disabled by default

## Phase 8 — Files and Caddy

Work items: `FILE-001` through `FILE-007` and `CAD-001` through `CAD-002`.

Deliver:

- provider-neutral file contracts
- File Manager and transfer queue
- Host Connector-local, SMB, SFTP and WebDAV providers
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

Work items: `HCON-006` and `REL-001` through `REL-008`.

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

- building Docker UI before package frontend, catalog and Host Connector contracts exist
- copying Julgate code while shared extraction design is unresolved
- implementing user application installation through Server or Runtime Manager Docker access
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
