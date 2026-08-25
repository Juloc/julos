# Work breakdown

This file is the issue blueprint for JulOS. Each item is intended to become one GitHub issue unless its implementation proves too large to review safely. Do not combine unrelated items.

Per-item completion **status** is tracked in `BACKLOG.md`, which is authoritative. The older per-item `Status:` lines in this file are not maintained past Phase 2 and must not be read as current progress. Some items added during implementation (for example `REL-PKG-001`, `REL-ALPHA-007` and `WEB-001`) are tracked in `BACKLOG.md` without a matching blueprint entry here.

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

Status: done.

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

Implemented as `src/JulOS.Desktop` with `typescript` as the only build dependency and no bundler. `moduleResolution` is `nodenext`, so relative imports must carry their `.js` extension and the emitted modules load in the browser unchanged.

The milestone needs real code to prove the pipeline, so it delivers the startup platform check: `platform-support.ts` detects missing Custom Element, Shadow DOM and CSS custom property support, and `main.ts` reveals a static bilingual notice in `static/index.html`. The notice is plain HTML because it must work before the localization service exists and in a browser that cannot run the shell. The shell surface itself belongs to `DESK-001`.

### FND-004 — Implement repository validation entrypoints

Status: done.

Depends on: FND-001, FND-003.

Deliver:

- `tools/validate.ps1`
- `tools/validate.sh`
- shared underlying validation commands
- Markdown and manifest validation hooks

Acceptance:

- both entrypoints run equivalent checks
- any failed stage returns non-zero and identifies the stage

Implemented as `tools/validate.mjs` with both entry points reduced to wrappers, so the two platforms cannot run different logic. `tools/lib/encoding-policy.mjs` turns decision `D012` into an executable check and backs `tools/normalize-encoding.mjs`.

Acceptance was verified by breaking a Markdown link and by removing a final newline; each failed its stage, named it and exited non-zero.

The manifest and container stages report `skipped` with the work item that implements them, so an unvalidated area is visible in the summary rather than counted as a pass.

### FND-005 — Add development Compose stack

Status: done.

Depends on: FND-001.

Deliver:

- Server and PostgreSQL services
- development volumes
- safe example environment file
- health and readiness wiring

Acceptance:

- fresh `docker compose up` reaches healthy state
- no real secret is committed

Implemented as `deploy/compose/compose.yaml` and `src/JulOS.Server/Dockerfile`, with base image tags pinned per decision `D020`.

Liveness reports only that the process runs, so a database outage cannot cause a restart loop. Readiness runs `PostgreSqlHealthCheck`, which opens a connection and executes a statement; a reachable server that has not finished starting or refuses authentication is not ready. Server refuses to start without `ConnectionStrings__CoreDatabase`, and the stack refuses to start without `JULOS_POSTGRES_PASSWORD`, so no default credential can reach a running system.

The container probe runs the application with `--health-check` instead of a shell tool, so the runtime image ships no HTTP client. No database port is published.

Acceptance was verified on a clean stack: both services reached `healthy`, `/health/live` and `/health/ready` returned 200, and stopping PostgreSQL moved readiness to 503 while liveness stayed 200.

### FND-006 — Add pull-request CI

Status: done.

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

Implemented as `.github/workflows/validation.yml`, which runs `sh tools/validate.sh` and nothing else. The workflow keeps no separate list of checks, so local and CI runs cannot diverge.

Only downloaded packages are cached, never build output or `node_modules`, and the run ends with `git diff --exit-code` so a tracked file modified by validation fails the build.

`API-001` adds the PostgreSQL service and real persistence integration tests. The workflow still invokes only `tools/validate.sh`; the service is an execution dependency, not a second validation command list.

### FND-007 — Add version and release metadata foundation

Status: done.

Depends on: FND-001.

Deliver:

- one repository version source
- assembly and image version propagation
- version shown in diagnostics and desktop footer/about
- release-note template

Acceptance:

- one version change updates all build outputs
- no `latest` dependency is required

Implemented as the root `VERSION` file, read by `Directory.Build.props` into `VersionPrefix`. The version is reachable through the assembly informational version, the startup log entry with event identifier 1000, `GET /api/v1/system/version` and the image label `org.opencontainers.image.version`.

The `version` validation stage reads the built project version back through MSBuild, so the file cannot silently stop being the source.

The Desktop footer and about surface belong to `DESK-001`, which builds the shell. The Desktop reads the version from `GET /api/v1/system/version` rather than embedding it, because the server is authoritative for the version it is running.

The release-note template is `docs/RELEASE_NOTES_TEMPLATE.md`.

## Phase 1 — Core platform

### CORE-001 — Implement core identifiers and clock abstraction

Status: done.

Depends on: FND-001.

Deliver stable IDs, UTC clock port and common revision value.

Acceptance:

- time-dependent tests use injected clock
- IDs are generated server-side

Implemented as `JulOS.Domain.Primitives` holding `Revision`, `EntityIdentifier` and `IIdentifierGenerator`, plus `DomainRuleViolationException` and the `TimeOrderedIdentifierGenerator` adapter. Decision `D024` records why the clock port is the base class library `TimeProvider` rather than a JulOS interface.

`tests/JulOS.Domain.Tests` and `tests/JulOS.Infrastructure.Tests` are created here with their first real tests, as decisions `D023` and `D025` require.

Every later `CORE` item builds on these primitives: one identifier type per entity, `Revision` for concurrency, `TimeProvider` for time, and `DomainRuleViolationException` with a stable code for a refusal.

### CORE-002 — Implement package lifecycle domain model

Status: done.

Depends on: CORE-001.

Deliver states, valid transitions and fault metadata.

Acceptance:

- invalid transitions fail explicitly
- transition tests cover install through removal

Implemented in `JulOS.Domain.Packages`. The transition table reproduces the graph in `PACKAGES.md` exactly, including the rule that a state with no running worker has no edge into `Faulted`: nothing there can crash.

Entering `Faulted` is only possible through `Fault`, which requires a code, a description and a time. `TransitionTo` refuses that target explicitly, so a fault can never be recorded without a reason to show the operator.

`PackageId` is the publisher's stable reverse domain name and is fixed for the life of the record. Removing and installing a package again produces a new installation record for the same package.

Deliberately absent: installed version, manifest schema version, publisher, signature thumbprint, configuration state and health state from `DATA_AND_API_CONTRACTS.md` section 2.3. Those are defined by the manifest and the verification and health work items, and are added by `PKG-001`, `PKG-002` and `PKG-007`.

### CORE-003 — Implement applications and launch-target domain model

Status: done.

Depends on: CORE-001.

Deliver application definitions, instance policies, launch targets and approval states.

Acceptance:

- stable keys and external identities are enforced
- display names are not identity fields

Implemented in `JulOS.Domain.Applications`. The second criterion is structural rather than a convention:

- `ApplicationDefinition` holds a `LocalizationKey` and no display text at all, so there is nothing to mistake for a name. Renaming points at a different key and leaves identity untouched.
- `LaunchTarget` identity is the owning package plus the `ExternalIdentity`. The label is separate and changes on every observation without affecting identity or approval.

