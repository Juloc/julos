# Work breakdown

This file is the issue blueprint for JulOS. Each item is intended to become one GitHub issue unless its implementation proves too large to review safely. Do not combine unrelated items.

## How to execute an item

For every item:

1. read the required documents listed in `AGENTS.md`
2. verify every dependency is merged
3. restate the item scope and out-of-scope items in the commit message
4. add or update tests before declaring completion
5. update all affected Markdown files
6. run the repository validation command
7. commit only after acceptance criteria are satisfied

## Phase 0 — Repository and engineering foundation

### FND-001 — Create solution skeleton

Status: done.

Depends on: documentation baseline.

Deliver:

- projects and directories from `TECHNICAL_SPECIFICATION.md`
- pinned .NET SDK
- central build properties
- nullable reference types and warnings
- minimal test project
- documented build command

Acceptance:

- clean checkout restores, builds and tests
- Domain references only base libraries
- no empty product feature implementation exists

Implemented as: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `JulOS.slnx`, the eight `src` projects and `tests/JulOS.Architecture.Tests`.

`JulOS.Desktop` is created by `FND-003` together with its TypeScript toolchain. `JulOS.Domain.Tests` and `JulOS.Application.Tests` are created by the first `CORE` item that adds behavior, per decision `D023`. `JulOS.Agent` and `JulOS.RuntimeManager` are console executables whose entry points exit with a non-zero code and name the work item that implements them; neither reports a fake ready state.

### FND-002 — Add architecture enforcement

Status: done.

Depends on: FND-001.

Deliver:

- project-reference tests
- forbidden namespace tests
- package-to-package reference prohibition
- Contracts dependency checks

Acceptance:

- intentionally adding a forbidden reference fails the test
- rules match `ARCHITECTURE.md`

Implemented in `tests/JulOS.Architecture.Tests` as a complete allowed dependency table, compiled-metadata checks for persistence, web and host-resource types, a PascalCase-aware product terminology scanner and composition-root rules. Acceptance was verified by adding a `JulOS.Domain` to `JulOS.Contracts` reference and a Domain type named after a product; both were reported and then removed.

The package-to-package rule is reported as inconclusive until the first project exists under `packages/`, so its inactive state stays visible in the test run instead of appearing as a pass.

The Package SDK public-surface review from `QUALITY_AND_TESTING.md` section 2.3 is implemented by `PKG-001`, when the SDK gains its first public type.

### FND-003 — Establish frontend toolchain

Depends on: FND-001.

Deliver:

- TypeScript configuration
- native ES-module build
- development watch command
- production asset build
- no general SPA framework

Acceptance:

- type checking and production build run locally and in CI
- generated output is not committed unless explicitly required

### FND-004 — Implement repository validation entrypoints

Depends on: FND-001, FND-003.

Deliver:

- `tools/validate.ps1`
- `tools/validate.sh`
- shared underlying validation commands
- Markdown and manifest validation hooks

Acceptance:

- both entrypoints run equivalent checks
- any failed stage returns non-zero and identifies the stage

### FND-005 — Add development Compose stack

Depends on: FND-001.

Deliver:

- Server and PostgreSQL services
- development volumes
- safe example environment file
- health and readiness wiring

Acceptance:

- fresh `docker compose up` reaches healthy state
- no real secret is committed

### FND-006 — Add pull-request CI

Depends on: FND-004, FND-005.

Deliver:

- backend build and tests
- frontend type check and build
- PostgreSQL integration tests
- policy and documentation validation
- container build without push

Acceptance:

- local validation and CI use the same commands
- cache does not hide missing generated dependencies

### FND-007 — Add version and release metadata foundation

Depends on: FND-001.

Deliver:

- one repository version source
- assembly and image version propagation
- version shown in diagnostics and desktop footer/about
- release-note template

Acceptance:

- one version change updates all build outputs
- no `latest` dependency is required

## Phase 1 — Core platform

### CORE-001 — Implement core identifiers and clock abstraction

Depends on: FND-001.

Deliver stable IDs, UTC clock port and common revision value.

Acceptance:

- time-dependent tests use injected clock
- IDs are generated server-side

### CORE-002 — Implement package lifecycle domain model

Depends on: CORE-001.

Deliver states, valid transitions and fault metadata.

Acceptance:

- invalid transitions fail explicitly
- transition tests cover install through removal

### CORE-003 — Implement applications and launch-target domain model

Depends on: CORE-001.

Deliver application definitions, instance policies, launch targets and approval states.

