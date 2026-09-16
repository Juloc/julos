# Packages

## 1. Purpose

Packages add optional JulOS capabilities without increasing Core coupling. Official packages live in this monorepo until the package SDK, manifest and release process are stable.

Publisher signatures are recommended but not an installation gate. Every artifact requires an integrity digest. Unsigned or unknown-publisher artifacts require a clear administrator warning; an artifact with a claimed but invalid signature is rejected. Unknown native frontend code runs only through the isolated frontend boundary from `APPLICATION_CATALOG.md`.

This document defines JulOS **extension packages**. User-facing catalog applications—existing connections, Docker images and Compose stacks—use `APPLICATION_CATALOG.md`. The Store can show both, but persistence, runtime ownership and trust presentation remain distinct.

## 2. Package identity

Package IDs are stable reverse-domain identifiers:

```text
de.juloc.julos.browser
de.juloc.julos.remote
de.juloc.julos.docker
de.juloc.julos.proxmox
de.juloc.julos.files
de.juloc.julos.caddy
de.juloc.julos.discovery
```

Display names use the `JulOS.<Name>` convention in code and releases.

Package identity never changes when a display name, repository or publisher website changes.

## 3. Package artifact

A package release is represented by an immutable OCI artifact or equivalent bundle containing a descriptor and references to immutable assets. A publisher signature is optional metadata over the complete artifact.

Logical contents:

```text
package/
├─ manifest.json
├─ manifest.signature                 optional
├─ frontend/
│  ├─ application modules
│  ├─ widget modules
│  ├─ design assets
│  └─ localization bundles
├─ migrations/
├─ schemas/
│  ├─ settings schemas
│  └─ contract schemas
└─ runtime references
   ├─ worker image digest
   └─ optional helper image digests
```

Large container images are referenced by immutable digest rather than duplicated inside the artifact.

## 4. Manifest contract

Initial semantic shape:

```json
{
  "schemaVersion": 1,
  "id": "de.juloc.julos.docker",
  "name": "Docker",
  "publisher": "Juloc",
  "version": "1.0.0",
  "minimumCoreVersion": "1.0.0",
  "maximumCoreVersion": null,
  "worker": {
    "image": "ghcr.io/juloc/julos-docker@sha256:...",
    "cpuLimit": 1.0,
    "memoryLimitMb": 256
  },
  "capabilitiesProvided": [
    { "name": "docker.inventory", "version": 1 },
    { "name": "docker.control", "version": 1 }
  ],
  "capabilitiesRequired": [
    { "name": "host.connector.connection", "version": 1, "optional": false }
  ],
  "permissions": [
    "docker.read",
    "docker.control"
  ],
  "applications": [],
  "widgets": [],
  "settings": [],
  "migrations": [],
  "runtimeProfiles": [],
  "dependencies": []
}
```

The final JSON schema is committed before implementation. Unknown required fields, unsupported schema versions, integrity mismatches, claimed-but-invalid signatures and incompatible Core versions fail installation clearly. Missing/unknown signatures produce the documented trust warning.

## 5. Package component types

A package may contribute:

- package worker
- native desktop application
- widget
- settings section
- capability provider
- problem detector
- discovery source
- background operation handler
- package-owned schema migrations
- optional runtime profiles and image references
- localization resources

A package does not need every component type.

## 6. Lifecycle

```text
Available
  ↓
Installing → Installed → Configuring → Disabled
                                ↓
                             Starting
                                ↓
                              Enabled
                                ↓
                             Stopping
                                ↓
                              Disabled

Any active transition may enter Faulted.
Installed packages may enter Updating or Removing.
```

Required lifecycle behavior:

1. verify artifact digest, evaluate publisher/signature state and require acknowledgement when needed
2. validate manifest and Core compatibility
3. display permissions, dependencies and runtime requirements
4. create package record and storage
5. apply package-owned migrations
6. start worker in validation mode
7. validate configuration
8. enable only after health and registrations succeed
9. expose health, logs and diagnostics
10. disable without deleting package data
11. remove runtime resources only after explicit confirmation
12. preserve rollback artifact until update confirmation

