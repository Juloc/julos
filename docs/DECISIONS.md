# Architecture decisions

Accepted decisions are recorded here until individual ADR files become necessary. Update an existing decision instead of adding contradictory guidance elsewhere.

## D001 — Documentation-first initialization

**Status:** Accepted

Production code starts after product scope, architecture, package boundaries, implementation order and contribution rules are committed.

Reason: JulOS combines desktop, package, Agent and remote-session concerns. Undefined boundaries would create early coupling that is expensive to remove.

## D002 — Initial monorepo

**Status:** Accepted

Core, Desktop, Agent, Package SDK, official packages and runtime images live in `Juloc/julos` initially.

Reason: contracts and packages will evolve together before 1.0. Separate repositories would add versioning, CI and dependency overhead without providing real isolation.

Create `Juloc/julos-package-template` only after the SDK and manifest are stable.

## D003 — Small product-independent core

**Status:** Accepted

The core owns platform concepts only. Docker, Proxmox, Caddy, remote protocols, files and discovery exist only in packages and Agents.

Reason: a deployment must remain lightweight and functional when optional packages are absent or faulty.

## D004 — Capability-based package collaboration

**Status:** Accepted

Packages communicate through versioned capabilities brokered by Core. Direct package references and cross-package database reads are forbidden.

Reason: providers can be replaced and packages can be enabled independently without a dependency graph hidden in implementation code.

## D005 — Real browser runtime, not iframe integration

**Status:** Accepted

Internal websites open in a real isolated Chromium runtime connected to a JulOS window through remote-session transport.

Reason: this supports local addresses, multiple tabs, downloads, certificates, logins, browser tools and sites that prohibit framing. It also avoids exposing internal management services publicly.

Qualified by `D035`: for internal **web applications**, local transparent-proxy rendering in an iframe is the default and this streamed real-browser runtime is the fallback (isolation, targets that cannot be proxied, and RDP/VNC/SSH). D005 remains authoritative for the streamed runtime itself.

## D006 — Julgate extraction instead of duplication

**Status:** Accepted

Reusable Julgate session, streaming and protocol code is extracted into shared JulOS Remote components. Julgate remains operational until documented parity is reached.

Reason: copying code would create two diverging implementations. Archiving Julgate before parity would remove a working product.

This migration path is a controlled product transition, not a permanent runtime fallback.

## D007 — External products remain authoritative

**Status:** Accepted

Caddy UI, Proxmox, Docker and other systems remain the source of truth for their domains. JulOS stores connections, presentation state, approvals, observations and derived problems.

Reason: duplicating domain state creates synchronization bugs and conflicting management paths.

## D008 — Caddy as a small integration package

**Status:** Accepted

Caddy UI exposes stable authenticated integration endpoints. JulOS.Caddy consumes them for status, widgets, problems and launch actions.

Reason: JulOS should not rebuild Caddy UI or couple to its database. The package must also work when Caddy UI is not hosted by the Docker package.

## D009 — One shared Agent binary

**Status:** Accepted

JulOS uses one small Agent with explicitly enabled capabilities rather than one Agent per package.

Reason: multiple Agents would duplicate enrollment, updates, security, networking and host metrics. The shared Agent still prevents arbitrary command execution through a strict capability allowlist.

## D010 — Docker Compose is the first deployment target

**Status:** Accepted

JulOS 1.0 targets a Docker Compose control-plane deployment with PostgreSQL and optional runtime containers.

Reason: this matches the intended homelab environment and keeps installation understandable. The internal architecture must not depend on every optional container being present.

## D011 — No workarounds or silent fallback paths

**Status:** Accepted

The project does not accept hidden temporary branches, duplicated implementations, broad exception suppression or success responses that conceal degraded behavior.

Reason: operational software must make failures visible and actionable. Correctly blocked work is safer than a misleading partial implementation.

## D012 — Repository encoding policy

**Status:** Accepted

General repository text uses UTF-8 with BOM and CRLF. Unix-executed scripts and formats that require LF use UTF-8 and LF through explicit file-pattern overrides.

Reason: this preserves the established Juloc repository convention without breaking Linux runtime files.

## D013 — Native TypeScript modules and Web Components

**Status:** Accepted

The initial Desktop uses TypeScript, native ES modules, Custom Elements and Shadow DOM rather than a general SPA framework.