Acceptance:

- stable keys and external identities are enforced
- display names are not identity fields

### CORE-004 — Implement desktop layout domain model

Depends on: CORE-001.

Deliver layouts, windows, widget placements, viewport classes and revisions.

Acceptance:

- invalid bounds and duplicate z-order normalization are tested
- mobile and desktop layouts remain separate

### CORE-005 — Implement session-reference domain model

Depends on: CORE-001.

Deliver protocol-neutral states and lifecycle policy.

Acceptance:

- window close and session termination are distinct
- invalid lifecycle transitions fail

### CORE-006 — Implement Agent domain model

Depends on: CORE-001.

Deliver identity, enrollment, state, capabilities and revocation.

Acceptance:

- revoked Agent cannot transition to connected
- last-seen is not interpreted as a metric value

### CORE-007 — Implement problem, notification and audit models

Depends on: CORE-001.

Deliver deduplication identity, state transitions, notification metadata and append-only audit contract.

Acceptance:

- repeated observations update one problem
- resolved problems can reopen on a new observation

### CORE-008 — Implement permission and scope model

Depends on: CORE-001.

Deliver permission strings, subject assignments and target scopes.

Acceptance:

- read and control permissions remain separate
- scope evaluation tests cover global, package and resource scopes

## Phase 2 — Persistence, authentication and core APIs

### API-001 — Add PostgreSQL core persistence

Depends on: CORE-002 through CORE-008, FND-005.

Deliver DbContext, mappings, first migration and migration command.

Acceptance:

- empty database migrates successfully
- constraints reflect domain invariants

### API-002 — Add optimistic concurrency

Depends on: API-001.

Deliver revision handling for layouts, settings, packages and connections.

Acceptance:

- stale update returns conflict with current revision
- silent last-write-wins does not occur

### API-003 — Add local authentication

Depends on: API-001.

Deliver initial admin setup, login, logout, secure cookies, lockout and session timeout.

Acceptance:

- desktop and APIs reject unauthenticated users
- login rate limiting is tested

### API-004 — Add role and permission authorization

Depends on: API-003, CORE-008.

Deliver backend policies and administrator role management foundation.

Acceptance:

- every mutation endpoint requires a policy
- unauthorized calls return 401 or 403 correctly

### API-005 — Add profile and preferences API

Depends on: API-003.

Deliver language, timezone, theme and motion preferences.

Acceptance:

- English and German are valid
- invalid timezone and locale fail validation

### API-006 — Add common Problem Details and correlation IDs

Depends on: FND-001.

Deliver middleware and stable error codes.

Acceptance:

- API errors include correlation ID
- stack traces and secrets are absent

### API-007 — Add operation-resource framework

Depends on: API-001, API-006.

Deliver queued/running/succeeded/failed/cancelled operations and progress events.

Acceptance:

- background work is not reported as success before completion
- operation failure retains a safe cause

### API-008 — Add secret-reference service

Depends on: API-001, API-004.

Deliver encrypted storage, opaque references, create/rotate/delete and lease port.

Acceptance:

- secret value is never returned after creation
- logs and audit tests contain no plaintext

### API-009 — Add audit service

Depends on: API-001, API-003.

Deliver append-only mutation audit and query API.

Acceptance:

- required security and infrastructure actions are audited
- audit details are sanitized

### API-010 — Add real-time event hub

Depends on: API-003, API-006.

Deliver versioned SignalR envelope, reconnect refresh rule and client subscription.

Acceptance:

- duplicate event does not duplicate client state
- reconnect triggers authoritative refresh

## Phase 3 — Desktop shell

### DESK-001 — Create shell and design tokens

Depends on: FND-003, API-003, API-005.

Deliver desktop surface, taskbar, theme tokens and localization foundation.

Acceptance:

- system, light and dark themes work
- English and German shell strings exist

### DESK-002 — Implement client API and event services

Depends on: API-006, API-010.

Deliver typed API client, Problem Details mapping, correlation display and reconnect behavior.

Acceptance:

- no raw authentication token is exposed to package modules
- offline and unauthorized are distinct states

### DESK-003 — Implement window store

Depends on: CORE-004, DESK-001.

Deliver deterministic open, focus, move, resize, minimize, restore, maximize and close commands.

Acceptance:

- unit tests cover state transitions
- five simultaneous windows are usable

### DESK-004 — Implement drag, resize and bounds

Depends on: DESK-003.

Deliver pointer and touch interaction with animation-frame updates.

Acceptance:

- no server request per pointer movement
- title bar cannot become permanently unreachable

### DESK-005 — Implement snapping

Depends on: DESK-004.

Deliver left, right, quarter and maximize snap previews and restore behavior.

Acceptance:

- taskbar bounds are respected
- keyboard shortcuts and pointer snapping agree

### DESK-006 — Implement taskbar and window switcher

Depends on: DESK-003.

Deliver grouped running apps, counts, minimized restore and Alt+Tab behavior.

Acceptance:

- single-instance and multi-instance apps behave correctly
- keyboard focus is predictable

### DESK-007 — Implement launcher and command palette

Depends on: CORE-003, DESK-002.

Deliver searchable applications, targets and permitted commands.

Acceptance:

- unauthorized commands are not executable
- 1000 applications remain searchable within performance budget

### DESK-008 — Implement layout persistence

Depends on: API-002, DESK-003.

Deliver debounced persistence, revisions and restore.

Acceptance:

- reload restores layout
- conflicting browser instances return and handle revision conflict

### DESK-009 — Implement responsive desktop modes

Depends on: DESK-003 through DESK-008.

Deliver desktop, tablet and mobile viewport behavior.

Acceptance:

- mobile uses task switching instead of unusable overlapping windows
- viewport layouts do not overwrite each other

### DESK-010 — Implement notifications and problem center shell

Depends on: CORE-007, API-010.

Deliver global notification center, problem center and deep-link host behavior.

Acceptance:

- color is not the sole severity signal
- repeated events do not spam notifications

### DESK-011 — Implement widget host

Depends on: DESK-008, API-010.

Deliver widget grid, size variants and status states.

Acceptance:

- package widget cannot edit another widget
- stale data is labeled with observation time

### DESK-012 — Accessibility and keyboard pass

Depends on: DESK-001 through DESK-011.

Deliver keyboard navigation, focus, reduced motion, zoom and screen-reader labels.

Acceptance:

- shell is operable without a pointer
- automated and manual checklist passes

## Phase 4 — Package platform

### PKG-001 — Define package manifest schema

Depends on: CORE-002, CORE-003.

Deliver versioned JSON schema, validation and fixtures.

Acceptance:

- unknown incompatible schema fails clearly
- permissions and runtime requirements are mandatory declarations

### PKG-002 — Implement package artifact verification

Depends on: PKG-001, API-008.

Deliver digest and signature verification with trust configuration.

Acceptance:

- modified artifact is rejected
- untrusted publisher cannot install

### PKG-003 — Implement Runtime Manager service

Depends on: FND-005, SEC requirements.

Deliver narrow authenticated runtime API and Docker ownership enforcement.

Acceptance:

- unrelated containers cannot be inspected or controlled
- privileged and arbitrary mount requests are rejected

### PKG-004 — Implement package storage isolation

Depends on: API-001, PKG-001.

Deliver package schema creation, restricted role and migration tracking.

Acceptance:

- one package cannot query another schema
- failed migration prevents enablement

### PKG-005 — Implement package worker control contract

Depends on: PKG-001, PKG-003.

Deliver health, configure, start, stop, validate and registration contract.

Acceptance:

- calls have authentication and deadlines
- worker failure cannot stop Server

### PKG-006 — Implement install and configure lifecycle

Depends on: PKG-002 through PKG-005, API-007.

Deliver install operation, configuration validation and disabled installed state.

Acceptance:

- install is idempotent by operation key
- configuration failure leaves package recoverable

### PKG-007 — Implement enable, disable and fault handling

Depends on: PKG-006.

Deliver worker start, registration, health monitoring and safe disable.

Acceptance:

- faulted package disappears from launcher but remains diagnosable
- core desktop stays usable

### PKG-008 — Implement update and removal

Depends on: PKG-007.

Deliver compatibility validation, migrations, rollback limits and data-retention choice.

Acceptance:

- irreversible migration is disclosed before update
- remove cannot delete data without explicit choice

### PKG-009 — Implement capability broker

Depends on: PKG-005, API-004.

Deliver provider registration, resolution, authorization and audit.

Acceptance:

- packages have no direct references
- unavailable provider returns explicit error

### PKG-010 — Implement package frontend host contract

Depends on: DESK-002, PKG-001.

Deliver signed module loading, integrity verification, Custom Element host context and theme/localization bridge.

Acceptance:

- package module receives no raw token or secret
- styles do not leak across Shadow DOM boundary

### PKG-011 — Implement Package Manager UI

Depends on: PKG-006 through PKG-010.