A package that is installed but not configured remains disabled.

## 7. Package states

### Available

Known to a configured package source but not installed.

### Installed

Artifact and storage exist. Worker is not active.

### Configuring

Administrator is providing or validating required settings.

### Disabled

Installed and intentionally inactive. Configuration remains.

### Enabled

Worker is ready and registrations are active.

### Faulted

Worker, migration, configuration or dependency state prevents normal function. Core remains available.

### Updating

New artifact is being verified, migrated and activated.

### Removing

Runtime is stopped and selected package resources are being removed.

## 8. Package boundaries

A package may use:

- public versioned Core contracts
- Package SDK abstractions
- its own PostgreSQL schema and restricted role
- Core package-settings service
- capability requests through the broker
- public integration APIs of external products
- declared runtime volumes and networks
- operation-specific secret leases

A package may not use:

- another package's internal classes
- another package's database tables or credentials
- private or undocumented external endpoints
- arbitrary Server filesystem access
- unrestricted shell execution
- raw container-runtime APIs
- frontend-only authorization
- raw user authentication tokens
- secret values in logs or events

## 9. Package worker contract

The Server supervises each backend package worker as a child process and exchanges a bounded newline-delimited JSON protocol over its standard input/output, not a private HTTP endpoint and not gRPC, for:

```text
liveness
readiness
configuration validation
start
stop
registration inventory
capability execution
health diagnostics
```

Worker calls have deadlines and cancellation. A failed or timed-out worker call returns a typed package error.

The supervised local `process` worker path is restricted to administrator-trusted signed extensions. An unsigned or unknown-publisher backend worker requires the resource-limited Runtime Manager worker-container profile from `PKG-013`, or the extension remains disabled. Warning acknowledgement alone never grants execution under the Server operating-system identity.

Workers register:

- applications
- widgets
- capabilities
- settings schemas
- problem detectors
- package version and health

Registrations are removed when the package is disabled or worker connection expires.

## 10. Package frontend contract

Frontend modules are integrity-checked. A trusted publisher may run under the existing trusted frontend policy; unsigned or unknown-publisher native code requires the isolated package-origin/message bridge before execution.

Each application or widget declares:

- stable key
- module asset
- custom element name
- localization bundle
- supported theme and viewport behavior
- surface contract version and supported background modes
- API contract version
- required permissions
- window or widget size constraints

Package UI uses Shadow DOM to prevent style leakage. The host provides theme tokens, localization, typed API access, navigation and event subscriptions.

The host does not provide secrets, raw tokens or unrestricted global state.

### Isolated frontends

Untrusted frontend code does not run in the Shell realm. It runs in a frame sandboxed with `allow-scripts` and deliberately **without** `allow-same-origin`, which gives it an opaque origin: no JulOS session cookie, no Shell DOM, no same-origin fetch against Core and no access to anything the Shell holds. The two sandbox tokens are never combined, because a frame granted both can remove its own sandbox.

The frame is loaded from a generated `srcdoc`, so it has no URL of its own on the JulOS origin, and it carries a content policy of `default-src 'none'` with `connect-src 'none'` — it cannot open connections of its own at all. Only the *origin* of the verified module is placed in that policy, never the path: a path is attacker-shaped input, and spaces or quotes in one would otherwise add source expressions and widen exactly the thing the policy narrows.

Everything the frame may do goes through one typed message bridge. Authorization itself is enforced by Server: the capability broker checks the invoking package against its declared capabilities and answers `403 package.capability_not_granted`. The bridge check in the Shell is defence in depth in front of that, not a substitute for it, and only the frame the Shell created may speak through it.

### Isolated workers

A `process` worker runs as Server itself: same user, same filesystem, same network. That is acceptable for a package whose publisher this installation trusts and is not acceptable for one it does not, and no sandbox exists on that path to make it acceptable. Enabling an untrusted package that declares a `process` runtime therefore fails with `package.worker_isolation_required`.