`Observe` never changes the approval state. An ignored target therefore stays ignored across inventory passes instead of reappearing as new, which is what `DKR-004` and `DISC-005` require.

`ViewportClass` is added to `JulOS.Domain.Primitives` because both this item and `CORE-004` need the same vocabulary.

The identifier type is `ApplicationDefinitionId`, not `ApplicationId`, because `System.ApplicationId` exists and the collision would force an alias in every consuming file.

Deliberately absent: application type, module reference, custom element name and launch contract version from `DATA_AND_API_CONTRACTS.md` section 2.5, and the launch parameters from section 2.6. All are declared by the package manifest and are defined by `PKG-001`.

### CORE-004 — Implement desktop layout domain model

Status: done.

Depends on: CORE-001.

Deliver layouts, windows, widget placements, viewport classes and revisions.

Acceptance:

- invalid bounds and duplicate z-order normalization are tested
- mobile and desktop layouts remain separate

Implemented in `JulOS.Domain.Layouts`.

A layout belongs to exactly one `ViewportClass`, so a phone layout and a desktop layout are separate records and neither can overwrite the other.

Z-order is derived, not stored input. The window list order is authoritative and `NormalizeZOrder` renumbers it into a gap-free sequence after every change. A stored layout that arrives with two windows on the same index cannot survive, because a duplicate index makes a click land on an arbitrary window.

`WindowBounds` allows a negative origin, because a window may overhang while it is dragged, but `ClampToReachable` pulls a title bar back inside the usable area. A title bar outside it can never be grabbed again.

`SnapGeometry` is pure arithmetic shared by the preview and the stored bounds, so the two cannot disagree. Halves are computed as `floor` and `remainder`, which keeps a one-pixel seam from appearing on an odd width.

A window in a fixed state refuses `MoveTo` rather than accepting a geometry it does not have, and `Unminimize` returns it to the state it was in rather than silently discarding a maximized or snapped arrangement.

Deliberately absent: the owning user, the layout name and the default flag from `DATA_AND_API_CONTRACTS.md` section 2.7, and the widget settings payload from section 2.9. Ownership belongs to `API-001` and the settings schema is package-defined, so it belongs to `PKG-001`.

### CORE-005 — Implement session-reference domain model

Status: done.

Depends on: CORE-001.

Deliver protocol-neutral states and lifecycle policy.

Acceptance:

- window close and session termination are distinct
- invalid lifecycle transitions fail

Implemented in `JulOS.Domain.Sessions`. A closing window enters the aggregate only through `ApplyWindowClosed`, which dispatches through the session's `SessionLifecyclePolicy`. Closing a window can therefore disconnect, suspend or end a session, but it can never terminate one implicitly, which is decision `D018` made structural.

`SessionRequest` carries an opaque kind and target reference owned by the requesting package, so no protocol name enters Core.

Deliberately absent from the aggregate: the owning package, the user and the expiry timestamp listed in `DATA_AND_API_CONTRACTS.md` section 2.10. `ARCHITECTURE.md` section 11.1 limits what Core sees to the request, reference, state, lifecycle policy and failure code. Those three fields are ownership and persistence concerns and are mapped by `API-001`.

### CORE-006 — Implement Agent domain model

Status: done.

Depends on: CORE-001.

Deliver identity, enrollment, state, capabilities and revocation.

Acceptance:

- revoked Agent cannot transition to connected
- last-seen is not interpreted as a metric value

Implemented in `JulOS.Domain.Agents`. `Revoked` has an empty outgoing transition set, so revocation is terminal in the type rather than by convention.

The second criterion is structural. `AgentHeartbeat` is a value with exactly one member, the moment, and exactly one way to be produced, from the clock. There is no constructor that accepts a measurement, so a heartbeat cannot be mistaken for or repurposed as an observation of the host. Reflection tests fail the build if that changes.

`CapabilityName` validates a generic dotted format and hard-codes no product, which keeps the capability vocabulary open while the Domain stays product-free.

`CapabilityVersion` is separate from `Revision`: the Agent reports the former, and Core owns the latter for its own concurrency.

The aggregate deliberately carries no credential, key or token. Section 2.11 of `DATA_AND_API_CONTRACTS.md` defines none, and the Domain has no mechanism to protect a secret. `CapabilityMetadata` documents the same constraint, because an opaque payload is where a careless caller would otherwise put one.

### CORE-007 — Implement problem, notification and audit models

Status: done.

Depends on: CORE-001.

Deliver deduplication identity, state transitions, notification metadata and append-only audit contract.

Acceptance:

- repeated observations update one problem
- resolved problems can reopen on a new observation

Implemented in `JulOS.Domain.Observability`.

`ProblemIdentity` is the reporting package, the condition type and the stable resource identity. `Observe` refuses an identity that does not match, so a restart loop seen a hundred times stays one problem with a rising observation count instead of a hundred entries to dismiss.

A resolved problem reopens on a new observation, because the condition is back and hiding it would leave an operator believing a fixed system is still fixed. An acknowledged or suppressed problem keeps that state: both are decisions the operator made about this exact condition, and the next poll must not undo them.

Severity is a named, ordered value and never a colour, because colour alone is not a usable signal for a colour-blind operator or in a monochrome export.

`AuditEvent` is append-only by construction. Every member is read-only and the type exposes no instance method at all; two reflection tests fail the build if that changes. `AuditOutcome` separates `Denied` from `Failed`, because repeated denials are a security signal and merging them would hide it inside operational noise.

`Notification` carries a deduplication key so an event arriving on every poll produces one message rather than a stream the user learns to dismiss unread.

Deliberately absent: the owning user and the deep link from `DATA_AND_API_CONTRACTS.md` sections 2.15 and 2.16, and the actor, agent and remote address from section 2.17. All are ownership and transport fields mapped by `API-001` and `API-009`.

### CORE-008 — Implement permission and scope model

Status: done.

Depends on: CORE-001.

Deliver permission strings, subject assignments and target scopes.

Acceptance:

- read and control permissions remain separate
- scope evaluation tests cover global, package and resource scopes

Implemented in `JulOS.Domain.Permissions`. `PermissionEvaluator.Grants` is a pure function over an assignment set, so an authorization decision cannot depend on ambient state and is fully testable.

Read and control separation is structural: permission equality is exact, and the evaluator never derives one permission from another. A test proves that holding a read permission grants nothing about control.

Default deny. An empty assignment set grants nothing, and a narrow grant never widens: a package or resource scope satisfies only an exact same-kind, same-identity target, while a global scope satisfies any target.

`PermissionAssignment` carries no revision, because the table in `DATA_AND_API_CONTRACTS.md` section 2.2 declares none. A grant is created and withdrawn rather than edited in place, which also keeps the audit trail readable.

## Phase 2 — Persistence, authentication and core APIs

### API-001 — Add PostgreSQL core persistence

Status: done.

Depends on: CORE-002 through CORE-008, FND-005.

Deliver DbContext, mappings, first migration and migration command.

Acceptance:

- empty database migrates successfully
- constraints reflect domain invariants