Reason: window movement, package module loading and desktop state need precise browser-side behavior but do not require a large application framework. Native boundaries keep startup and package integration lightweight.

A framework can be introduced only through a later accepted decision based on measured implementation needs.

## D014 — Package backend workers run out of process

**Status:** Accepted

Enabled package backend logic runs in independent worker processes or containers. JulOS Server coordinates packages but does not load package implementation assemblies into the core process.

Reason: a faulty package must not terminate the control plane. Independent workers also create enforceable resource, storage and API boundaries.

## D015 — Narrow Runtime Manager owns local runtime-container control

**Status:** Accepted

JulOS Server does not receive the raw container runtime socket. A dedicated Runtime Manager sidecar manages only JulOS-owned package and session runtime resources through an allowlisted API.

Reason: the control plane needs to start Browser, Remote and package workers even when the Docker management package is not installed, while avoiding unrestricted Docker access in Server.

Runtime Manager is control-plane infrastructure, not the JulOS Docker package.

## D016 — Trusted signed packages only for 1.0

**Status:** Accepted

JulOS 1.0 installs only official or administrator-trusted signed packages. Package artifacts, frontend modules and runtime images are verified by identity, digest and signature.

Reason: package JavaScript and backend workers have meaningful access to the JulOS environment. A public untrusted marketplace requires stronger sandboxing and is outside 1.0.

## D017 — Package-owned database schemas

**Status:** Accepted

A package that requires relational storage receives its own PostgreSQL schema and restricted database role. Core and other packages do not query that schema.

Reason: packages need durable operational state without creating cross-package coupling or granting broad database access.

Small package settings may use the core package-settings service instead of a dedicated schema.

## D018 — Window state and session state are separate

**Status:** Accepted

Desktop windows persist presentation state. Browser, RDP, VNC and SSH sessions persist runtime state through separate session references and policies.

Reason: closing or reloading a window must not accidentally terminate a remote task, and a restored window must not imply that an expired session is active.

## D019 — English source, English default UI, German included

**Status:** Accepted

Code, contracts and repository documentation use English. User-facing text is localizable, English is the default language and German is supported from the first user-facing milestone.

Reason: consistent source language simplifies maintenance while supporting the intended users.

## D020 — Versioned artifacts instead of unpinned latest deployments

**Status:** Accepted

Core images, runtime images and packages are published with immutable versioned tags or digests. Deployment does not depend on an unpinned `latest` tag.

Reason: package compatibility, migrations, rollback constraints and diagnostics require an exact deployed version.

## D021 — Trunk-based delivery on `main`

**Status:** Accepted

One completed work item becomes one commit on `main`. Branches and pull requests are used only for large, risky or externally reviewed changes.

Reason: the repository currently has a single maintainer. A mandatory branch and pull request per work item adds process latency without adding a real reviewer. The per-change content requirements — tests, documentation, backlog and validation — are unchanged and remain the actual quality gate.

This decision is revisited when more than one person contributes regularly.

## D022 — Repository-wide .NET build conventions

**Status:** Accepted

The .NET build uses the XML solution format `JulOS.slnx`, central package management through `Directory.Packages.props`, shared compiler settings through `Directory.Build.props`, and the Microsoft.Testing.Platform mode of `dotnet test` selected in `global.json`.

Warnings are errors in every project and documentation generation is enabled, so an undocumented public API or an unused symbol fails the build rather than accumulating.

Reason: one declared version per dependency and one declared setting per rule prevent per-project drift, which is the usual source of "works in one project" build differences. The Microsoft.Testing.Platform mode is required because the VSTest mode of `dotnet test` no longer supports these test projects on the .NET 10 SDK.

## D023 — Test projects are created with their first real test

**Status:** Accepted

A test project is added when the code it validates exists. The foundation milestone therefore creates only `tests/JulOS.Architecture.Tests`, because repository structure and dependency direction are the only things that exist and can be asserted. `JulOS.Domain.Tests` and `JulOS.Application.Tests` are created by the first `CORE` work item that adds behavior.

Reason: an empty test project reports a passing test run without validating anything, which is the misleading-success pattern that `D011` forbids.

## D024 — Shared domain primitives

**Status:** Accepted

