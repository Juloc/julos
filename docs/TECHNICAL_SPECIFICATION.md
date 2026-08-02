# Technical specification

This document turns the architecture into concrete implementation rules. Changing a mandatory rule requires an accepted entry in `DECISIONS.md` and updates to every affected specification.

## 1. Technology baseline

### Server and workers

- .NET 10 with the exact SDK pinned in `global.json`
- ASP.NET Core for HTTP APIs, authentication, SignalR and hosted services
- PostgreSQL for core and package-owned persistent state
- Entity Framework Core for migrations and core persistence
- structured JSON logging through the built-in logging abstractions
- OpenTelemetry-compatible trace and metric boundaries without requiring an external collector for basic operation

### Desktop client

- TypeScript compiled as native ES modules
- browser-native Custom Elements and Shadow DOM for package UI boundaries
- CSS design tokens implementing the Fluent 2 visual language
- no general SPA framework in the initial foundation
- SignalR for server-originated desktop events and status changes
- IndexedDB only for disposable client cache, never as the authoritative store

A frontend framework may be introduced only through an accepted decision showing a concrete maintenance or capability requirement that native modules cannot satisfy cleanly.

### Testing

- Microsoft.Testing.Platform and MSTest for .NET tests
- built-in Node test runner for pure TypeScript logic where sufficient
- Microsoft Playwright for end-to-end browser testing
- custom architecture tests using project and assembly metadata instead of a broad architecture-testing dependency

## 2. Repository layout

```text
src/
  JulOS.Domain/
  JulOS.Application/
  JulOS.Contracts/
  JulOS.Infrastructure/
  JulOS.Server/
  JulOS.Desktop/
  JulOS.PackageSdk/
  JulOS.Agent/
  JulOS.RuntimeManager/
packages/
  Browser/
  Remote/
  Docker/
  Proxmox/
  Files/
  Caddy/
  Discovery/
runtimes/
  Browser/
  Remote/
tests/
  JulOS.Domain.Tests/
  JulOS.Application.Tests/
  JulOS.Architecture.Tests/
  JulOS.Infrastructure.Tests/
  JulOS.Integration.Tests/
  JulOS.Desktop.Tests/
  JulOS.EndToEnd.Tests/
deploy/
  compose/
  examples/
docs/
tools/
```

Directories are created only when their first real implementation is added. Empty placeholder projects are not retained unless the current milestone explicitly validates the intended dependency boundary.

### 2.1 Repository build configuration

| File | Purpose |
|---|---|
| `VERSION` | the single version source for every build output |
| `global.json` | pins the .NET SDK and selects the Microsoft.Testing.Platform mode of `dotnet test` |
| `Directory.Build.props` | target framework, nullable reference types, warnings as errors, analyzers and shared metadata |
| `Directory.Packages.props` | central package management; every NuGet version is declared exactly once |
| `JulOS.slnx` | the solution; every committed project is listed exactly once |

Projects declare package references without a version. Warnings are errors repository-wide; a suppression must be local, narrow and justified in place.

### 2.2 Desktop build

`src/JulOS.Desktop` is a Node workspace with `typescript` as its only build dependency. There is no bundler, so the compiler emits the modules the browser loads directly and every relative import carries its `.js` extension.

| Command | Result |
|---|---|
| `npm run typecheck` | type checks sources and tests without emitting |
| `npm run build` | emits `dist/scripts` and copies `static` to `dist` |
| `npm run watch` | rebuilds on change during development |
| `npm test` | compiles to `build/tests` and runs the Node test runner |

`dist` and `build` are generated and are not committed.

### 2.3 Versioning

`VERSION` at the repository root holds one semantic version. `Directory.Build.props` reads it into `VersionPrefix`, so every assembly carries it without restating it anywhere.

The running version is reachable through:

- the assembly informational version
- the startup log entry with event identifier 1000
- `GET /api/v1/system/version`
- the `org.opencontainers.image.version` label of the container image

A Dockerfile cannot read a file into a label, so `JULOS_VERSION` is passed as a build argument. `tools/validate.mjs` and continuous integration pass the value from `VERSION`; a plain `docker compose up --build` labels the image `0.0.0-development`. The application version inside the image always comes from `VERSION`, because the file is part of the build context.

The `version` validation stage reads the built project version back through MSBuild, so the file cannot silently stop being the source.

## 3. Project responsibilities

### JulOS.Domain

Contains platform entities, value objects, domain rules and domain events. It references only the .NET base class libraries.

Forbidden:

- EF Core attributes or contexts
- ASP.NET types
- Docker, Proxmox, Caddy or protocol names
- package implementation types
- filesystem or network access

### JulOS.Application