An untrusted package consequently cannot run any backend worker today: `process` is refused for the reason above, and a `container` runtime already fails with `package.container_runtime_not_configured` because the Runtime Manager transport is not yet connected to the package worker path. The resource-limited container profile for untrusted workers belongs with that transport and is not claimed here.

The trusted path is unchanged and uses the same application model: only where a frontend is mounted differs, never what an application is.
 Every message is validated against the package manifest before the Shell acts on it, and anything not explicitly recognised and granted is refused: a frontend cannot widen what its package may reach by asking, because the Shell checks the manifest and not the message. This is not the default application runtime, which `AGENTS.md` forbids; it is the path a package takes precisely because it is not trusted.


An installation records how much is known about who produced it: `trusted-signed`, `unknown-signed` or `unsigned`. That is trust, not integrity — every installed artifact was verified byte for byte against its recorded digest regardless. Anything that is not `trusted-signed` runs on the isolated path introduced by `PKG-013`. The check is written as "trusted is the exception" rather than as a list of untrusted states, so a state added later is isolated by default instead of silently inheriting full access.

Shadow DOM is not a security sandbox. The isolated frontend bridge exposes only versioned typed messages, no Shell DOM, JulOS cookies or arbitrary Core endpoints. Mobile-capable applications implement activate, deactivate, suspend, resume, optional Back and dispose semantics from `MOBILE_PWA.md`.

The schema adds one exact case-sensitive object to each mobile-capable `Applications[]` entry, validated by the `package-manifests` stage since `MOB-006`:

```json
"Surface": {
  "ContractVersion": "1.0.0",
  "SupportedBackgroundModes": ["suspend", "keep-surface-active"],
  "HandlesBack": true
}
```

An application that lists the `mobile` viewport without declaring `Surface` fails manifest validation, and the element the manifest names must implement all six lifecycle methods or the Shell refuses to drive it with `package.surface_contract_unsupported`. Declaring the contract and not implementing it is therefore not a state a package can ship in.

`suspend` is required for every application that lists the `mobile` viewport. `keep-surface-active` is optional capability declaration, never permission for the package to select the user's preference. Unknown fields/major versions fail manifest validation. Exact async methods, deadlines, reasons and state transitions are owned by `MOBILE_PWA.md`.

## 11. Package storage

### Small settings

Use Core package-settings service when the package requires only a limited versioned configuration document.

### Relational operational state

Use one package-owned PostgreSQL schema with a restricted role.

Rules:

- migrations belong to the package artifact
- migration state is recorded per package version
- migration failure prevents activation
- irreversible migration is declared before update
- package removal asks whether data should be retained or deleted
- Core does not query package schema

### Runtime storage

Browser profiles, transfer staging and other runtime volumes are declared separately from database state. Temporary runtime data has explicit cleanup policy.

## 12. Dependencies and capabilities

### Required dependency

Used only when package installation cannot function without another package identity. Prefer required capability over package identity when replacement providers are valid.

### Optional dependency

Enables an additional feature but does not block package operation.

### Required capability

The package can be installed but cannot enable until a compatible provider is available, unless the capability is target-specific and configuration can remain incomplete.

### Optional capability

The package enables related UI only when a provider is available.

Example:

```text
Proxmox requires no Remote package identity.
Proxmox optionally requests remote.console/1.
When no provider exists, inventory works and console action is unavailable.
```

## 13. Permissions

Permissions are declared in the manifest and granted through Core roles and scopes.

Examples:

```text
system.metrics.read
docker.read
docker.control
proxmox.read
proxmox.vm.control
remote.connect
remote.clipboard
remote.file-transfer
files.read
files.write
files.delete
caddy.read
network.discovery
network.discovery.approve
```

Packages cannot create undeclared permission names at runtime.

## 14. Runtime profiles

A runtime profile declares a controlled template for Runtime Manager.