The domain shares four primitives, and nothing else:

- `TimeProvider` from the base class library is the clock port. JulOS defines no clock interface of its own.
- `IIdentifierGenerator` produces identifiers, implemented as time-ordered version 7 GUIDs derived from the injected `TimeProvider`.
- Each entity declares its own identifier type, for example `public readonly record struct AgentId(Guid Value)`, validated through `EntityIdentifier.Validated`.
- `Revision` carries optimistic concurrency, starting at 1 and never wrapping.

A refused domain operation throws `DomainRuleViolationException` with a stable code.

Reason: `TimeProvider` is the platform's clock abstraction and already has a maintained test double, so a custom interface would add a second concept without adding capability. Version 7 identifiers keep the primary key index compact because they sort by creation time, which random identifiers do not. Per-entity identifier types make passing the wrong identifier a compile error rather than a runtime lookup miss. Throwing rather than returning a failure value prevents a caller from ignoring a refusal and continuing into an invalid state.

## D025 — Infrastructure adapters have their own unit test project

**Status:** Accepted

`tests/JulOS.Infrastructure.Tests` holds unit tests for control-plane adapters that need no external dependency. `tests/JulOS.Integration.Tests` remains for tests that run against a real PostgreSQL instance or another live dependency.

Reason: an adapter such as the identifier generator is pure logic and must not require a database container to run. Putting it in the integration project would make the fast test set depend on infrastructure it does not need.

## D026 — Persistence rows stay outside Domain

**Status:** Accepted

Entity Framework Core maps relational storage rows in Infrastructure rather than materializing the Domain aggregates directly. Each row exposes only storage data and a one-way `FromDomain` conversion where a Domain aggregate already exists. It contains no lifecycle rule, authorization decision or alternate state transition.

Reason: some Domain types deliberately require services such as `TimeProvider` or enforce creation through named operations. Adding persistence-only constructors, mutable setters or EF annotations would weaken those invariants and make Domain depend on PostgreSQL tooling. Separate rows keep the Domain persistence-neutral while database constraints provide an independent final enforcement layer.

## D027 — Local authentication uses Identity cookies and a default-deny fallback

**Status:** Accepted

Local accounts and roles use ASP.NET Core Identity stores in the existing `core` schema. Browser sessions use one HTTP-only, same-site-strict Identity cookie, and the antiforgery cookie shares the same policy. Both mark themselves secure exactly when the inbound request is HTTPS (`CookieSecurePolicy.SameAsRequest`) rather than unconditionally, because the documented single-container alpha deployment binds Kestrel to plain loopback HTTP by design and ASP.NET Core's antiforgery middleware raises a hard `InvalidOperationException` on every request when `SecurePolicy` is `Always` and the request is not HTTPS. Server installs an authenticated-user fallback policy, and only setup, login, authentication status and health probes may opt out explicitly.

Reason: Identity supplies reviewed password hashing, lockout, security stamps and cookie integration without placing credential behavior in Domain. A fallback policy makes a newly added endpoint private unless its owner consciously declares otherwise. Roles are stored now because the first administrator needs a stable system role, but permission mapping and role administration stay in `API-004` so authentication does not invent authorization behavior. An unconditional `Always` policy was accepted originally but broke every antiforgery-protected mutation on the documented deployment, discovered only through a real deployed check because integration tests exercise the in-memory `WebApplicationFactory` client over an `https://` base address regardless of the deployment's real scheme; a deployment that terminates TLS in front of JulOS still gets a secure cookie under `SameAsRequest`, and a loopback HTTP evaluation deployment now works instead of failing on every mutation.

## D028 — Authorization has no role-name bypass

**Status:** Accepted

Backend policies resolve direct user grants and grants inherited from current ASP.NET Core Identity roles, then call the pure Core permission evaluator for the requested permission and scope. The built-in administrator role is immutable and receives explicit global assignments; its name never short-circuits a policy.

Reason: a role-name superuser branch would create a second authorization system outside the Domain model, hide missing assignments and make scoped permissions impossible to reason about. Explicit persisted grants keep policy behavior visible, testable and migratable. Existing administrators are backfilled during the `API-004` migration so an upgrade cannot lock out the operator.


## D029 — External AES-GCM secret key ring

**Status:** Accepted

