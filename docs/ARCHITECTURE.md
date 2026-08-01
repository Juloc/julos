# Architecture

## System overview

```text
Client browser
  └─ JulOS Desktop
       ├─ Window manager
       ├─ App launcher and widgets
       └─ Session views
            │
JulOS Server
  ├─ Authentication and authorization
  ├─ Package registry and lifecycle
  ├─ App, window and layout state
  ├─ Capability broker
  ├─ Events, notifications and problems
  └─ Audit log and secret references
       │
JulOS Agent and package runtimes
  ├─ Host metrics
  ├─ Docker and Proxmox adapters
  ├─ Browser runtime
  ├─ Remote runtime
  ├─ File providers
  └─ External product integrations
```

## Dependency direction

Dependencies point inward:

```text
Package implementations
        ↓
Package SDK and versioned contracts
        ↓
Core application services
        ↓
Core domain model
```

The core domain must not reference package implementations, infrastructure products or protocol-specific types.

## Core responsibilities

The core owns only cross-product platform behavior:

- users, roles and sessions
- installed package records and package lifecycle
- capabilities and permissions
- applications and launch definitions
- windows, layouts and desktop settings
- notifications, problems and audit entries
- agent identity and connectivity
- encrypted secret references

The core does not implement Docker, Proxmox, Caddy, RDP, VNC, SSH, SMB, SFTP, WebDAV or network-discovery logic.

## Desktop responsibilities

The desktop is a lightweight client shell. It owns presentation state but not authorization decisions.

- window placement, size, focus and snapping
- taskbar and launcher behavior
- widget placement
- viewport-specific layout restoration
- rendering registered package applications
- reconnect and offline presentation

Backend APIs remain authoritative for permissions, session creation and infrastructure actions.

## Package runtime

A package can contribute:

- applications
- widgets
- background services
- capability providers
- settings pages
- problem detectors
- API endpoints behind the package boundary
- database migrations for its own schema
- optional runtime containers

Packages must be independently startable and stoppable. A package crash is reported as a package problem and must not terminate the core.

## Capability broker

Packages request capabilities through the core broker instead of referencing providers directly.

Example:

```text
Proxmox package requests remote.console
Core resolves an enabled provider
Remote package creates the session
Proxmox receives a versioned session reference
```

Capability requests include the user, resource, requested operation and permission context. The backend validates every request.

## Agent model

A small JulOS Agent runs where local access is required. One agent binary exposes only explicitly enabled capabilities.

Initial agent capabilities:

- `system.metrics`
- `system.storage`
- `system.network`
- `docker.read`
- `docker.control`
- `files.local`
- `network.discovery`

The agent uses outbound authenticated communication to JulOS Server. It does not expose a general remote shell and does not accept arbitrary commands.

## Data ownership

- Core database: users, layouts, package installations, permissions, applications, problems and audit metadata.
- Package schema: package configuration and package-owned operational state.
- External system: authoritative domain state and history.
- Browser runtime storage: isolated user profiles and temporary session data.
- File providers: file content remains in the configured source.

Cross-package database reads are forbidden.

## Real browser and remote sessions

JulOS applications are not generally embedded through iframes.

The Browser package starts a real isolated browser runtime inside the configured network. The Remote package provides the reusable session transport and input path extracted from Julgate. Browser, RDP, VNC and SSH sessions are represented by core session references and rendered in JulOS windows.

Window state and runtime session state are separate. Closing a window can either disconnect, suspend or terminate a session according to the application policy.

## Integration products

Large products expose stable integration APIs. JulOS packages consume those APIs and provide summaries, widgets, problems and deep links.

The Caddy package must not read the Caddy UI database directly. Caddy UI exposes versioned integration endpoints for status, routes, certificates and problems.

## Failure behavior

- unavailable external systems show explicit offline states
- last-known values are labeled with their observation time
- reconnects do not require a full page reload
- failures never become empty success responses
- package failures are isolated and observable
- startup has a safe mode that disables optional packages

## Security boundaries

- TLS for all server-agent and runtime communication
- encrypted secrets at rest
- short-lived session credentials
- backend-enforced capability permissions
- confirmation for destructive actions
- audit entries for configuration and infrastructure mutations
- resource limits for browser and remote runtimes
- no direct public exposure of Docker sockets or internal management APIs

## Deployment model

The first supported deployment is Docker Compose with PostgreSQL and optional package runtime containers. JulOS Server is the control plane. Agents connect Proxmox nodes, Docker hosts and selected VMs.

The architecture must not require all packages or runtime containers to be installed.