Deliver list, details, permissions, configuration, health, logs and lifecycle actions.

Acceptance:

- configuration-required and faulted states are clear
- safe mode can disable a package

### PKG-012 — Create reference test package

Depends on: PKG-010.

Deliver one minimal official test package with app, widget, worker, settings and intentional fault test mode.

Acceptance:

- all package platform paths are exercised
- package contains no product-specific infrastructure logic

## Phase 5 — Agent and host observability

### AGT-001 — Implement enrollment tokens

Depends on: API-003, API-008.

Deliver short-lived one-time enrollment tokens.

Acceptance:

- token cannot be reused
- expiry and audit are tested

### AGT-002 — Implement Agent identity and outbound connection

Depends on: AGT-001, CORE-006.

Deliver durable credentials, protocol negotiation and heartbeat.

Acceptance:

- revoked Agent cannot reconnect
- offline state appears without page reload

### AGT-003 — Implement Agent command dispatcher

Depends on: AGT-002.

Deliver typed allowlisted capability requests, deadlines, cancellation and output limits.

Acceptance:

- arbitrary command payload is impossible
- malformed requests fail safely

### AGT-004 — Implement system metrics collectors

Depends on: AGT-003.

Deliver CPU, memory, load, uptime, storage and network observations for Linux.

Acceptance:

- unavailable values are unknown, not zero
- observation timestamps are preserved

### AGT-005 — Implement host metrics package and widgets

Depends on: AGT-004, DESK-011, PKG-012.

Deliver package worker, host app and CPU/RAM/storage/network widgets.

Acceptance:

- widgets show live, stale, offline and error states
- detailed host view opens from widget

### AGT-006 — Implement Agent diagnostics and update foundation

Depends on: AGT-002.

Deliver version, capability inventory, reconnect diagnostics and future update contract without automatic update behavior.

Acceptance:

- incompatible Agent version is actionable
- no silent protocol downgrade

## Phase 6 — Remote and Browser

### REM-001 — Define protocol-neutral Remote contracts

Depends on: PKG-009, CORE-005.

Deliver session create, state, display, input, clipboard, transfer and lifecycle contracts.

Acceptance:

- no Guacamole type enters Core contracts
- Browser can use the same session model

### REM-002 — Complete Julgate inventory

Depends on: REM-001.

Deliver evidence-based component and parity inventory in Julgate.

Acceptance:

- every reusable and product-specific component is classified
- known keyboard and connection defects are recorded

### REM-003 — Extract shared transport implementation

Depends on: REM-002.

Deliver shared libraries consumed by Julgate and JulOS Remote.

Acceptance:

- no source duplication
- Julgate remains deployable

### REM-004 — Implement Remote worker and session orchestration

Depends on: REM-003, PKG-009, PKG-003.

Deliver session creation, runtime allocation, events, reconnect and cleanup.

Acceptance:

- active session survives window detach according to policy
- runtime crash creates a problem

### REM-005 — Implement remote display client

Depends on: REM-004, DESK-003.

Deliver display, resize, mouse, keyboard and full-screen client.

Acceptance:

- resize is debounced
- keyboard capture escape behavior is documented and tested

### REM-006 — Integrate RDP

Depends on: REM-004, REM-005.

Deliver credentials, domain, security, certificate policy, resize, clipboard and useful errors.

Acceptance:

- Android/mobile duplicate-key regression test exists
- invalid credentials and account-disabled errors remain distinguishable when upstream permits

### REM-007 — Integrate VNC

Depends on: REM-004, REM-005.

Acceptance:

- authentication, scaling, clipboard and reconnect tested

### REM-008 — Integrate SSH

Depends on: REM-004, REM-005.

Acceptance:

- password/key auth, host-key policy and terminal resize tested

### BRW-001 — Build Browser runtime image

Depends on: PKG-003, REM-004.

Deliver pinned Chromium image, unprivileged user, display endpoint and resource limits.

Acceptance:

- image contains no default credentials
- health and cleanup work

### BRW-002 — Implement Browser profiles and network profiles

Depends on: BRW-001, API-008.

Deliver persistent, temporary and application modes plus allowed network configuration.

Acceptance:

- users cannot share profiles
- temporary data is removed

### BRW-003 — Implement Browser package worker

Depends on: BRW-002, PKG-009.

Deliver runtime creation, session reference, policy and cleanup.

Acceptance:

- internal DNS and local address access works through configured network
- private URL is not exposed directly when policy forbids it

### BRW-004 — Implement full Browser application