Core secret references use AES-256-GCM with a random 96-bit nonce and a 128-bit authentication tag. The reference identifier, owning scope and purpose are authenticated associated data. PostgreSQL stores ciphertext and the non-secret key identifier; 32-byte encryption keys are Base64 files in an external deployment-owned key-ring directory. One configured active key encrypts new values, while retained keys decrypt existing rows.

The control plane never returns a stored value through HTTP. Decryption occurs only through the operation-scoped Application lease after the durable operation is verified as running, non-cancelling and owned by the matching Core or package scope. Lease buffers are zeroed on expiry or disposal. Deletion destroys all protected-value columns and retains a revisioned metadata tombstone plus a sanitized audit event.

Reason: authenticated encryption protects confidentiality and detects record substitution without inventing a custom cryptographic construction. Keeping key files outside PostgreSQL means a database backup alone cannot decrypt credentials. A small explicit key ring supports controlled encryption-key rotation without an indefinite dual storage path or a dependency on an external secret product for the first supported deployment.


## D030 — Remote session provisioning owns a durable Operation to lease target credentials

**Status:** Accepted

`ISecretLeaseService.AcquireAsync` (D029) only releases a decrypted secret value to a durable, running, non-cancelling Operation owned by the matching Core or package scope. Before this decision, `PostgresRemoteSessionProvisioner` validated a Remote session's `SecretReferenceId` metadata (presence, scope, purpose) but never leased the value, so the target's RDP/VNC/SSH credential never reached the provider runtime; `ISecretLeaseService` had no caller anywhere in the repository.

`PostgresRemoteSessionProvisioner.ProvisionAsync` now creates (or idempotently reuses) an `Operation` of type `remote.session.credential`, scoped to the session's `OwnerUserId` and `SourcePackageId = callerPackageId`, keyed by the session ID. It drives that operation `Queued → Running`, leases the secret, copies the lease value, and marks the operation `Succeeded`. The decrypted bytes are Base64-encoded into one opaque `JULOS_REMOTE_TARGET_CREDENTIAL` runtime secret-environment entry alongside the existing callback token; Core does not parse or interpret its contents.

The byte content is a provider-boundary convention, not a Core contract: a UTF-8 JSON object with optional `username`, `password`, `domain`, `privateKey` and `passphrase` fields, matching the subset of `JulOS.Remote.Transport.GuacamoleLaunchRequest` fields that are secret. The calling package chooses which fields to populate for its protocol; the Remote provider runtime is the only component that parses this JSON.

The same investigation found a second gap blocking any real provider from reporting its own `connected` event: `PostgresRemoteSessionConnectionService.ConnectAsync` requires an exact `row.Revision` match, but no runtime environment variable ever carried that number. `PostgresRemoteSessionProvisioner` now also injects the non-secret `JULOS_REMOTE_EXPECTED_REVISION`, computed as the session's revision immediately after the pending `Connecting` transition (`row.Revision + 1` at request-build time, since that transition is always the very next write the provisioner makes once runtime allocation succeeds).


## D031 — Remote provider runtime bundles unmodified guacd and the official Guacamole web application

**Status:** Accepted

`docs/BACKLOG.md` listed "exact provider runtime composition" as an open product decision that did not block implementation. `packages/JulOS.Remote/runtime/Dockerfile` resolves it: one container image, launched per Remote session, bundles `guacd` and the Guacamole web application together with a new minimal translator, `JulOS.Remote.ProviderBridge`.

guacd is compiled from the official Apache `guacamole-server-1.6.0` source release rather than copied from the upstream `guacamole/guacd` image, because that image is Alpine (musl) based while the Guacamole web application's own official image is Ubuntu (glibc) based; the two cannot be combined by copying compiled binaries across incompatible C libraries. Building guacd from source against the exact same Ubuntu 24.04 base as the final image keeps every shared library version aligned. The web application itself is copied verbatim from the official `guacamole/guacamole` image (`/opt/guacamole`, including its own `entrypoint.d` scripts and bundled `guacamole-auth-json` extension) and runs through its own unmodified startup scripts.

