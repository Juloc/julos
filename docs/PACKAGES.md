# Packages

## 1. Purpose

Packages add optional JulOS capabilities without increasing Core coupling. Official packages live in this monorepo until the package SDK, manifest and release process are stable.

JulOS 1.0 installs only official or administrator-trusted signed packages.

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

A package release is represented by a signed OCI artifact or equivalent immutable bundle containing a descriptor and references to immutable assets.

Logical contents:

```text
package/
├─ manifest.json
├─ manifest.signature
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
    { "name": "agent.connection", "version": 1, "optional": false }
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

The final JSON schema is committed before implementation. Unknown required fields, unsupported schema versions, invalid signatures and incompatible Core versions fail installation clearly.

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

1. verify publisher, signature and artifact digest
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

Every backend package worker exposes authenticated private control endpoints or equivalent gRPC methods for:

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

Workers register:

- applications
- widgets
- capabilities
- settings schemas
- problem detectors
- package version and health

Registrations are removed when the package is disabled or worker connection expires.

## 10. Package frontend contract

Frontend modules are signed and integrity-checked.

Each application or widget declares:

- stable key
- module asset
- custom element name
- localization bundle
- supported theme and viewport behavior
- API contract version
- required permissions
- window or widget size constraints

Package UI uses Shadow DOM to prevent style leakage. The host provides theme tokens, localization, typed API access, navigation and event subscriptions.

The host does not provide secrets, raw tokens or unrestricted global state.

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
2. verify signature and digest
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

- connects through Agent capability
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

- provides Agent-local, SMB, SFTP and WebDAV providers
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

- combines Agent-visible ARP, ICMP, mDNS, SSDP and optional SNMP sources
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

A public catalog and third-party publisher onboarding are outside 1.0.

## 19. Repository strategy

Current repository:

```text
Juloc/julos
```

Create `Juloc/julos-package-template` only after Package SDK, manifest, worker contract and release pipeline are stable. Do not create separate official package repositories during initial implementation.

## 20. Package definition of done

An official package is complete only when it includes:

- valid signed manifest
- configuration validation
- permissions and capability declarations
- health and diagnostics
- worker isolation
- application and widget registrations where required
- localization
- migration tests
- timeout and fault tests
- security review
- operations documentation
- package-specific README
- update and removal behavior
