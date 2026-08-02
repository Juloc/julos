# Architecture

## 1. System overview

```text
Remote or local client browser
  └─ JulOS Desktop
       ├─ Window manager
       ├─ Taskbar, launcher and command palette
       ├─ Widget host
       ├─ Notification and problem center
       └─ Native package application modules
            │ HTTPS and SignalR
JulOS Server
  ├─ Authentication and authorization
  ├─ Core application services
  ├─ Package registry and lifecycle coordinator
  ├─ Capability broker
  ├─ App, window and layout state
  ├─ Agent connection service
  ├─ Events, operations, notifications and problems
  ├─ Audit log
  └─ Secret reference service
       │
       ├─ PostgreSQL
       ├─ Runtime Manager
       │    └─ JulOS-owned package and session runtime containers
       ├─ out-of-process package workers
       └─ outbound-connected JulOS Agents
            └─ hosts, Docker engines, filesystems and networks
```

Browser, RDP, VNC and SSH session content is transported through JulOS Remote. Internal websites are not generally embedded through iframes.

## 2. Architectural layers

Dependencies point inward:

```text
Package implementations and infrastructure adapters
                         ↓
             Package SDK and Contracts
                         ↓
               Application services
                         ↓
                    Domain
```

Composition roots point to implementations, but Domain and Application do not depend on outer layers.

## 3. Domain responsibilities

Domain owns platform rules only:

- package installation lifecycle
- capability identity and provider-selection inputs
- application and launch-target identity
- desktop layout, window and widget state
- session-reference lifecycle
- Agent identity and state
- permission and scope values
- problem, notification and audit metadata

Domain does not reference:

- ASP.NET Core
- Entity Framework Core
- Docker, Proxmox or Caddy
- Guacamole, RDP, VNC or SSH
- SMB, SFTP or WebDAV
- package implementation assemblies
- filesystem, process or network APIs

## 4. Application services

Application services implement core use cases through ports:

- install, configure, enable, disable, update and remove packages
- approve applications and discovery proposals
- create and update layouts
- enroll and revoke Agents
- authorize and route capability requests
- process problem observations
- create operations and audit events
- create and manage session references
- lease secrets for one authorized operation

Application services do not perform product-specific protocol work.

## 5. JulOS Server

Server is the ASP.NET Core composition root and control plane.

It owns:

- web authentication and sessions
- endpoint mapping
- authorization policy registration
- persistence wiring
- package lifecycle coordination
- event and operation transport
- Agent control connection
- Runtime Manager client
- package worker control clients

Server remains functional when every optional package is disabled or faulted.

Core persistence is implemented in Infrastructure. `CoreDbContext` owns the `core` schema and maps persistence-specific rows to Domain concepts; Domain contains no EF Core reference, persistence annotation or materialization-only constructor. Server only composes the context and invokes the explicit migration process. Package schemas are outside the context and cannot be reached through its model.

Local account records, password hashes, roles, setup completion and profile preferences are Infrastructure-owned persistence. The Application profile port exposes only the authenticated user's supported preferences and optimistic-concurrency revision; Infrastructure validates and persists them through `CoreDbContext`. Server composes ASP.NET Core Identity, cookie sessions, rate limiting and the shared antiforgery validator. A fallback policy denies anonymous access by default; endpoint owners must explicitly mark the setup, login, status or health surfaces anonymous.

Authorization keeps three owners separate. Domain owns pure permission and scope evaluation. Application owns the stable Core permission catalog and role-administration ports. Infrastructure resolves direct user assignments and inherited role assignments from PostgreSQL, while Server maps named policies and administrator endpoints. No role name bypasses permission evaluation; even the system administrator role is authorized through explicit global assignments.

## 6. JulOS Desktop

Desktop is a lightweight browser client shell. It owns immediate presentation state but not authorization or authoritative infrastructure state.

Responsibilities:

- window placement, size, focus, z-order and snapping
- taskbar and launcher behavior
- widget placement
- viewport-specific layout restoration
- package Custom Element hosting
- API and SignalR clients
- offline, stale and reconnect presentation
- localization and theme application

Window movement and resize occur locally at animation-frame speed. Persisted layout is synchronized after interaction; Server is not called for every pointer event.

## 7. Package architecture

A package consists of one signed artifact descriptor and one or more optional components:

```text
Package
├─ manifest and signatures
├─ frontend ES modules and localization assets
├─ package worker image or executable
├─ package-owned migrations
├─ runtime image references
├─ application and widget registrations
└─ capability declarations
```

### 7.1 Package worker

Package backend logic runs out of process. Worker responsibilities are limited to its domain and declared capabilities.

A package worker:

- exposes liveness and readiness
- authenticates to Server
- registers applications, widgets and capabilities
- validates configuration
- handles typed package requests
- uses only its package storage and authorized external connections
- has configured CPU and memory limits

A worker crash changes the package to a visible fault state and does not terminate Server.

### 7.2 Package frontend

Package frontend code is a signed ES module loaded as a Custom Element with Shadow DOM.

The Desktop supplies a limited host context for:

- typed API access
- localization
- theme tokens
- navigation
- command registration
- current window identity
- permission summary

The package does not receive raw authentication tokens, secret values or direct access to another package's state.

### 7.3 Package storage

Packages may use:

- core package-settings storage for small versioned settings
- a package-owned PostgreSQL schema with a restricted role
- declared runtime volumes for profiles or temporary transfer data

Cross-package database reads are forbidden.

## 8. Runtime Manager

