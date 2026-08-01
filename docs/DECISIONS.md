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