Example fields:

```text
profile key
approved image digest
command identifier from image metadata
CPU and memory limits
process limit
allowed network profile kinds
allowed volumes
allowed environment keys
health probe
cleanup policy
```

The manifest cannot request privileged mode, arbitrary host mounts or unrestricted network configuration.

## 15. Package updates

Update flow:

1. download immutable artifact
2. verify digest and evaluate optional publisher signature
3. validate compatibility and permissions changes
4. show release and migration notes
5. drain or cancel package operations according to policy
6. stop old worker
7. apply migration
8. start new worker
9. validate health and registrations
10. mark update successful
11. remove old artifact after retention period

If activation fails:

- keep package fault diagnostics
- attempt rollback only when schema and manifest declare it safe
- otherwise remain disabled or faulted with restore instructions

There is no silent fallback to the old version.

## 16. Package categories

- Integration package: Docker, Proxmox, Caddy
- Application package: Browser, Files
- Capability package: Remote
- Discovery package: network and service detection
- Provider extension: future file or DNS provider where the owning package supports extensions
- Widget-only package: only when no application or backend worker is needed

## 17. Initial official packages

### Browser

- starts isolated Chromium runtimes
- supports persistent, temporary and fixed-application modes
- accesses local DNS and private addresses through configured network profiles
- uses Remote transport for display and input
- hands downloads to Files when available
- enforces session, resource and cleanup limits

### Remote

- extracts reusable Julgate code without copying product UI
- provides RDP, VNC, SSH and console capabilities
- owns connection lifecycle, input, clipboard and reconnect behavior
- integrates file redirection through Files capabilities
- preserves Julgate until functional parity is verified

### Docker

- connects through Host Connector capability
- installs catalog-selected single-image and standard Compose applications on an explicitly selected host
- inventories hosts, Compose projects, services and containers
- provides health, logs and controlled lifecycle actions
- identifies applications using stable host/project/service identities
- proposes discovered applications for approval
- detects unhealthy, restart-loop, stopped and unreachable resources

### Proxmox

- reads clusters, nodes, VMs, LXCs, storage, tasks, backups and snapshots
- provides explicitly enabled control actions
- requests console sessions through Remote
- never stores a competing copy of Proxmox configuration

### Files

- provides Host Connector-local, SMB, SFTP and WebDAV providers
- uses one file-operation contract across providers
- provides upload, download, copy, move, rename, delete and preview
- requires explicit confirmation and permission for destructive operations

### Caddy

- reads stable Caddy UI integration APIs
- shows status, route summary, certificate problems and reload errors
- opens Caddy UI through Browser or configured launch action
- works without Docker package
- never reads Caddy UI database tables directly

### Discovery

- combines Host Connector-visible ARP, ICMP, mDNS, SSDP and optional SNMP sources
- records devices and services as observations and proposals
- requires approval before management
- preserves ignored state without repeated alerts

## 18. Package source strategy

Initial source is the official JulOS release registry under the Juloc GitHub packages namespace or another configured immutable OCI registry.

The Package Manager must support:

- source URL
- trusted publisher keys
- authentication secret reference
- refresh
- source health

Official extension releases remain signed. Administrator-selected custom sources and publishers are allowed under the same digest, warning and native-isolation rules. Application catalogs use the broader source model in `APPLICATION_CATALOG.md`.

## 19. Repository strategy

Current repository:

```text
Juloc/julos
```

Create `Juloc/julos-package-template` only after Package SDK, manifest, worker contract and release pipeline are stable. Do not create separate official package repositories during initial implementation.

## 20. Package definition of done

An official package is complete only when it includes:

- valid manifest, immutable digest and explicit signature state
- configuration validation
- permissions and capability declarations
- health and diagnostics
- worker isolation
- application and widget registrations where required
- surface lifecycle tests for every mobile-capable application
- localization
- migration tests
- timeout and fault tests
- security review
- operations documentation
- package-specific README
- update and removal behavior