Contains platform use cases, ports and authorization-independent orchestration. It references Domain and stable Contracts only where transport-neutral shared types are necessary.

### JulOS.Contracts

Contains versioned public contracts used across process boundaries:

- HTTP request and response models
- Agent contracts
- package manifest and lifecycle contracts
- capability request and response contracts
- session references and status contracts
- event envelope contracts

Contracts contain no persistence annotations or implementation behavior.

### JulOS.Infrastructure

Contains core persistence, the ASP.NET Core Identity stores for local users and roles, secret storage, external identity integration and infrastructure adapters required by the control plane. Product-specific adapters remain in packages.

### JulOS.Server

ASP.NET Core composition root. It owns middleware, endpoint mapping, authentication setup, secure cookie configuration, default-deny fallback authorization, rate limiting, antiforgery, background-service registration and dependency injection wiring.

### JulOS.Desktop

Contains static TypeScript, HTML, localization resources, design tokens and desktop client modules. It consumes only documented APIs and events.

### JulOS.PackageSdk

Contains the smallest stable interfaces and helpers required by package authors. It must not expose internal server services.

### JulOS.Agent

A single deployable agent binary. It provides only enabled allowlisted capabilities and establishes an outbound connection to Server.

### JulOS.RuntimeManager

A narrow local control-plane sidecar that creates, updates and removes JulOS-owned runtime containers. It is the only control-plane component allowed direct access to the local container runtime.

Runtime Manager restrictions:

- accepts authenticated requests only from JulOS Server
- manages only resources with mandatory JulOS ownership labels
- uses an allowlisted set of mounts, networks, environment keys and resource limits
- cannot inspect or control unrelated user containers
- has no general command-execution endpoint

## 4. Core process model

### 4.1 JulOS Server

One stateless server process except for database, package artifact storage, secret key material and runtime references.

Responsibilities:

- authentication and authorization
- API and event transport
- core application services
- package lifecycle coordination
- capability brokerage
- Agent connections
- problem and notification processing
- audit logging

### 4.2 Package workers

Backend package logic runs out of process. Each enabled package has a worker process or container with:

- package identity and version
- health endpoint
- authenticated control channel
- registered capability providers
- declared storage access
- resource limits

A worker crash changes the installation to `Faulted`; it does not stop Server.

### 4.3 Runtime containers

Browser and Remote sessions run in short-lived or pooled containers managed through Runtime Manager. Runtime containers are distinct from package workers.

## 5. Package frontend model

Official package frontends are signed ES modules described by the package manifest.

Each application entry declares:

- module URL or package asset path
- integrity hash
- custom element name
- required API contract version
- instance policy
- default window constraints
- supported viewport classes

Package application UI runs as a Custom Element with Shadow DOM. It receives a limited host context containing:

- current application and window identity
- localization service
- theme tokens
- navigation and command registration
- typed API client factory
- event subscription API
- permission summary

It does not receive raw authentication tokens, secret values, global state mutation access or unrestricted access to another package's DOM.

JulOS 1.0 installs only trusted official packages signed by the configured JulOS signing authority. Untrusted third-party package sandboxing is outside 1.0.

## 6. Package backend communication

Server and package workers communicate through versioned HTTP or gRPC contracts over a private control network.

Required worker endpoints:

```text
GET  /health/live
GET  /health/ready
POST /control/start
POST /control/stop
POST /control/configure
POST /control/validate
GET  /control/registrations
```

The exact transport is selected during M3. The semantic contract is fixed:

- calls are authenticated
- calls have deadlines
- retries occur only for documented idempotent operations
- failure returns a typed problem, never an empty success
- package workers cannot call internal server endpoints outside their declared contract

## 7. Agent transport

Agents establish an outbound long-lived connection to Server.

Enrollment flow:

1. Administrator creates one-time enrollment token.
2. Agent sends token, public key and basic identity.
3. Server validates and consumes the token.
4. Server creates durable Agent identity and issues client credentials.
5. Agent stores credentials with operating-system permissions.
6. Later connections use mutual authentication and short-lived session negotiation.

Agent messages use a versioned envelope:

```text
MessageId
ContractVersion
AgentId
SentAtUtc
CorrelationId
MessageType
Payload
```

Commands are typed capability requests. No arbitrary shell command payload exists.

## 8. Capability broker

A capability is a versioned operation family such as `remote.session/1`, `docker.inventory/1` or `files.read/1`.

Provider registration contains:

- capability name and version
- package and worker identity
- supported target kinds
- health state
- priority
- metadata schema version

Request flow:

1. caller requests capability with user, target and operation context
2. Server authenticates the caller
3. authorization policy validates permission and target scope
4. broker resolves a healthy compatible provider
5. provider receives an opaque request reference and typed payload
6. result is returned through the versioned contract
7. mutations create an audit event