Implemented as `CoreDbContext` and relational storage rows in `JulOS.Infrastructure.Persistence.Core`. The context owns only the `core` schema; package schemas remain outside it. Stable identifiers are never database-generated, mutable rows map `Revision` as a concurrency token, and PostgreSQL check constraints preserve domain invariants even when data enters below the application layer. Audit events are protected by an update/delete trigger as well as their immutable domain type.

The committed migration is applied only through `JulOS.Server --migrate-database`. The development Compose stack runs that command in a one-shot service and starts Server only after it succeeds, so normal startup never changes the schema.

`tests/JulOS.Integration.Tests` creates isolated databases on a real PostgreSQL service and proves that an empty database migrates, invalid states are rejected and audit rows are append-only. CI supplies the service through `JULOS_TEST_POSTGRES`. Since decision `D033`, SQLite is also a supported core-store provider — the single-host default when no provider or connection string is configured — and is exercised by other integration tests (for example the web-application proxy tests); the persistence-integration suite in this project targets PostgreSQL.

### API-002 — Add optimistic concurrency

Status: done.

Depends on: API-001.

Deliver revision handling for layouts, settings, packages and connections.

Acceptance:

- stale update returns conflict with current revision
- silent last-write-wins does not occur

Implemented by mapping every currently persisted mutable row revision as an Entity Framework Core concurrency token. `CoreDbContext` translates provider conflicts into the transport-neutral `ConcurrencyConflictException`, reads the authoritative stored revision and never retries or overwrites automatically.

The Server maps that exception through the existing API-006 error pipeline to HTTP 409 with `request.concurrency_conflict` and the `currentRevision` Problem Details extension. Real PostgreSQL integration tests prove that a stale package write fails, the newer row remains unchanged and the public error contract contains the current revision. Layout, window, widget, session, Agent and problem rows use the same model rule; settings receive it when API-005 introduces their persistence.

### API-003 — Add local authentication

Status: done.

Depends on: API-001.

Deliver initial admin setup, login, logout, secure cookies, lockout and session timeout.

Acceptance:

- desktop and APIs reject unauthenticated users
- login rate limiting is tested

Implemented with ASP.NET Core Identity persisted in the existing `core` schema. The singleton `authentication_setup` row is locked inside one database transaction, so only one initial administrator can be created even when setup requests race. The administrator receives the system `Administrator` role, while permission evaluation and role-management endpoints remain owned by `API-004`.

The Server uses a secure, HTTP-only, same-site session cookie, a validated configurable session timeout, lockout after repeated failures and a per-IP fixed-window limit for setup and login. Login failures deliberately return one public code for an unknown user, a wrong password and a locked account. Logout requires a valid antiforgery token.

A fallback authorization policy protects every endpoint unless it is explicitly anonymous. Only authentication setup/status/login and health probes are anonymous. Integration tests run against migrated PostgreSQL and prove one-time setup, protected APIs, cookie attributes, lockout, rate limiting, antiforgery logout and configurable session expiry.

### API-004 — Add role and permission authorization

Status: done.

Depends on: API-003, CORE-008.

Deliver backend policies and administrator role management foundation.

Includes attaching an authorization policy to `GET /api/v1/system/version`, which `FND-007` added while no authentication existed.

Acceptance:

- every mutation endpoint requires a policy
- unauthorized calls return 401 or 403 correctly
- no endpoint outside authentication and health is reachable unauthenticated

Implemented through `JulOS.Application.Authorization`, the Infrastructure-backed permission reader and role administrator, and policy handlers in `JulOS.Server.Authorization`. Permission evaluation uses the existing pure Domain evaluator and combines direct user grants with grants inherited from local Identity roles. There is no administrator bypass: the system administrator role receives the three initial Core permissions as ordinary global assignments.

The version endpoint requires `core.system.version.read`. Authorization administration is split into read and manage permissions. Role, membership and grant mutations require both the manage policy and antiforgery validation. System roles cannot be renamed or deleted, and the last administrator cannot be removed.

Migration `20260802131339_AddRoleAuthorization` adds role descriptions, makes global grants genuinely unique with PostgreSQL `NULLS NOT DISTINCT`, and backfills explicit administrator grants for installations upgraded from `API-003`. Integration tests cover anonymous `401`, authenticated `403`, role inheritance, administrative mutations, system-role safety, antiforgery metadata and the upgrade backfill against PostgreSQL.

### API-005 — Add profile and preferences API

Status: done.

Depends on: API-003.

Deliver language, timezone, theme and motion preferences.

Acceptance:

- English and German are valid
- invalid timezone and locale fail validation

Implemented through versioned Profile contracts, an Application profile port, the Core-backed Infrastructure service and authenticated Server endpoints. The current user can read only their own profile and change only the supported preference fields. Mutations require the common antiforgery contract and the caller's current revision.

Migration `AddProfilePreferences` adds the persisted motion mode and enforces the supported language and motion values in PostgreSQL. Integration tests cover defaults, valid German and `Europe/Berlin` updates, invalid locale and time-zone rejection, antiforgery, endpoint metadata and stale-revision conflicts.

### API-006 — Add common Problem Details and correlation IDs

Status: done.

Depends on: FND-001.

Deliver middleware and stable error codes.

Acceptance:

- API errors include correlation ID
- stack traces and secrets are absent

Implemented as `JulOS.Contracts.Errors` for the public member names and platform codes, and `JulOS.Server.Errors` for the correlation middleware and the single problem customiser that runs for handled failures and unhandled exceptions alike, so no failure path can return a differently shaped body.

The developer exception page is deliberately never enabled. A response shape that differs between environments hides the production behaviour that needs testing.

A caller-supplied `X-Correlation-Id` is echoed only when it is a short run of unreserved characters, otherwise it is replaced. An accepted value reaches log files and a response header, where a line break or a control character would let a caller forge an entry.

Nothing derived from an unhandled exception reaches the client, because the message can carry a connection string or a credential. The correlation identifier is how the caller and the server-side entry are matched.

`tests/JulOS.Integration.Tests` is created here and drives the real host through `WebApplicationFactory`.

### API-007 — Add operation-resource framework

Depends on: API-001, API-006.

Deliver queued/running/succeeded/failed/cancelled operations and progress events.

Acceptance:

- background work is not reported as success before completion
- operation failure retains a safe cause

Status: done.

Implemented through versioned operation and progress contracts, an Application lifecycle port, PostgreSQL-backed Infrastructure storage and permission-protected Server endpoints. Creation is idempotent per user and key. Queued resources remain queued until an owning executor explicitly starts them; only the executor can mark verified work as succeeded. Progress events are immutable and update the current summary atomically.

Running cancellation is a durable request observed by the owning worker or target Host Connector through the Application service; queued cancellation becomes terminal immediately. Failed operations persist only a stable code and caller-safe detail. Migration `AddOperationResources` creates the operation and progress tables and backfills the three explicit operation permissions for an existing administrator role.

Integration tests cover antiforgery, idempotency conflict, reconnect-safe reads, progress ordering, persistent cancellation and safe failed-operation causes against real PostgreSQL.

### API-008 — Add secret-reference service