Runtime Manager is a small privileged sidecar that owns direct access to the local container runtime.

It manages only JulOS-owned resources:

- package workers when containerized
- Browser runtimes
- Remote runtimes
- package-declared helper runtimes

Runtime Manager validates:

- approved image digest
- mandatory ownership labels
- allowed networks
- allowed mounts and volumes
- allowed environment keys
- CPU, memory and process limits
- runtime ownership for inspect, stop and removal

It does not expose the Docker API, arbitrary command execution or unrelated container control.

Runtime Manager is part of the JulOS control plane and is independent from the optional Docker package that manages user Docker environments.

## 9. Capability broker

Packages collaborate through versioned capabilities instead of direct references.

Example:

```text
Proxmox package requests remote.console/1
Server validates user permission and target scope
Capability broker selects a healthy Remote provider
Remote creates a protocol-specific session
Proxmox receives a protocol-neutral session reference
```

A capability request includes:

- caller package
- user and permission context
- target type and stable identity
- capability name and version
- typed operation payload
- correlation ID and deadline

Mutating requests create audit events.

## 10. Agent architecture

One Agent binary runs on hosts where local access is required.

Initial capability families:

- `system.metrics`
- `system.storage`
- `system.network`
- `docker.read`
- `docker.control`
- `files.local`
- `network.discovery`

The Agent:

- establishes an outbound authenticated connection
- advertises only enabled capabilities
- accepts typed allowlisted commands
- enforces target roots and scopes locally
- supports cancellation, deadlines and result limits
- has no general remote shell

Package-specific interpretation occurs in package workers. The Agent contains transport and host capability implementations, not package UI or business logic.

## 11. Browser and Remote architecture

### 11.1 Remote

Remote owns protocol-neutral session orchestration and protocol adapters.

Core sees only:

- session request
- session reference
- state
- lifecycle policy
- failure code

RDP, VNC, SSH, guacd and display transport types remain inside Remote components.

### 11.2 Browser

Browser owns isolated Chromium runtime policy:

- profile type
- target URL
- network profile
- resource limits
- download, upload and clipboard policy
- inactivity and maximum duration

Browser asks Runtime Manager to start the runtime and requests a Remote session for display and input.

### 11.3 Window/session separation

A Window stores presentation state. A Session stores runtime state.

Closing a Window may disconnect, suspend or terminate according to the application policy. Reloading Desktop can restore a Window and then reconnect, show an expired state or request a new Session.

## 12. Integration products

Large products expose stable integration APIs. JulOS packages consume those APIs for summaries, problems and launch actions.

Caddy UI integration:

```text
Caddy UI authoritative state
   ↓ authenticated integration API
JulOS.Caddy worker
   ↓ package API and registrations
JulOS Desktop widgets and application
```

JulOS.Caddy never reads Caddy UI database tables directly and does not require the Docker package.

## 13. Data ownership

### Core database

- users, roles and permission assignments
- package installations and lifecycle
- application definitions and approvals
- layouts, windows and widget placements
- Agent identities and connection state
- session references
- problem, notification, operation and audit metadata
- connection metadata and secret references

### Package storage

- package configuration
- external-resource mapping
- package-owned operational state
- discovery observations
- package-specific cached summaries where necessary

### External system

- infrastructure configuration
- VM, container, route, certificate and file domain data
- authoritative task and history data

### Runtime storage

- Browser profiles
- temporary downloads and uploads
- short-lived session artifacts

Temporary runtime data is not confused with persistent package state.

## 14. Communication paths

### Client to Server

- HTTPS REST for authoritative state and mutations
- SignalR for change notifications
- short-lived session-specific transport tokens for Remote

### Server to package worker

- authenticated versioned control and capability contracts over private network

### Server to Runtime Manager

- authenticated narrow allowlisted runtime API

### Agent to Server

- outbound long-lived mutually authenticated connection

### Package worker to external product

- package-owned adapter using an authorized secret lease and explicit timeout

No component receives a generic proxy to another trust boundary.

## 15. Failure behavior

- unavailable external systems show explicit offline or unavailable states
- last-known values include observation time and stale status
- reconnect does not require full-page reload
- failures never become empty success responses
- package failures are isolated and observable
- Agent disconnect cancels or fails affected operations predictably
- Runtime Manager failure prevents new runtimes but does not stop core read access
- startup safe mode disables optional packages
- cleanup failure creates a problem rather than silently leaking resources

## 16. Security boundaries

- TLS for all remote communication
- mutual authentication for Agent and privileged control channels
- encrypted secret storage and opaque references
- short-lived session and credential leases
- backend-enforced permissions and scopes
- explicit confirmation for destructive actions
- audit entries for mutations
- package and runtime resource limits
- signed package and runtime artifacts
- no public Docker sockets, PostgreSQL ports or internal package control endpoints

Detailed requirements are in `SECURITY_AND_OPERATIONS.md`.

## 17. Deployment model

The first supported control plane is Docker Compose.

Required:

- JulOS Server
- PostgreSQL
- Runtime Manager

Optional:

- package workers
- Remote runtime or guacd
- Browser runtime pool
- Agents on Proxmox nodes, Docker hosts and selected VMs

The architecture must not require all packages or runtimes to be installed.

## 18. Evolution rules

- public contracts are versioned
- package implementation details do not enter Core
- compatibility layers have documented removal versions
- new abstraction requires a real repeated use case or explicit architectural boundary
- external product integrations use documented public APIs
- every boundary change updates architecture tests and decisions