`JulOS.Remote.ProviderBridge` (`packages/JulOS.Remote/runtime/JulOS.Remote.ProviderBridge`) contains no new transport or protocol logic. It is a thin translator, built on the already-published `JulOS.Remote.Transport` library's `GuacamoleJsonLaunchEncoder` (D006, REM-003), between the generic `JULOS_REMOTE_*` runtime environment contract and one Guacamole JSON-auth token. It generates a random JSON-auth secret key local to its own container's lifetime — never shared with JulOS.Server, since one runtime always serves exactly one session against exactly one target and no other party ever needs to construct a valid token for it. A minimal nginx reverse proxy injects that pre-computed token as a fixed upstream query string in front of the web application's WebSocket tunnel, because `RemoteDisplayGateway.ProviderEndpoint` resolves one fixed template with no room for a per-session query string by design (JulOS.Server proxies the connection itself and never exposes the provider endpoint to the browser).

Reason: this keeps 100% of the actual RDP/VNC/SSH protocol and Guacamole wire-protocol implementation on unmodified upstream artifacts (source-built guacd, the official web application, the already-extracted shared transport library) and confines all new code to environment-contract translation and process supervision — consistent with the standing rule against building a new Remote transport implementation. Building guacd from source, rather than treating it as unavailable, was chosen over standing up a second Alpine-based container (Runtime Manager launches exactly one image per runtime) or accepting an unverified cross-libc binary copy.


## D032 — Runtime Manager identifies a runtime's image and secret-like environment names by label, not by `docker` output text

**Status:** Accepted

Deploying the Remote provider runtime end to end (D031) through a real Runtime Manager found two defects that a real digest-pinned deployment could never avoid, because they are structural to how Docker reports state, not incidental to this one image:

`HttpRemoteRuntimeManager.VerifyIdentity` compares the `Image` a caller requested against the `Image` Runtime Manager reports back after creating the container. `DockerCliRuntimeBackend.ReadAsync` sourced that value from `docker container ls --format {{.Image}}`. For any container created from a digest reference (`repository@sha256:...`) — the only form `ConfiguredRemoteRuntimePolicy` accepts — Docker always reports a bare short image ID through this column, on locally built and registry-pulled images alike (verified directly with both). The identity check therefore rejected every digest-pinned runtime unconditionally. `DockerCliRuntimeBackend` now stores the exact requested image string as a `com.juloc.julos.image` label at creation and reads it back from that label, the same pattern already used for the package, version and instance identity.

Separately, `RuntimePolicy.LooksLikeSecretName` gates which runtime environment entries are permitted to travel through the secret channel by checking for `PASSWORD`, `SECRET`, `TOKEN` and `PRIVATE_KEY` substrings. It did not recognize `CREDENTIAL`, so the `JULOS_REMOTE_TARGET_CREDENTIAL` entry introduced by D030 failed Runtime Manager's own validation before a container could ever be created. `CREDENTIAL` is now included.

Both were found by running a real Remote session through a real JulOS Server, a real Runtime Manager container with a Docker-socket mount, and the real provider runtime image against a real SSH target — no test at any layer exercised an actual `docker container ls` invocation or the exact `JULOS_REMOTE_TARGET_CREDENTIAL` name before this.

Reason: matching Docker's actual reporting behavior for digest references, rather than assuming it echoes back whatever reference string was supplied, is a factual correction, not a design choice — every alternative (accepting the short ID as the identity, or forbidding digest references) would either weaken the identity check's purpose or contradict the mandatory digest-pinning policy. Labels are already the established mechanism for every other piece of identity Runtime Manager must recover after the fact.

Reason: the lease-gated decryption path already existed and is the only sanctioned way to read a secret's value (D029); wiring Remote session provisioning to use it (rather than inventing a second credential-delivery path) keeps exactly one decryption boundary in Core. Modeling credential acquisition as its own Operation keeps the existing idempotency, cancellation and audit semantics that `IOperationService` already provides, instead of adding bespoke state to `RemoteSessionRow`. Keeping the byte layout a provider-boundary convention (not a Core contract) preserves the existing protocol-neutral boundary: Core forwards opaque bytes exactly as it already does for the Browser display credential and the Remote callback token.

## D033 — SQLite is the default core store; PostgreSQL is opt-in

**Status:** Accepted

The core store supports both SQLite and PostgreSQL through one `CoreDbContext`. For a single-host homelab — the primary 1.0 target — SQLite in one file is the default: no second container, no database password and no database network port. PostgreSQL is opt-in for larger or multi-instance deployments.