Depends on: API-001, API-004.

Deliver encrypted storage, opaque references, create/rotate/delete and lease port.

Acceptance:

- secret value is never returned after creation
- logs and audit tests contain no plaintext

Status: done.

Implemented through versioned metadata-only contracts, an Application service and lease port, AES-256-GCM protection in Infrastructure, Core PostgreSQL persistence and permission-protected Server endpoints. The active encryption key and retained decryption keys are loaded from external `*.key` files, never from PostgreSQL or ordinary configuration columns. Associated data binds ciphertext to the opaque reference, scope and purpose so copied or altered records fail authentication.

Create and rotate accept a value only for the current request and return metadata without the value. Delete destroys the nonce, ciphertext, authentication tag and key identifier while retaining a revisioned tombstone. Every mutation appends an audit event whose summary and safe details state only that the value was omitted.

The lease port releases decrypted bytes only for a running, non-cancelling operation whose Core or package identity owns the secret scope. Leases expire within the configured short lifetime and zero their buffers on expiry or disposal. Integration tests verify encryption at rest, response and audit redaction, antiforgery, optimistic concurrency, scope denial, rotation, deletion and real PostgreSQL constraints.

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

Includes the footer and about surface showing the server version from `GET /api/v1/system/version`, which `FND-007` prepared.

Acceptance:

- system, light and dark themes work
- English and German shell strings exist
- the running server version is visible without opening developer tools

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

### DESK-013 — Complete browser first-run and sign-in

Status: done. Detailed delivered behavior and acceptance evidence are owned by `DESKTOP_UX_COMPLETION.md` and summarized in `BACKLOG.md`.

### DESK-014 — Compose the production Shell

Status: done. The canonical launcher/window/taskbar/package/widget composition and acceptance record are in `DESKTOP_UX_COMPLETION.md`. HCON-002 depends on this completed item.

### DESK-015 — Complete cross-platform desktop interaction

Status: done; deployed Windows/macOS/touch evidence remains a release-gate check. Multi-display and interaction details are in `DESKTOP_UX_COMPLETION.md` and `MULTI-DISPLAY.md`.

### DESK-016 — Complete appearance and personalization foundation

Status: done for the documented shipped theme/motion/token scope. Deferred personalization is not silently part of this item; see `DESKTOP_UX_COMPLETION.md` and `UI_DESIGN_SYSTEM.md`.

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

This records the completed initial trusted-only path. `PKG-013` and `PKG-014` deliberately supersede the publisher gate for unknown/unsigned content only after isolation and digest-bound acknowledgement; invalid integrity remains rejected.

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

Historical terminology note: `AGT-001` through `AGT-006` were implemented and released under the former **Agent** name. Do not rename historical commits, migrations, release notes or work-item IDs. `HCON-002` uses this implementation as its migration source and replaces current product/API/process terminology atomically; no new work extends an `AGT-*` contract.

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

### REM-009 — Implement provider-neutral terminal presentation

Depends on: REM-004, REM-005, HCON-005.

Deliver:

- versioned terminal WebSocket contract separate from graphical Guacamole display
- terminal renderer with UTF-8 input/output, resize, focus release and mobile keyboard support
- reconnect, idle timeout, maximum duration and explicit end semantics
- package-facing session creation contract that identifies one already-authorized terminal provider target

Out of scope:

- generic Host Connector or Server shell
- reuse of SSH target credentials for Docker exec
- terminal input/output recording

Acceptance:

- resize and multibyte input survive round trips
- disconnect/reconnect follows explicit session policy
- expiry and Window close end presentation without inventing success
- no graphical Remote/Guacamole payload is required for a terminal-only provider
- automated contract, authorization, reconnect, timeout and mobile-keyboard tests pass

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

Deliver tabs, address field, navigation, downloads and session status. Downloads depend on `FILE-007` (Phase 8); until it lands, Browser download support is deferred and BRW-004 is accepted without it (see the BRW-004 note in `BACKLOG.md`).

Acceptance:

- multiple isolated windows work
- startup stages and failures are clear

### BRW-005 — Implement fixed web-application mode

Depends on: BRW-004, CORE-003.

Deliver app-branded launch target with optional minimal chrome.

Acceptance:

- app mode remains a full browser session, not iframe
- policy can allow opening in full browser mode

## Phase 6A — Product realignment foundations

This phase is mandatory before new host, catalog or mobile behavior. It replaces incorrect product terminology and persistence assumptions without keeping dual implementations. Normative details are in `HOST_CONNECTOR.md`, `MOBILE_PWA.md` and `APPLICATION_CATALOG.md`.

### SPEC-001 — Reconcile the JulOS product concept

Depends on: current Phase 6 implementation evidence and accepted user decisions.

Deliver:

- accepted Host Connector, open application catalog and PWA/mobile target specifications
- synchronized Product, Concept, Architecture, Technical, UX, Package, Data, Security, Quality, implementation, backlog, decision and glossary documents
- explicit migration boundaries for legacy Agent terminology, viewport layouts and signed-only package policy
- branch disposition for the open package-route fix and stale Docker implementation branch

Out of scope:

- production code or schema changes
- merging the existing Docker branch
- claiming any new target behavior is implemented

Acceptance:

- no authoritative specification contradicts decisions D009, D016 and D036 through D041
- every new implementation item has dependencies, deliverables, exclusions and acceptance criteria
- historical releases/migrations remain factual and current target terminology is unambiguous
- Markdown links and repository policy validation pass

### STAB-001 — Integrate the real-Kestrel package-route fix

Depends on: SPEC-001. Historical source: regression commit `31a11ba` from the former `origin/agent/fix-package-route-fallback` branch.

Completion record: Done on `main` as `0ef293c` and released in `0.4.0-beta.19`; the source branch is deleted. This blueprint remains for traceability and is not selectable work.

Deliver:

- replace the routed catch-all that suppresses package parameter routes under Kestrel
- preserve Web App proxy handling and typed JulOS `request.not_found` responses
- add the `server-smoke` validation stage that boots real Kestrel
- assert package enable, disable, remove and update route families are routed
- wire the smoke into release validation and reconcile `QUALITY_AND_TESTING.md`

Out of scope:

- Host Connector, catalog, mobile or Docker behavior
- weakening the authenticated fallback policy
- treating in-memory `TestServer` coverage as real-host evidence

Acceptance:

- an unknown package action reaches its handler and returns a package-owned error rather than `request.not_found`
- a genuinely unknown route returns HTTP 404 with `request.not_found`
- Web App proxy host routing still executes before the not-found middleware
- `server-smoke` and full repository validation pass on the integrated `main`
- the remote fix branch is deleted only after the verified commit is reachable from `main`

### DB-001 — Add supported SQLite schema upgrades

Depends on: API-001, D033 and SPEC-001.

Deliver:

- an Infrastructure-owned ordered SQLite migration runner used only by `--migrate-database`
- `__julos_schema_history` containing migration ID, checksum and applied timestamp
- a deterministic baseline detector for the exact last supported `EnsureCreated` beta schema
- a fresh-database baseline migration and a real previous-beta SQLite fixture
- transaction, checksum and interrupted-migration diagnostics
- removal of `EnsureCreated` from production upgrade behavior
- provider-aware backup/restore path for default SQLite before the first schema-changing cutover