Depends on: BRW-003, REM-005.

Deliver tabs, address field, navigation, downloads and session status.

Acceptance:

- multiple isolated windows work
- startup stages and failures are clear

### BRW-005 — Implement fixed web-application mode

Depends on: BRW-004, CORE-003.

Deliver app-branded launch target with optional minimal chrome.

Acceptance:

- app mode remains a full browser session, not iframe
- policy can allow opening in full browser mode

## Phase 7 — Docker and Proxmox

### DKR-001 — Implement Agent Docker capability

Depends on: AGT-003.

Deliver engine connection, read scope and allowlisted control actions.

Acceptance:

- Docker socket is not publicly exposed
- read-only scope cannot mutate

### DKR-002 — Implement Docker inventory worker

Depends on: DKR-001, PKG-005.

Deliver hosts, projects, services, containers, images, volumes and networks.

Acceptance:

- inventory uses stable service identity
- transient container ID changes do not duplicate resources

### DKR-003 — Implement Docker application UI

Depends on: DKR-002, PKG-010.

Deliver resource navigation, status, logs and actions.

Acceptance:

- write actions are permission-controlled and confirmed
- error cause remains visible

### DKR-004 — Implement Docker application discovery

Depends on: DKR-002, CORE-003.

Deliver label, manual, Compose, Caddy, port and heuristic evidence pipeline.

Acceptance:

- proposals require approval
- ignored proposals remain ignored

### DKR-005 — Implement Docker problems

Depends on: DKR-002, CORE-007.

Deliver unhealthy, restart-loop, stopped, unreachable, mount and resource conditions.

Acceptance:

- repeated observations deduplicate
- resolved conditions close correctly

### PVE-001 — Implement Proxmox connection validation

Depends on: API-008, PKG-005.

Deliver API token authentication, endpoint validation and TLS policy.

Acceptance:

- credentials are opaque
- untrusted certificate behavior is explicit

### PVE-002 — Implement Proxmox inventory

Depends on: PVE-001.

Deliver clusters, nodes, VMs, LXCs, storage, tasks, backups and snapshots.

Acceptance:

- external IDs remain stable
- unknown values are not zero

### PVE-003 — Implement Proxmox application and widgets

Depends on: PVE-002, DESK-011.

Deliver node/VM/storage views and summary widgets.

Acceptance:

- root/node and per-VM status are visible
- large inventories remain responsive

### PVE-004 — Implement Proxmox control actions

Depends on: PVE-002, API-004.

Deliver start, shutdown, stop and reboot with explicit permissions.

Acceptance:

- control is disabled by default
- destructive actions are audited

### PVE-005 — Integrate Proxmox console through Remote

Depends on: PVE-002, REM-004, PKG-009.

Acceptance:

- Proxmox package requests capability only
- no Remote implementation reference exists

## Phase 8 — Files and Caddy

### FILE-001 — Define file-provider contracts

Depends on: PKG-009.

Deliver provider-neutral path, metadata, list, read, write, copy, move, rename, delete and transfer contracts.

Acceptance:

- path traversal and provider-root escape are impossible
- cancellation and conflict behavior are defined

### FILE-002 — Implement File Manager shell

Depends on: FILE-001, PKG-010.

Deliver navigation, breadcrumbs, list virtualization, preview host and transfer queue.

Acceptance:

- large directories remain usable
- transfer continues while window is minimized

### FILE-003 — Implement Agent-local provider

Depends on: FILE-001, AGT-003.

Acceptance:

- configured roots are enforced
- symlink escape is tested

### FILE-004 — Implement SMB provider

Depends on: FILE-001, API-008.

Acceptance:

- credentials remain secret
- provider-specific errors remain actionable

### FILE-005 — Implement SFTP provider

Depends on: FILE-001, API-008.

Acceptance:

- host-key policy is explicit
- cancellation and partial transfer cleanup work

### FILE-006 — Implement WebDAV provider

Depends on: FILE-001, API-008.

Acceptance:

- capability differences are represented explicitly
- unsupported atomic operations do not pretend success

### FILE-007 — Integrate Remote and Browser transfers

Depends on: FILE-002, REM-004, BRW-003.

Acceptance:

- feature disables explicitly when Files is unavailable
- permission checks cover both session and destination

### CAD-001 — Add Caddy UI integration API

Repository: `Juloc/caddy-ui`.

Deliver versioned authenticated summary, routes, certificates and problems endpoints.

Acceptance:

- no JulOS database dependency
- stable identities and timestamps

