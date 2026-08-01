# Implementation plan

This plan is ordered. Do not start a later milestone while an earlier milestone has unresolved architecture or acceptance failures.

Each numbered work item should normally become one GitHub issue. Split an item only when the resulting issues can be completed and validated independently.

## M0 — Repository foundation

### M0.1 Documentation baseline

- Add product, architecture, package, decision and implementation documentation.
- Add contributor and AI-agent rules.
- Add encoding and line-ending policy.
- Add pull-request template.

Acceptance:

- all foundation documents agree on scope and terminology
- README links every authoritative document
- backlog identifies the next implementation issue

### M0.2 Solution skeleton

- Create the solution and directories for Core, Server, Desktop, Contracts, Package SDK, Agent and tests.
- Pin the supported .NET SDK in the repository.
- Add build-wide analyzers and nullable reference types.
- Add minimal frontend toolchain only after documenting the selected approach.

Acceptance:

- clean checkout builds with one documented command
- no package-specific dependency exists in Core
- architecture tests can reference the project structure

### M0.3 Local development stack

- Add Docker Compose for JulOS Server and PostgreSQL.
- Add development configuration examples without real secrets.
- Add health and readiness endpoints.
- Add deterministic database migration startup.

Acceptance:

- `docker compose up` reaches healthy state
- missing required configuration fails with an actionable message
- restarts do not create duplicate migrations or seed data

### M0.4 Continuous integration

- Build backend and frontend.
- Run unit, architecture and integration tests.
- Validate formatting, Markdown links and package manifests.
- Build container images without publishing them on pull requests.

Acceptance:

- the same validation is runnable locally
- failure output identifies the failing project or rule

## M1 — Core and authentication

### M1.1 Core domain model

Implement only core-owned entities:

- user and role references
- package installation
- capability registration
- application definition
- window and layout state
- agent identity
- problem and notification
- audit entry

Acceptance:

- no infrastructure product appears in core entity names
- domain invariants have unit tests

### M1.2 Local authentication

- Implement local login and secure session handling.
- Add roles and permission claims.
- Prepare an OIDC provider contract without implementing provider-specific code.

Acceptance:

- unauthenticated access cannot reach desktop or APIs
- authorization is validated by backend tests

### M1.3 Secrets service

- Store encrypted secret values separately from normal configuration.
- Return opaque secret references to packages.
- Prevent values from appearing in API responses and logs.

Acceptance:

- secret round-trip and access tests pass
- log tests show no plaintext value

## M2 — Desktop shell

### M2.1 Desktop layout

- Create desktop surface, taskbar, launcher, notification area and settings entry.
- Use Fluent 2 design tokens consistently.
- Support system, light and dark themes.

Acceptance:

- desktop works at desktop, tablet and mobile viewport sizes
- no global CSS override is required for package applications

### M2.2 Window manager

Window state:

```text
WindowId, AppId, Title, Position, Size, State, ViewportClass,
DesktopId, ZIndex, SessionId
```

Implement:

- open and focus
- move and resize
- minimize, maximize and close
- left, right and quarter snapping
- configurable single-instance or multi-instance applications

Acceptance:

- five simultaneous windows remain usable
- snapping never places a window outside the usable desktop
- keyboard and touch interactions have tests where practical

### M2.3 Layout persistence

- Persist layout per user and viewport class.
- Restore after reload and reconnect.
- Separate window state from runtime session state.

Acceptance:

- desktop and mobile layouts do not overwrite each other
- missing applications are skipped with a visible explanation

## M3 — Package runtime

### M3.1 Versioned manifest

- Define JSON schema and validation errors.
- Add package identity, versions, compatibility, permissions, applications, widgets and capabilities.

Acceptance:

- valid and invalid manifest fixtures are tested
- incompatible packages cannot be enabled

### M3.2 Package lifecycle

- Install, configure, enable, disable and remove.
- Add package health, logs and fault state.
- Add package-owned database migrations.

Acceptance:

- a deliberately failing package does not stop the core
- disabling a package removes its apps and widgets without deleting configuration

### M3.3 Capability broker

- Register providers by versioned capability.
- Resolve requests using enabled packages and target support.
- Validate user permissions and audit mutations.

Acceptance:

- packages do not reference each other directly
- denied capability requests return explicit authorization errors

## M4 — Agent and widgets

### M4.1 Agent enrollment

- Create one-time enrollment token.
- Generate durable agent identity.
- Use outbound authenticated connection and heartbeat.

Acceptance:

- revoked agents cannot reconnect
- offline agents are detected without manual refresh

### M4.2 Host metrics

- CPU, load, memory, uptime, storage and network counters.
- Label values with observation time.

Acceptance:

- unavailable metrics do not become zero values
- Linux collection is tested against fixtures or integration hosts

### M4.3 Widget framework

- Register, place, resize and remove widgets.
- Stream updates without page reload.
- Support loading, stale, offline and error states.

Acceptance:

- package widgets cannot modify another widget's state
- widget clicks can open a registered application or deep link

## M5 — Browser and Remote

### M5.1 Remote contracts

- Define session creation, connection state, display, input, clipboard and termination contracts.
- Define reconnect and inactivity policies.

Acceptance:

- contracts are protocol-neutral
- session and window lifecycle remain independent

### M5.2 Julgate extraction

- Inventory reusable Julgate session and transport code.
- Extract backend and client components without copying old product UI.
- Add parity checklist for RDP, VNC and SSH.

Acceptance:

- every extracted component has tests or a documented integration test
- Julgate remains deployable during migration

### M5.3 Browser runtime

- Build isolated Chromium runtime.
- Add persistent, temporary and fixed-app profiles.
- Connect display and input through Remote.
- Add session limits, cleanup and internal network access.

Acceptance:

- local IP and DNS targets work without public exposure
- temporary profile data is removed after termination
- multiple browser windows are isolated

## M6 — Docker and Proxmox

### M6.1 Docker connection and inventory

- Connect through an agent capability.
- Read hosts, Compose projects, services, containers, images, volumes and networks.
- Add controlled lifecycle actions behind permissions.

Acceptance:

- no Docker socket is exposed publicly
- read-only configuration cannot perform mutations

### M6.2 Docker app discovery

Discovery priority:

1. JulOS labels
2. Compose service metadata
3. Caddy route integration
4. published ports
5. container and image heuristics

Stable identity:

```text
agent-id + compose-project + service-name
```

Acceptance:

- restart or container recreation does not duplicate an application
- discovered applications require approval

### M6.3 Docker problems

Detect unhealthy, restart loop, stopped, unreachable, missing mount and resource-limit conditions.

Acceptance:

- repeated observations update one problem instead of creating duplicates

### M6.4 Proxmox inventory

- Connect through supported Proxmox API authentication.
- Read clusters, nodes, VMs, LXCs, storage, tasks and backups.
- Add explicitly enabled control actions.

Acceptance:

- write actions are disabled by default
- console requests use Remote capability

## M7 — Files and Caddy

### M7.1 File contracts and manager

- Define provider-neutral paths and operations.
- Implement local agent, SMB, SFTP and WebDAV providers.
- Add upload, download, copy, move, rename, delete and preview.

Acceptance:

- path traversal is blocked
- destructive actions require explicit confirmation
- provider errors retain their useful cause

### M7.2 Caddy UI integration API

Implement in Caddy UI:

```text
GET /api/integration/summary
GET /api/integration/routes
GET /api/integration/certificates
GET /api/integration/problems
```

Acceptance:

- API is versioned and authenticated
- no JulOS database coupling is introduced

### M7.3 Caddy package

- Show health, route summary, certificate state and reload problems.
- Open Caddy UI for complete management.

Acceptance:

- package functions without Docker package
- package never reads Caddy UI database directly

## M8 — Discovery, problems and hardening

### M8.1 Network discovery

- Collect ARP, ICMP, mDNS, SSDP and optional SNMP observations through agents.
- Implement `Discovered → Confirmed → Managed → Ignored` lifecycle.

Acceptance:

- discovery never grants management automatically
- ignored devices do not repeatedly reappear as new

### M8.2 Problem center

Common problem fields:

```text
ProblemId, SourcePackage, ResourceId, Severity, Title, Description,
DetectedAt, LastSeenAt, SuggestedAction, DeepLink
```

Acceptance:

- problems deduplicate by source and resource identity
- stale and resolved states are explicit

### M8.3 Security and recovery

- rate limits, CSRF and content security policy
- audit coverage
- backup and restore
- package safe mode
- runtime resource limits
- dependency and container scanning

Acceptance:

- JulOS can start with optional packages disabled
- documented restore test succeeds

## M9 — 1.0 release

- complete end-to-end setup documentation
- test fresh installation and upgrade
- publish versioned images and release notes
- verify Julgate migration status
- create package template only if SDK is stable

Release acceptance is defined in `docs/PRODUCT.md`.