Implementation rules:

- PostgreSQL continues to use committed EF migrations; old migrations are never rewritten
- SQLite migration classes/scripts are provider-specific where PostgreSQL SQL cannot apply
- a legacy schema is baselined only when its complete expected table/index fingerprint matches
- unknown/partial schemas fail with `database.sqlite_schema_unsupported`; they are not guessed or repaired
- normal Server startup never changes schema
- SQLite backup stops mutations, checkpoints WAL, uses the SQLite backup API into staging, runs `integrity_check`, writes checksums and publishes atomically; restore verifies/stages before replacing the explicit database file

Acceptance:

- fresh SQLite and upgraded previous-beta SQLite expose the same current model
- running migration twice is idempotent
- checksum mismatch or injected failure leaves the previous transaction usable
- real fixture retains users, packages, Agent/Host Metrics, layouts, sessions, secrets and audit rows
- scripted SQLite backup/restore drill reproduces the pre-migration database byte-logically and leaves the source unchanged on injected failure
- documentation and backup/restore runbook name the supported rollback limit

### HCON-001 — Lock Host Connector contracts and migration constants

Depends on: SPEC-001.

Deliver:

- committed public/runtime contract fixtures from `HOST_CONNECTOR.md`
- canonical Host Connector IDs, event names, errors, permissions, paths and headers
- CredentialV1 enrollment/two-phase rotation, heartbeat/long-poll and typed result endpoint fixtures
- documented `MachineIdentityNamespaceV1` retaining the legacy hash namespace
- complete database, credential-rotation and identity-file migration map

Out of scope: executable rename or compatibility adapter.

Acceptance:

- fixtures cover minimum, complete, malformed, result exact-retry/conflict and unsupported-major messages
- no target contract contains a generic command, shell, TCP destination or Docker API payload
- old protocol can only become a non-executing 426 tombstone during the cutover

### HCON-002 — Replace Agent with Host Connector atomically

Depends on: HCON-001, DB-001, completed AGT-001 through AGT-006, DESK-014 and PKG-012.

Deliver in one vertical commit:

- project, namespace, type, API, protocol-v1 heartbeat/long-poll, environment, service/image and current documentation rename
- PostgreSQL and SQLite table/column/index/constraint migration preserving durable IDs/history, with a bounded old-schema drain-only host that archives legacy Commands instead of coercing them into typed requests
- protected default/custom-path identity migration preserving ID, CredentialV1 and MachineIdentityV1, including both-file conflict handling
- client-generated credential enrollment plus administrator-initiated two-phase rotation with pending-hash persistence and crash recovery
- Host Connector permission creation, explicit role/assignment backfill and endpoint policy cutover
- Host Metrics `2.0.0` with `hostConnectorId`
- Settings → Hosts → Host access administration
- removal of Agent launcher/taskbar app and browser-facing generic command creation
- exact 426 legacy runtime tombstone only for the documented transition release
- English and German user-facing text, pre-cutover backup/drain/failure-recovery/rollback-limit runbook and upgrade note

Out of scope:

- Docker, Files or Discovery feature expansion
- automatic binary update
- permanent old/new dual route

Acceptance:

- clean install, PostgreSQL fixture, SQLite fixture and real identity-file upgrade pass
- enrollment, exact retry, credential rotation crash/timeout recovery, heartbeat/long-poll, typed success/failure/cancelled results, metrics, diagnostics, rename and revoke work under new contracts
- queued legacy Commands become failed archive rows with `agent.command_cancelled_for_upgrade`; active legacy work blocks migration; succeeded/failed/expired/cancelled rows retain their exact terminal data and never become fake success
- read/manage/diagnostics/Admin and 401/403 permission migration matrix passes idempotently
- revoked and legacy binaries execute no request after cutover
- current UI/API/assemblies contain no product Agent surface outside explicit historical allowlist
- credentials and tokens are absent from logs, diagnostics and migration output

### HCON-003 — Validate and publish the Host Connector upgrade

Depends on: HCON-002.

Deliver:

- deployed clean-install and supported-upgrade evidence
- published Host Connector image/binary and Host Metrics package with immutable version/digest
- executed evidence for the HCON-002 upgrade, failure-recovery and rollback-limit runbooks
- removal date for the non-executing legacy tombstone

Acceptance:

- a previously enrolled real host reconnects without re-enrollment
- Server, Connector and official package version mismatch is actionable
- legacy identity file is removed only after verified new-file write
- release smoke covers Host access UI, metrics, default/custom identity paths, credential rotation, revoke and diagnostics

### HCON-004 — Add typed host-adapter request registry

Depends on: HCON-002, PKG-009.

Deliver:

- registry keyed by capability name/version, operation name, payload schema version and result schema version
- per-operation request/result validators, authorization/scope descriptor, deadline, replay-safe/reconcile-required classification and result-size policy
- protected atomic local request/result journal with prepared/executing/result-ready restart rules, acknowledgement deletion and bounded scrubbed orphan retention
- internal dispatch API for package-owned providers
- rejection of unknown/disabled capabilities before queue persistence

Out of scope: arbitrary extension scripts, generic JSON commands or streaming.

Acceptance:

- an unregistered tuple cannot be queued or executed
- target scope is checked by Server and independently by Connector
- cancellation/deadline/result limit propagate end to end
- disconnect before result redelivers only replay-safe work; reconcile-required unknown outcome terminates visibly as failed/expired and can only use a new read-only reconciliation request
- crash injection at every journal write/call/submit/ack boundary never duplicates reconcile-required execution and exact result bytes survive restart
- architecture tests prove packages do not reference Connector implementation types

### HCON-005 — Add target-bound multiplexed Host Connector streams

Depends on: HCON-003, HCON-004, API-007, API-009.

Deliver:

- authenticated stream grant and multiplexing contract from `HOST_CONNECTOR.md`
- binding to parent Operation, user, capability, target, direction, expiry and byte limits
- backpressure, cancellation, idle timeout and disconnect cleanup
- Server/Connector diagnostics with no payload content

Out of scope: arbitrary TCP proxy, host shell or target selection by the Connector.

Acceptance:

- a grant cannot be replayed for another target/capability/Connector
- cancellation and disconnect close both directions predictably
- byte/idle/expiry limits fail with stable codes
- WEB-001 and REM-009 can consume the same transport without bypassing their own authorization

### MOB-001 — Lock PWA, workspace, lifecycle and Back contracts

Depends on: SPEC-001.

Deliver:

- committed contract fixtures and canonical vocabulary from `MOBILE_PWA.md`
- workspace/layout resolution and migration fixtures
- package Surface lifecycle and Shell navigation interfaces
- documented cache allow/deny matrix

Acceptance:

- Phone, Tablet, desktop-single and desktop-multi behavior has one unambiguous owner
- Window, Surface and Session state cannot be conflated in contracts
- no hardware fingerprint or offline-mutation queue is permitted