Packages never obtain another package's service instance.

## 9. Window manager implementation

The desktop maintains an in-memory window store synchronized with persisted layout revisions.

Window operations are deterministic commands:

```text
OpenWindow
FocusWindow
MoveWindow
ResizeWindow
MinimizeWindow
RestoreWindow
MaximizeWindow
SnapWindow
CloseWindow
AttachSession
DetachSession
```

Each operation validates:

- application instance policy
- minimum and maximum size
- usable viewport bounds
- current layout revision
- associated session policy

Drag and resize update local presentation at animation-frame speed. Persistence is debounced and sent after interaction ends. Server remains authoritative for stored layout revision but is not involved in every pointer movement.

## 10. Browser runtime

A browser runtime contains:

- Chromium
- isolated Linux user
- virtual display or headless display surface compatible with Remote transport
- configured network profile
- profile volume where persistence is enabled
- download staging directory
- CPU, memory, process and inactivity limits

Modes:

- `Persistent`: named profile retained for one user
- `Temporary`: unique profile removed after termination
- `Application`: fixed start URL and optional restricted browser chrome

Browser package never returns a raw internal service URL to the external client when that URL should remain private. The client receives a session reference.

## 11. Remote runtime

Remote is protocol-neutral at the core boundary. Protocol adapters translate into the common session model.

Common session functions:

- create
- connect
- observe state
- resize display
- send pointer input
- send keyboard input
- clipboard read and write when allowed
- upload or drive redirection when allowed
- reconnect
- disconnect
- terminate

Julgate and guacd implementation details remain behind Remote package contracts.

## 12. Persistence and concurrency

- all timestamps stored as UTC
- all user-visible times localized by the client
- stable identifiers generated by Server
- mutable core records contain revision values
- updates use optimistic concurrency
- API conflicts return current revision and conflict details
- soft deletion is used only when audit or recovery requires it
- domain history is not duplicated from external systems

`CoreDbContext` lives in Infrastructure and owns only the PostgreSQL `core` schema. It maps persistence-specific rows to the Phase 1 domain concepts instead of adding EF Core constructors or annotations to Domain. Package-owned schemas never enter this context.

The migration history table is `core.__ef_migrations_history`. Schema changes run only through the explicit `JulOS.Server --migrate-database` command or the equivalent one-shot Compose service. Normal Server startup does not call `Migrate`, and manual schema edits are unsupported.

The first migration uses database keys, foreign keys, unique indexes and check constraints to enforce identities, valid revisions, fault metadata, scope shape, layout bounds and lifecycle timestamps. Audit events additionally have a PostgreSQL trigger that rejects updates and deletes.

## 13. Events and real-time updates

Server publishes versioned events through SignalR:

```text
package.changed
application.changed
window.invalidated
agent.status.changed
resource.observed
problem.changed
notification.created
session.changed
```

Events contain identifiers and revisions, not large authoritative objects. Clients fetch current state when necessary.

Delivery is at least once. Clients deduplicate by EventId and tolerate reconnect gaps by requesting a state refresh.

## 14. Error handling

All HTTP APIs use a common problem shape based on ASP.NET Core Problem Details plus:

```text
code
correlationId
retryable
fieldErrors
currentRevision
sourcePackage
```

Rules:

- never convert exceptions into success with empty data
- never expose secrets, stack traces or internal connection strings
- preserve actionable external-system error details after sanitization
- log once at the layer that owns handling responsibility
- UI shows a stable error code and correlation ID for diagnostics

## 15. Localization

- source code and contracts use English
- default UI language is English
- German is included from the first user-facing milestone
- package manifests declare localization bundles
- no user-facing string is hard-coded in TypeScript or backend response construction
- date, time, number and storage formatting use the selected locale

## 16. Performance rules

Initial budgets on a typical homelab control-plane VM:

- unauthenticated shell response: under 500 ms after process warm-up
- desktop interactive after cached load: under 2 seconds on a normal broadband connection
- window drag and resize: target 60 frames per second without server round trips
- idle desktop: no continuous high-frequency polling
- widget refresh: event-driven or package-defined interval, minimum 5 seconds unless a live session requires more
- core memory target without optional workers: below 300 MB
- desktop initial compressed assets target: below 1.5 MB excluding localization and package modules

Budget changes require measured evidence and documentation.

## 17. Prohibited designs

- iframe as the default application runtime
- direct package-to-package service references
- cross-package database access
- package logic in Core
- raw Docker socket access from Server
- arbitrary Agent command execution
- frontend-only authorization
- secrets in browser storage
- long-running request polling when an event stream exists
- duplicate old and new implementations kept indefinitely
- broad catch blocks that report success
- generic plugin abstractions without a real package requirement
