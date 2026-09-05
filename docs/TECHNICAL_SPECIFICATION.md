# Technical specification

This document turns the architecture into concrete implementation rules. Changing a mandatory rule requires an accepted entry in `DECISIONS.md` and updates to every affected specification.

## 1. Technology baseline

### Server and workers

- .NET 10 with the exact SDK pinned in `global.json`
- ASP.NET Core for HTTP APIs, authentication, SignalR and hosted services
- SQLite by default for core and package-owned persistent state; PostgreSQL is opt-in for larger or multi-instance deployments (D033)
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
- installable PWA manifest and a service worker that caches only versioned immutable Shell assets

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
  JulOS.HostConnector/
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
- Host Connector contracts
- client-device, workspace and application-catalog contracts
- package manifest and lifecycle contracts
- capability request and response contracts
- session references and status contracts
- event envelope contracts

Contracts contain no persistence annotations or implementation behavior.

### JulOS.Infrastructure

Contains core persistence, the ASP.NET Core Identity stores for local users and roles, secret storage, external identity integration and infrastructure adapters required by the control plane. Product-specific adapters remain in packages.

Secret storage is a Core-backed Infrastructure adapter. AES-256-GCM ciphertext, nonce, tag and key identifier are stored in the core database; the 32-byte key files are loaded from the absolute external `Secrets:KeyRingPath`. The active key identifier and lease lifetime are non-secret configuration, while key contents are deployment material. The HTTP layer exposes metadata-only create, read, rotate and delete operations; decrypted bytes exist only inside the operation-scoped Application lease.

### JulOS.Server

ASP.NET Core composition root. It owns middleware, endpoint mapping, authentication setup, secure cookie configuration, default-deny fallback authorization, rate limiting, antiforgery, background-service registration and dependency injection wiring.

### JulOS.Desktop

Contains static TypeScript, HTML, localization resources, design tokens and desktop client modules. It consumes only documented APIs and events.

### JulOS.PackageSdk

Contains the smallest stable interfaces and helpers required by package authors. It must not expose internal server services.

### JulOS.HostConnector

A single optional deployable Host Connector binary. It provides only enabled versioned typed capabilities and establishes an outbound authenticated connection to Server. It contains no assistant/chat behavior, package UI, package business logic, generic shell or Docker API proxy.

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
- Host Connector connections
- problem and notification processing
- audit logging

### 4.2 Package workers

Backend package logic runs out of process. Each enabled package has a supervised child process with:

- package identity and version
- health protocol message
- authenticated control channel
- registered capability providers
- declared storage access
- resource limits

A worker crash changes the installation to `Faulted`; it does not stop Server.

### 4.3 Runtime containers

Browser and Remote sessions run in short-lived or pooled containers managed through Runtime Manager. Runtime containers are distinct from package workers.

## 5. Package frontend model

Trusted package frontends may be integrity-checked ES modules described by the package manifest. Unsigned or unknown-publisher native frontends run only after `PKG-013` supplies the isolated package-origin/message-bridge boundary; Shadow DOM is not a hostile-code sandbox.

Each application entry declares:

- module URL or package asset path
- integrity hash
- custom element name
- required API contract version
- instance policy
- default window constraints
- supported viewport classes
- surface contract version and supported background modes

Package application UI runs as a Custom Element with Shadow DOM. It receives a limited host context containing:

- current application and window identity
- localization service
- theme tokens
- navigation and command registration
- typed API client factory
- event subscription API
- permission summary

It does not receive raw authentication tokens, secret values, global state mutation access or unrestricted access to another package's DOM.

Unsigned and unknown-publisher extension artifacts may be selected after a clear warning. Their integrity digest is still mandatory. Native code that lacks a trusted publisher cannot execute in the Shell origin; it requires the isolated frontend contract. An artifact that claims a signature but fails verification is rejected as corrupted.

### 5.1 Frontend surface lifecycle

The package host owns one versioned Surface handle per Window. The contract supports activate, deactivate, suspend, resume, optional Back handling and dispose. Surface execution is independent from Window and runtime Session lifecycle. Unsupported major versions fail activation; a mobile-capable package is not silently kept alive because it lacks suspend support. `MOBILE_PWA.md` defines exact semantics.

### 5.2 Catalog applications

`app-catalog-index.v1` and `app-manifest.v1` are separate from the extension-package manifest. Catalog entries can connect an existing service, normalize a Docker image into Compose, apply standard Compose or reference a native extension. The Docker package and Host Connector execute user workloads; Runtime Manager remains limited to JulOS control-plane runtimes. `APPLICATION_CATALOG.md` defines schemas, sources, trust, APIs and lifecycle.

## 6. Package backend communication

Server supervises each package worker as a child process and exchanges a bounded newline-delimited JSON protocol over the worker's standard input and output. This is not a private HTTP or gRPC endpoint and is not network-isolated (D034).

Required worker protocol messages:

```text
validate
configure
register
start
stop
health
command
```

The transport is the newline-delimited JSON protocol over the worker's standard input and output. The semantic contract is fixed:

- calls are authenticated
- calls have deadlines
- retries occur only for documented idempotent operations
- failure returns a typed problem, never an empty success
- package workers cannot call internal server endpoints outside their declared contract

## 7. Host Connector transport

Host Connector protocol v1 uses outbound HTTPS requests only: heartbeat, bounded long-poll for typed requests and result submission. HCON-005 adds a separately authenticated target-bound streaming connection; it does not replace or duplicate the control protocol.

Enrollment flow:

1. Administrator creates one-time enrollment token.
2. Host Connector generates a 48-byte random Base64url credential and sends token, credential and basic identity over HTTPS.
3. Server validates and consumes the token.
4. Server creates durable Host Connector identity, stores only the credential hash and returns identity/poll policy without echoing the credential.
5. Host Connector stores its original credential with operating-system permissions.
6. Later requests use server-authenticated TLS plus Host Connector ID and bearer credential; exact enrollment/rotation retries are idempotent.

Host Connector messages use a versioned envelope:

```text
MessageId
ContractVersion
HostConnectorId
SentAtUtc
CorrelationId
MessageType
Payload
```

For a control request, `MessageId` is the persisted Host Connector Request ID and the result-route ID. Requests are typed capability operations selected by capability, version, operation, payload-schema version and result-schema version. The registry also fixes `replay-safe` or `reconcile-required`; an unknown-outcome mutation becomes in-time `failed` from journal recovery or deadline `expired`, always with `host_connector.outcome_unknown` and a new read-only reconciliation request rather than replay. Result submission must match the persisted Result Schema Version and has the exact succeeded/failed/cancelled union, bounded canonical digest and idempotent terminal-retry behavior in `HOST_CONNECTOR.md`; successful result JSON is persisted rather than reduced to an unverified success flag.

Credential rotation is two-phase and crash recoverable: Connector persists old active plus pending locally, Server persists only current/pending hashes and an overlap deadline, and the pending-authenticated acknowledgement promotes the hash transactionally. No crash point can leave Connector with only a credential Server never accepted.

No arbitrary shell, command line, TCP target or Docker request payload exists. Bounded streams require a typed parent operation and target-bound grant. The complete wire and migration contract is `HOST_CONNECTOR.md`.

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


## 8.1 Durable operation execution

`IOperationService` is the Core-owned boundary for long-running work. A caller creates a queued operation with a user-scoped idempotency key. The owning executor explicitly marks it running, appends progress, and reaches exactly one terminal state. Current status and every accepted progress event are committed in the core database, so reconnects and Server restarts do not invent completion or lose cancellation intent.

A cancellation request for queued work cancels it immediately. A running request sets a durable cancellation timestamp; the worker or Host Connector must observe that flag and later acknowledge cancellation through the same lifecycle port. Failure completion accepts only a stable code and sanitized safe detail. Raw exceptions remain inside the executor boundary.

## 9. Window manager implementation

The desktop maintains an in-memory window store synchronized with persisted layout revisions.

Four cooperating owners are required and cannot be replaced by a second window store:

- `WorkspaceController` resolves Phone, Tablet, desktop-single or desktop-multi plus shared/device/fresh scope;
- `PresentationController` maps durable Windows into freeform, tiled, Phone Single or Phone Split slots;
- `SurfaceLifecycleScheduler` applies the exact foreground-focused, foreground-visible, background-active, suspended, faulted and terminated state machine;
- `ShellNavigationController` routes overlay, application, split/task and Root Back behavior.

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

Phone presentation additionally validates at most two foreground slots. Tablet uses the same Window commands with touch-oriented presentation defaults. A workspace switch flushes the old writable layout before loading and rendering the new one.

Drag and resize update local presentation at animation-frame speed. Persistence is debounced and sent after interaction ends. Server remains authoritative for stored layout revision but is not involved in every pointer movement.

## 10. Browser proxy

The JulOS Browser is a Core transparent-proxy surface. HTTP/HTTPS and WebSockets route through
JulOS, while the user's own browser performs HTML/JavaScript/media rendering.

The proxy owns framing/CSP compatibility, redirect/cookie normalization, DNS pinning and SSRF policy.
Browser tabs and resumable workspace metadata are server-owned. No isolated Chromium Browser runtime
or Browser package exists.

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

`CoreDbContext` lives in Infrastructure and owns only the `core` schema. It maps persistence-specific rows to the Phase 1 domain concepts instead of adding EF Core constructors or annotations to Domain. Package-owned schemas never enter this context.

Schema initialization and upgrade run only through the explicit `JulOS.Server --migrate-database` command or the equivalent one-shot Compose service; normal Server startup never changes the schema and manual schema edits are unsupported. PostgreSQL and SQLite both apply committed, ordered migrations after `DB-001`. `EnsureCreated` is permitted only for isolated test stores that are never upgraded. Existing beta SQLite databases receive a deterministic fixture-tested migration path before Host Connector or workspace schema changes land.

PostgreSQL and SQLite migrations enforce equivalent identities, valid revisions, scope shape, layout mode/nullability and lifecycle rules with provider-appropriate keys, partial indexes, foreign keys and check constraints. PostgreSQL additionally uses its append-only audit trigger; SQLite uses a provider-specific equivalent trigger after DB-001. Domain/Application rules remain the first validation layer, but a supported provider may not weaken a documented persistence invariant merely because its DDL syntax differs.

## 13. Events and real-time updates

Server publishes versioned events through SignalR:

```text
package.changed
application.changed
window.invalidated
host_connector.status.changed
client_device.changed
workspace_layout.changed
operation.changed
app_installation.changed
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
- profile preferences accept only `en` or `de`, a resolvable IANA time zone, `system`/`light`/`dark` theme and `enabled`/`reduced` motion
- preference updates use the current user revision and never silently overwrite a concurrent change

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
- arbitrary Host Connector command execution
- user-workload management through Runtime Manager
- unsigned native frontend code in the authenticated Shell origin
- frontend-only authorization
- secrets in browser storage
- long-running request polling when an event stream exists
- duplicate old and new implementations kept indefinitely
- broad catch blocks that report success
- generic plugin abstractions without a real package requirement