### MOB-002 — Add installable PWA Shell

Depends on: MOB-001, FND-003, DESK-001, DESK-002.

Deliver:

- manifest, complete icons/maskable icons and standalone install metadata
- versioned service worker and immutable Shell-asset cache
- disconnected and update-available Shell surfaces in English and German
- Page/Service-Worker update handshake with per-client flush/conflict/offline/discard behavior
- safe-area, `100dvh`, `VisualViewport` and software-keyboard handling

Acceptance:

- installability audit passes on supported Android/iOS/Desktop targets
- API/auth/antiforgery/secret/operation/session/display/proxy responses never enter persistent cache
- offline start shows unavailable/Retry and performs no mutation
- activation never forces reload; each page reloads only after clean/flush or explicit current-page discard

### MOB-003 — Add client-device registration and preferences

Depends on: MOB-001, DB-001, API-002, API-003, API-004.

Deliver:

- `ClientDevice`, `DeviceWorkspacePreference` and `ApplicationExecutionPreference` persistence
- server-generated random device key, Secure/HTTP-only/SameSite-Strict cookie, hash-only storage and owner-scoped APIs
- fixed capability classification, device-level Workspace override and exact registration/re-registration/delete-revision DTO/status behavior
- Settings UI to name/remove devices and choose shared/device plus resume/fresh
- `client_device.changed`, antiforgery, optimistic concurrency and documented audit behavior

Acceptance:

- device key cannot authenticate or access another user's resources
- clearing site data creates a visible new device identity
- missing/deleted/cross-user cookie never reveals or adopts another user's device
- device removal deletes only that device's layouts/preferences
- concurrent preference writes return 409 with authoritative revision

### MOB-004 — Replace viewport-only layout identity

Depends on: MOB-003, CORE-004, DESK-008, DB-001.

Deliver:

- Workspace-class/layout-scope Domain and API model
- PostgreSQL and SQLite migration from desktop/tablet/mobile shared layouts
- provider-equivalent partial unique indexes, composite ownership/window references, denormalized immutable Window Workspace Class and Phone/display-slot state checks
- resolver for shared, device and fresh modes
- stable `DisplaySlot` field and separate desktop-multi initialization
- deletion of the old viewport route in the same cutover
- `workspace_layout.changed` publication and authoritative client refresh

Acceptance:

- migrated windows, widgets, bounds and revisions remain intact
- migrated Mobile layout chooses deterministic Primary, keeps every other Window and never fabricates Split
- Phone/Tablet/Desktop writes cannot overwrite another class
- fresh mode performs no persistence
- no indefinite old/new layout API exists

### MOB-005 — Implement Phone Split and Tablet desktop presentation

Depends on: MOB-004, DESK-003 through DESK-009.

Deliver:

- Phone Single/explicit Split stage, divider, focus and task-switcher integration
- Portrait top/bottom and Landscape left/right geometry
- third-app focused-pane replacement and background transition
- Tablet maximized/tiled defaults and capability-aware free windows
- logical display-slot restoration for desktop-multi

Acceptance:

- Phone never shows more than two foreground Windows
- ratio persists only between 25% and 75%
- orientation/software-keyboard changes do not change layout identity
- Tablet holds at least two visible apps and supports touch, keyboard and pointer

### MOB-006 — Implement versioned package Surface lifecycle

Depends on: MOB-001, PKG-001, PKG-010, MOB-003.

Deliver:

- exact `Applications[].Surface` manifest fields and async host for activate/deactivate/suspend/resume/Back/dispose
- Surface lifecycle scheduler and per-app/background preference resolution
- serialized/idempotent transitions, visible-unfocused state, reason enums, AbortSignal and deadlines
- instrumentation proving suspended surfaces stop frontend work
- explicit unsupported-major failure

Acceptance:

- packages cannot enable `keep-surface-active` themselves
- suspended test app has no timers, polling, rendering, input or display activity
- dispose and suspend are observably different
- mobile-capable package without the supported contract does not silently run

### MOB-007 — Migrate Browser and Remote surfaces

Depends on: MOB-006, REM-004, REM-005, BRW-003, BRW-004.

Deliver:

- Browser and Remote Surface implementations that detach display/input/polling on suspend
- resume against authoritative Session identity/revision
- separation of element disconnect from Session termination
- package-specific Back behavior

Acceptance:

- Surface suspend never terminates a Browser/Remote Session by itself
- Window close still applies the documented Session lifecycle policy
- resume after Server/PWA reconnect shows current state or explicit expiry
- no duplicate display connection survives suspend

### MOB-008 — Add Desktop Operation Center

Depends on: API-007, API-010, DESK-002.

Deliver:

- queued/running/recent Operation list and detail
- owner-scoped cursor API with state/package/time filters and bounded page size
- `operation.changed` event handling followed by authoritative refresh
- explicit cancellation action and permission state
- resume after PWA restart

Acceptance:

- closing/suspending an originating Window does not cancel work
- at-least-once events do not duplicate Operations
- progress/failure/cancellation remain visible with stable codes

### MOB-009 — Add Shell-owned Back navigation

Depends on: MOB-006, MOB-007, DESK-006, DESK-007, DESK-012.

Deliver:

- one `ShellNavigationController` and sequence/epoch history state machine without a guard sentinel
- overlay → app → split/task → workspace → Root ordering
- `popstate`, supported mouse Back and platform Back integration
- runtime bridge hook for proxied apps and package Surface hook

Acceptance:

- each layer consumes exactly one Back step
- multi-entry browser jumps unwind deterministically and rejected/timed-out handlers cannot trap history
- app returning not-handled continues to Shell history
- Browser page Back stays inside streamed Browser
- JulOS Root allows platform exit and cannot trap navigation

### MOB-010 — Complete cross-device PWA release acceptance

Depends on: MOB-002 through MOB-009.

Deliver:

- automated and recorded manual matrix for Android Chrome PWA, iPhone/iPad Home-Screen/Safari, Tablet pointer/keyboard, Desktop PWA/tab and multi-monitor
- upgrade evidence from viewport layouts
- measured idle/suspended resource behavior

Acceptance:

- every `MOBILE_PWA.md` acceptance criterion passes on named targets
- shared layout then device override is verified with two devices
- durable Operation survives app switch and PWA restart
- no high-severity accessibility, data-loss or navigation defect remains

### CAT-001 — Implement application-catalog schemas and validator

Depends on: SPEC-001, STAB-001, FND-004.

Deliver:

- `app-catalog-index.v1`, `app-manifest.v1` and optional `app-catalog-keyset.v1` JSON schemas
- bundle/path/size/symlink rules and `x-julos` schema
- supported Compose subset validator and deterministic normalization
- minimum, complete, all-delivery, extension, malformed and unsupported-feature fixtures
- `catalog-manifests` repository validation stage

Acceptance:

- unknown ordinary fields, traversal, symlinks, duplicate identities and unsupported required features fail
- unknown `x-*` metadata is preserved but never executed
- unsupported Compose semantics fail rather than being ignored
- parse/canonicalize/reparse of every valid fixture produces the same definition and plan digests