### CAD-002 — Implement JulOS Caddy package

Depends on: CAD-001, PKG-010.

Deliver status app, widgets, problems and Browser/deep-link launch.

Acceptance:

- works without Docker package
- never reads Caddy UI database

## Phase 9 — Discovery and operational hardening

### DISC-001 — Implement discovery observation contracts

Depends on: AGT-003, CORE-003.

Deliver device/service observations, evidence and lifecycle.

Acceptance:

- observation is not approval
- stable device identity can merge multiple protocols

### DISC-002 — Implement ARP and ICMP discovery

Depends on: DISC-001.

Acceptance:

- scan ranges are allowlisted
- rate limits prevent network flooding

### DISC-003 — Implement mDNS and SSDP discovery

Depends on: DISC-001.

Acceptance:

- duplicate observations merge
- untrusted text is safely rendered

### DISC-004 — Add optional SNMP discovery

Depends on: DISC-001, API-008.

Acceptance:

- disabled by default
- credentials and network scope are explicit

### DISC-005 — Implement discovery approval UI

Depends on: DISC-001, DESK-007.

Acceptance:

- approve, manage and ignore are distinct
- ignored devices do not repeatedly appear as new

### OPS-001 — Implement safe mode

Depends on: PKG-007.

Acceptance:

- core starts with optional packages disabled
- Package Manager and backup remain usable

### OPS-002 — Implement backup operation

Depends on: API-007, API-008, PKG-004.

Acceptance:

- backup records core and package versions
- archive verification occurs

### OPS-003 — Implement restore workflow

Depends on: OPS-001, OPS-002.

Acceptance:

- documented clean restore test succeeds
- packages re-enable one at a time

### OPS-004 — Implement retention and cleanup

Depends on: API-001, REM-004, BRW-002.

Acceptance:

- active resources are never deleted
- cleanup failures create problems

### OPS-005 — Complete security hardening

Depends on: all security-relevant features.

Deliver CSP, rate limits, anti-forgery, scans, key rotation and review findings.

Acceptance:

- security test suite passes
- no high-severity unresolved issue remains for release scope

### OPS-006 — Complete performance pass

Depends on: major 1.0 features.

Acceptance:

- documented budgets measured
- regressions resolved or accepted through decision

### OPS-007 — Complete accessibility pass

Depends on: major user-facing features.

Acceptance:

- keyboard, zoom, focus, contrast, touch and screen-reader checklist passes

## Phase 10 — Release and migration

### REL-001 — Create installation wizard and setup guide

Depends on: core, package manager and deployment stability.

Acceptance:

- new user can install without repository knowledge
- required secrets and networks are explained

### REL-002 — Create operational runbooks

Depends on: OPS features.

Deliver every runbook listed in `SECURITY_AND_OPERATIONS.md`.

Acceptance:

- a second developer can follow each runbook without hidden knowledge

### REL-003 — Validate fresh installation

Acceptance:

- empty host reaches working desktop
- Agent, Docker, Proxmox, Browser and Remote setup succeeds

### REL-004 — Validate supported upgrade

Acceptance:

- previous release fixture upgrades
- package compatibility diagnostics are correct

### REL-005 — Complete Julgate parity and migration

Depends on: REM items and `JULGATE_MIGRATION.md`.

Acceptance:

- parity matrix accepted
- migration and rollback window documented

### REL-006 — Create signed release pipeline

Acceptance:

- versioned images and package artifacts
- signatures, digests and software bill of materials
- release notes and migration notes

### REL-007 — Perform backup and restore release test

Depends on: OPS-002, OPS-003.

Acceptance:

- restore from release candidate backup succeeds on clean deployment

### REL-008 — Publish JulOS 1.0

Acceptance:

- every `PRODUCT.md` success criterion passes
- no critical or high release blocker remains
- documentation matches released behavior
- GitHub release and version tags are created

## Issue creation order

Create issues phase by phase. Do not create hundreds of unowned issues at once. At the start of each phase:

1. confirm previous phase acceptance
2. update this breakdown from implementation evidence
3. create only the next independently actionable issues
4. assign dependencies and labels
5. keep `BACKLOG.md` focused on current status

## Recommended labels

```text
area:foundation
area:core
area:desktop
area:packages
area:agent
area:remote
area:browser
area:docker
area:proxmox
area:files
area:caddy
area:discovery
area:operations
type:feature
type:bug
type:architecture
type:security
type:documentation
priority:critical
priority:high
priority:normal
good-first-issue
blocked
```