Provider selection in `CoreDatabaseConfiguration.Read`:

- an explicit `Database:Provider` of `sqlite` or `postgresql`/`postgres` is always honoured;
- with no provider set, a configured `ConnectionStrings:CoreDatabase` selects PostgreSQL, so an existing deployment that only sets a connection string keeps its behaviour unchanged;
- with no provider and no connection string, the store defaults to SQLite at `/var/lib/julos/julos.db`.

PostgreSQL remains required whenever more than one Server instance shares the core store, and stays the recommended choice for larger deployments, because SQLite supports exactly one writer on one host.

Reason: the earlier implicit default was PostgreSQL, which forced every evaluation and small single-owner deployment to run and secure a separate database server for no benefit. Defaulting to SQLite only when nothing at all is configured makes "just run the container" work out of the box, while leaving every deployment that already pins a provider or a connection string exactly as it was.

## D034 — Package workers run as supervised stdio child processes, not network services

**Status:** Accepted

Earlier architecture text described package workers as network-isolated services reachable over private HTTP or gRPC endpoints and fronted by Runtime Manager. The implemented and accepted mechanism is different: `ProcessPackageWorkerSupervisor` launches each signed package worker as a child process and speaks one bounded newline-delimited JSON protocol over its standard input and output (validate, configure, register, start, stop, health and typed command). Runtime Manager owns session and helper *runtimes* (Browser, Remote provider); it does not currently front the package workers themselves.

Reason: a supervised child process with a stdio contract is the smallest mechanism that satisfies the actual requirements — out-of-process fault isolation, a typed bounded request/response protocol with deadlines, and explicit package database-provider identity handoff — without a per-worker network listener, port allocation, service discovery or TLS between Server and worker on the same host. It follows the "add abstraction only for a proven repeated need" principle.

Upgrade trigger: move a specific package worker behind a network boundary (its own container/network namespace, authenticated HTTP/gRPC, Runtime-Manager-managed) when that worker must run on a different host than Server, or when its blast radius — for example one reaching an external Docker or Proxmox API — justifies kernel-level network isolation. The change is made per worker, recorded here, and reflected in `ARCHITECTURE.md`, `TECHNICAL_SPECIFICATION.md` and `PACKAGES.md` at the same time.

Reason: the prior HTTP/gRPC descriptions asserted an isolation boundary that is not built, which misleads security reasoning exactly where it will matter most. Recording the real transport and its explicit upgrade trigger keeps the documentation honest without paying for network isolation before a worker needs it.

## D035 — Hybrid web-application rendering: local transparent proxy by default, streamed browser as fallback

**Status:** Accepted

An internal web application opens by default in local mode: JulOS reverse-proxies the target transparently and the user's own browser renders it in a desktop-window iframe, so video and interactive content decode and run locally with hardware acceleration. The isolated streamed browser runtime (`D005`) is retained as the fallback for targets that cannot be proxied transparently, for isolation, and for RDP, VNC and SSH.

Local mode serves each target at its own `<slug>.<julos-domain>` host (wildcard DNS and TLS through the Caddy integration) and does not rewrite application URLs, because single-page applications with absolute root paths and root WebSocket endpoints break under a shared path prefix. The proxy only strips framing headers, keeps the application's cookies first-party inside the iframe, passes WebSockets through, reaches targets through the Agent tunnel, and injects target credentials on the server side through a secret lease so nothing secret reaches the client.

This qualifies `D005`: an iframe is used, but only for a JulOS-controlled host whose framing headers JulOS itself sets and which is never publicly exposed, so `D005`'s reasons — foreign origins forbidding framing, and exposing internal services — do not apply. Framing a foreign origin directly remains forbidden.

Reason: pixel-streaming a remote browser is the wrong transport for media and interactive local content and has a heavy per-window footprint, so it should be the exception, not the default. Transparent per-host proxying renders locally with hardware acceleration and, unlike path-based proxying, serves real single-page control panels without fragile URL rewriting. The full plan is `docs/WEB-APP-RENDERING.md`.

**Prerequisite:** wildcard DNS and a wildcard TLS certificate for the deployment domain. Without them only streamed mode is offered; path-based proxying is not adopted as a substitute.