### CAT-002 — Implement catalog sources, atomic cache and trust evaluation

Depends on: CAT-001, DB-001, API-007, API-008.

Deliver:

- source persistence and official/HTTPS/Git/OCI/local adapters
- Git commit and OCI/HTTP digest locking
- atomic last-valid cache with explicit stale state
- signature envelope verification, immutable Key ID/fingerprint and rotation/revocation policy
- persisted Catalog Publisher Key, cached-entry trust evidence and revisioned administrator trust decisions
- separate `catalog.trust.manage` permission with Administrator backfill and exact publisher-key list/read/trust API
- source permissions, Secret References, refresh Operation and Problems

Acceptance:

- failed/partial refresh never replaces the last valid cache
- private source credentials never reach client/logs
- source identity change and duplicate app identity fail clearly
- trusted-signed, unknown-signed, unsigned, invalid-signature and digest-mismatch fixtures behave exactly as documented
- trust/distrust/clear requires its dedicated permission, is audited, rejects stale revisions and cannot override invalid/revoked content

### PKG-013 — Isolate unknown native extension code

Depends on: PKG-003, PKG-010, CAT-001.

Deliver:

- separate package origin or sandboxed frame with typed message bridge for unknown frontends
- no JulOS session cookie, Shell DOM or arbitrary Core API on that origin
- resource-limited Runtime-Manager worker-container profile for unknown backend workers
- refusal of unknown `process` workers under Server identity

Acceptance:

- malicious fixture cannot read cookies, DOM, tokens or call undeclared endpoints
- bridge authorization and schema rejection are backend enforced
- worker receives only declared storage/network/channel and cannot inspect unrelated runtimes
- trusted official package path remains functional without a duplicate application model

### PKG-014 — Make publisher signatures optional with digest-bound acknowledgement

Depends on: PKG-002, PKG-008, PKG-011, PKG-013, CAT-002.

Deliver:

- four signature/trust presentation states and immutable digest enforcement
- install/update preview with exact warning codes and plan/artifact digest
- administrator acknowledgement bound to that digest and operation
- pause automatic update when source, key, trust or critical rights change

Acceptance:

- unsigned and cryptographically valid unknown-signed artifacts can install after warning through isolated paths
- claimed-invalid signature and digest mismatch always fail
- approval cannot be replayed for changed bytes or rights
- official signed package release flow remains trusted and unchanged

## Phase 7 — Docker, application delivery and Proxmox

### CONN-001 — Implement provider-neutral external Connections

Depends on: DB-001, API-004, API-008, CAT-001, CORE-003.

Deliver:

- Connection Domain/persistence/API from `APPLICATION_CATALOG.md`
- `connections.read`, `connections.manage` and `connections.validate` permissions with Administrator backfill
- provider capability name/version instead of implementation reference
- sanitized endpoint, optional routed Host Connector and versioned settings
- Connection Secret Bindings, `connection` Secret Reference scope and validation Operation
- explicit external launch-target registration

Acceptance:

- no endpoint embeds credentials and no API returns a value
- standalone Connection creation/configuration has no App Installation dependency
- connection validation is permission-checked, cancellable and observable
- deleting a Connection never deletes the external service
- cross-user/package scopes and stale revisions fail

### APP-001 — Implement App Installation domain, lock and preview/apply APIs

Depends on: CAT-002, CONN-001, API-007, PKG-014.

Deliver:

- App Installation with separate lifecycle, desired/observed runtime and health state
- catalog source tombstone references, provider-validated target Connection, installed/desired versions and deployment lock
- immutable schema-v1 Deployment Lock and single-use exact-digest Approval persistence
- mutation-free Preview with expiry, Plan Digest, rights/conflicts/data effects and trust result
- Apply request containing only Preview ID, Plan Digest, warning acknowledgements and Idempotency Key
- durable operation and stable errors

Out of scope: Docker apply, adoption, update, backup and uninstall.

Acceptance:

- Preview writes no Installation/resource/external state
- changed/expired plan cannot reuse acknowledgement
- Deployment Lock digest excludes relational row IDs, includes Trust Assessment, and rejects every unknown payload field
- native-extension delivery delegates to Package Installation lifecycle
- Domain contains no Docker or Compose types

### API-011 — Add app-installation Secret Bindings and Connector leases

Depends on: APP-001, API-008, HCON-004.

Deliver:

- `app-installation` Secret Reference scope and App Secret Binding persistence
- one-use operation-bound lease binding Installation, Connector, Binding, capability, target and approved Plan Digest
- authenticated out-of-band secret delivery and buffer disposal contract
- audit metadata without values

Acceptance:

- lease issues only for matching running, non-cancelling Operation
- secret bytes never enter preview/apply/Operation/Connector queue/result/audit/log persistence
- expiry/retry obtains a new lease and replay fails
- Connector zeroes/disposes value after Docker handoff

### DKR-001 — Implement bounded Host Connector Docker adapter

Depends on: HCON-004, CONN-001, PKG-009.

Deliver:

- typed `docker.inventory/1`, `docker.logs/1` and `docker.control/1` adapters
- provider-validated `docker-engine` Connection settings with one stable Engine ID and optional Host Connector route
- configured engine scope and independent local validation
- read versus control permission/scope separation
- selective port of useful behavior/tests from `origin/agent/docker-phase-completion`, never a wholesale merge

Out of scope: user app deployment and generic Docker API forwarding.

Acceptance:

- Server, Runtime Manager and clients receive no Docker socket/API proxy
- read scope cannot mutate
- unknown operation/field/engine fails closed
- two engines on one host remain distinct Connections and cannot cross scopes
- deadline, cancellation and output-size limits pass against a real Docker host

### DKR-002 — Implement stable Docker inventory and ownership mapping

Depends on: DKR-001, PKG-005.

Deliver:

- hosts, engines, projects, services, containers, images, volumes and networks
- stable project/service/resource identities
- JulOS installation ownership-label map with owned/adopted/external classification
- bounded observation persistence and stale/offline behavior

Acceptance:

- container recreation does not duplicate a service/app
- ephemeral container IDs/IPs never become durable identity
- unrelated resources are visible only under granted inventory scope and never become owned
- real Compose fixture verifies project/service/volume/network mapping

### APP-002 — Implement connection delivery and external app registration

Depends on: APP-001, CONN-001.

Deliver:

- `connection` delivery Preview/Apply
- selection and validation of an existing `ready` Connection through provider capability
- Application launch target and optional Web rendering policy
- external ownership and removal behavior

Acceptance:

- apply deploys no container
- connection secrets remain Secret Bindings
- Store setup can create a separate draft Connection, but App Preview remains mutation-free
- App removal leaves the referenced standalone Connection; Connection deletion is a separate confirmation
- Home Assistant connection fixture opens through an approved target

### DKR-007 — Implement single-image app deployment

Depends on: APP-001, API-011, DKR-002, PKG-009.

Deliver:

- `docker.apps/1` preflight/apply/inspect typed contract
- deterministic one-service Compose normalization
- image tag resolution and Deployment Lock digest
- parameter/Secret Binding, ownership labels, health verification and operation progress

Acceptance:

- wrong architecture, port/mount/name conflict and forbidden rights fail before apply
- success is recorded only after inspect verifies desired resources and health
- retry is exact and cannot create a second installation
- Connector mutates only matching ownership labels

### DKR-008 — Implement supported Compose preview and deployment

Depends on: CAT-001, DKR-007.

Deliver:

- parser/normalizer for the exact supported subset in `APPLICATION_CATALOG.md`
- multi-service, named-volume and network preflight/apply/inspect
- critical-right classification and host-policy denial
- deployment lock retaining normalized plan and image digests

Acceptance:

- `build`, escaping includes/files and unbound host environment are rejected
- unsupported keys are never ignored
- privileged/device/host namespace/socket/bind, every `cap_add`, `seccomp=unconfined` and `apparmor=unconfined` right requires exact acknowledgement and host-policy evaluation
- real multi-service Compose, health, volume, network and recreation tests pass

### APP-006 — Implement explicit Docker adoption

Depends on: APP-001, DKR-002, DKR-008.

Deliver:

- adoption Preview enumerating existing resources, conflicts, ownership and allowed actions
- explicit administrator approval and ownership-label transition where safe
- adopted data/resource retention defaults

Acceptance:

- discovery never adopts automatically
- resource already owned by another installation cannot be adopted
- shared/external resources remain non-owned
- removal of an adopted app retains data unless a separate verified ownership action permits deletion

### DKR-003 — Implement Docker application UI

Depends on: DKR-002, PKG-010, MOB-006.

Deliver resource navigation, status, logs, actions, app-installation links and Surface lifecycle support.

Acceptance:

- write actions are backend permission-controlled and confirmed
- error cause remains visible
- suspended UI stops log polling without losing server Operations
- stable installation/service identity is visible instead of container ID

### DKR-004 — Implement Docker application discovery

Depends on: DKR-002, CORE-003.

Deliver label, manual, Compose, Caddy, port and heuristic evidence pipeline.

Acceptance:

- proposals require approval
- ignored proposals remain ignored
- proposals offer Connect or Adopt explicitly and never imply management
- a workload named Hermes follows the same generic evidence path as any other workload

### DKR-005 — Implement Docker problems

Depends on: DKR-002, CORE-007.

Deliver unhealthy, restart-loop, stopped, unreachable, mount and resource conditions.

Acceptance:

- repeated observations deduplicate
- resolved conditions close correctly
- last-known observations are never presented as live

### DKR-006 — Implement explicit audited container terminal

Depends on: DKR-001, DKR-002, REM-009, HCON-005, API-004, API-009.

Deliver:

- `docker.container.terminal/1` for one current container resolved from stable installation/service identity
- permission `docker.container.terminal`, explicit confirmation, fixed shell profiles and bounded session
- Remote terminal presentation and target-bound stream
- audit open/connect/disconnect/expiry/failure without input/output

Acceptance:

- wrong Connector/engine/installation/service scope fails closed
- container recreation resolves the current container immediately before exec
- close, disconnect, idle and maximum timeout terminate and verify the exec process
- no Host Connector/Server/Runtime Manager host shell exists

### CAT-003 — Implement Store, source management and App Builder

Depends on: CAT-002, APP-001, PKG-014, DKR-008, MOB-006.

Deliver:

- one Store presenting delivery kind and technical trust boundary clearly
- source add/edit/remove/refresh and stale-cache UI
- My Apps local catalog and Builder import/edit/validate/test/export
- install Preview showing source/signature, target, rights, conflicts and data effects
- English/German and Phone/Tablet/Desktop Surface behavior

Acceptance:

- Connection/Image/Compose/Native flows are distinguishable before apply
- one warning allows authorized unsigned install; invalid signature remains blocked
- Builder export/import produces the same definition digest
- custom source failure leaves last valid content visibly stale

### APP-004 — Implement managed-app backup and restore

Depends on: APP-001, DKR-008, API-011.

Deliver:

- verified archive containing normalized definition/lock, image digests, non-secret config, Secret Reference IDs, ownership map and declared data
- stop-consistent first implementation and checksum manifest
- restore preflight, staging/apply and health verification
- retained backups independent from Installation removal

Acceptance:

- no secret value enters archive or logs
- checksum failure mutates nothing
- real named-volume backup restores on a clean target
- failed restore is observable and retains the prior verified backup

### APP-003 — Implement app update policy and diff

Depends on: CAT-002, APP-004, DKR-008, PKG-014.

Deliver:

- off/notify/automatic policy
- source/trust/definition/image/rights/data diff and Preview
- required pre-update backup and explicit safe rollback eligibility
- desired/install lock update only after health verification

Acceptance:

- automatic update pauses on source/key/trust or critical-right change
- failed health never advances installed lock
- data survives update and verified backup can restore
- no silent downgrade/fallback occurs

### APP-005 — Implement safe managed-app uninstall

Depends on: APP-004, DKR-008.

Deliver:

- Uninstall Preview classifying owned/adopted/external/shared resources
- retain data, backup then remove owned data, and permanent owned-data removal choices
- image cleanup candidates separate from data ownership

Acceptance:

- external/shared/adopted resources and retained data are not deleted
- selected backup is verified before destructive removal
- backups survive uninstall
- retry is idempotent and reports partial external failure explicitly

### REL-CAT-001 — Publish official catalog and application template

Depends on: CAT-003, APP-002 through APP-006, DKR-007, DKR-008, PKG-014.

Deliver:

- stable built-in source ID and migration from current embedded official package catalog
- public catalog/template repositories and validator usage guide
- immutable release/signature/index generation
- Home Assistant connection/Compose reference fixtures and generic workload terminal evidence

Acceptance:

- old `/api/v1/packages/catalog` and new catalog are not retained as indefinite parallel authorities
- existing installed native packages remain Package Installations without fake App Installation backfill
- official packages remain signed under publisher `juloc-official`
- fresh install and supported upgrade produce identical source/package visibility

### PVE-001 — Implement Proxmox connection validation

Depends on: CONN-001, API-008, PKG-005.

Deliver a versioned `proxmox-api` Connection provider with API token authentication, endpoint validation, optional Host Connector routing and TLS policy.

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

### FILE-003 — Implement Host Connector-local provider

Depends on: FILE-001, HCON-004.

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

Depends on: HCON-004, CORE-003.

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

### HCON-006 — Remove the non-executing legacy Agent tombstone

Depends on: HCON-003 and at least one published transition release whose notes announced the removal.

Deliver:

- delete the legacy `/api/v1/agent/*` 426 route and its compatibility-only middleware/fixtures
- retain historical migrations, audit names and `legacy_agent_commands` retention data only
- update upgrade diagnostics to report an obsolete Agent binary through current Host Connector/version health rather than a live legacy endpoint

Acceptance:

- no runtime route or executable accepts the legacy protocol
- supported previous-release upgrade follows the announced Host Connector path
- historical audit/export remains readable
- full validation and deployed upgrade smoke pass before 1.0 release

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
- Host Connector, Docker, Proxmox, Browser and Remote setup succeeds

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
area:host-connector
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
