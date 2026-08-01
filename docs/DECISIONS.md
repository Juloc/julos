# Architecture decisions

Accepted decisions are recorded here until individual ADR files become necessary. Update an existing decision instead of adding contradictory guidance elsewhere.

## D001 — Documentation-first initialization

**Status:** Accepted

Production code starts after product scope, architecture, package boundaries, implementation order and contribution rules are committed.

Reason:

JulOS combines desktop, package, agent and remote-session concerns. Undefined boundaries would create early coupling that is expensive to remove.

## D002 — Initial monorepo

**Status:** Accepted

Core, Desktop, Agent, Package SDK, official packages and runtime images live in `Juloc/julos` initially.

Reason:

Contracts and packages will evolve together before 1.0. Separate repositories would add versioning, CI and dependency overhead without providing real isolation.

A `Juloc/julos-package-template` repository is created only after the SDK and manifest are stable.

## D003 — Small product-independent core

**Status:** Accepted

The core owns platform concepts only. Docker, Proxmox, Caddy, remote protocols, files and discovery exist only in packages and agents.

Reason:

A deployment must remain lightweight and functional when optional packages are absent or faulty.

## D004 — Capability-based package collaboration

**Status:** Accepted

Packages communicate through versioned capabilities brokered by the core. Direct package references and cross-package database reads are forbidden.

Reason:

Providers can be replaced and packages can be enabled independently without a dependency graph hidden in implementation code.

## D005 — Real browser runtime, not iframe integration

**Status:** Accepted

Internal websites open in a real isolated browser runtime connected to a JulOS window through remote-session transport.

Reason:

This supports local addresses, multiple tabs, downloads, certificates, logins, browser tools and sites that prohibit framing. It also avoids exposing internal management services publicly.

## D006 — Julgate extraction instead of duplication

**Status:** Accepted

Reusable Julgate session, streaming and protocol code is extracted into JulOS Remote packages. Julgate remains operational until documented parity is reached.

Reason:

Copying the code would create two diverging implementations. Archiving Julgate before parity would remove a working fallback product.

This migration path is not a runtime workaround; it is a controlled product transition with one eventual implementation.

## D007 — External products remain authoritative

**Status:** Accepted

Caddy UI, Proxmox, Docker and other systems remain the source of truth for their domains. JulOS stores connections, presentation state, approvals and derived problems.

Reason:

Duplicating domain state creates synchronization bugs and conflicting management paths.

## D008 — Caddy as a small integration package

**Status:** Accepted

Caddy UI exposes stable authenticated integration endpoints. JulOS.Caddy consumes them for status, widgets, problems and deep links.

Reason:

JulOS should not rebuild Caddy UI or couple to its database. The package must also work when Caddy UI is not hosted by the Docker package.

## D009 — One shared agent binary

**Status:** Accepted

JulOS uses one small agent with explicitly enabled capabilities rather than one agent per package.

Reason:

Multiple agents would duplicate enrollment, updates, security, networking and host metrics. The shared agent still prevents arbitrary command execution through a strict capability allowlist.

## D010 — Docker Compose is the first deployment target

**Status:** Accepted

JulOS 1.0 targets a Docker Compose control-plane deployment with PostgreSQL and optional runtime containers.

Reason:

This matches the intended homelab environment and keeps installation understandable. The internal architecture must not depend on every optional container being present.

## D011 — No workarounds or silent fallback paths

**Status:** Accepted

The project does not accept hidden temporary branches, duplicated implementations, broad exception suppression or success responses that conceal degraded behavior.

Reason:

Operational software must make failures visible and actionable. Correctly blocked work is safer than a misleading partial implementation.

## D012 — Repository encoding policy

**Status:** Accepted

General repository text uses UTF-8 with BOM and CRLF. Unix-executed scripts and formats that require LF use UTF-8 and LF through explicit file-pattern overrides.

Reason:

This preserves the established Juloc repository convention without breaking Linux runtime files.
