# Packages

## Purpose

Packages add optional JulOS capabilities without increasing core coupling. Official packages live in this monorepo until the package SDK and release process are stable.

## Package identity

Package IDs are stable reverse-domain identifiers:

```text
de.juloc.julos.browser
de.juloc.julos.docker
de.juloc.julos.proxmox
de.juloc.julos.remote
de.juloc.julos.files
de.juloc.julos.caddy
de.juloc.julos.discovery
```

Display names use the `JulOS.<Name>` convention in code and releases.

## Manifest contract

Initial manifest shape:

```json
{
  "id": "de.juloc.julos.docker",
  "name": "Docker",
  "version": "1.0.0",
  "minimumCoreVersion": "1.0.0",
  "entrypoint": "JulOS.Packages.Docker",
  "capabilitiesProvided": ["docker.read", "docker.control"],
  "capabilitiesRequired": ["agent.connection"],
  "permissions": ["docker.read"],
  "applications": [],
  "widgets": [],
  "dependencies": []
}
```

The final schema is versioned. Unknown required fields or incompatible schema versions fail installation clearly.

## Lifecycle

```text
Available → Installed → Configured → Enabled → Disabled → Removed
                         └─────────────→ Faulted
```

Required lifecycle behavior:

- validate signature and manifest
- validate core compatibility
- resolve required capabilities and dependencies
- apply package-owned migrations transactionally
- start package services independently
- expose health and diagnostics
- disable without damaging package data
- remove runtime resources only after explicit confirmation
- support rollback when an update changes runtime or schema state

## Package boundaries

A package may use:

- public core contracts
- Package SDK abstractions
- its own schema and configuration
- capability requests through the broker
- public integration APIs of external products

A package may not use:

- another package's internal classes
- another package's database tables
- private or undocumented external endpoints
- arbitrary server filesystem access
- unrestricted shell execution
- frontend-only authorization

## Package categories

- Integration package: Docker, Proxmox, Caddy
- Application package: Browser, Files, Terminal
- Capability package: Remote
- Provider package: SMB, SFTP, DNS providers
- Discovery package: network and service detection
- Widget-only package: small derived status views, only when a full package is unnecessary

## Capability examples

```text
system.metrics.read
docker.read
docker.control
proxmox.read
proxmox.vm.control
remote.rdp
remote.vnc
remote.ssh
remote.console
files.read
files.write
caddy.summary.read
network.discovery
```

Capabilities are versioned contracts. Permissions decide who can request them. Providers decide whether the target connection supports them.

## Initial packages

### Browser

- starts isolated Chromium runtimes
- supports persistent and temporary profiles
- supports full-browser and fixed-app modes
- accesses local DNS and private addresses from the configured network
- uses Remote transport for display and input
- hands downloads to Files when available
- enforces session and resource limits

### Remote

- extracted from Julgate without copying product UI
- provides RDP, VNC, SSH and console session capabilities
- owns connection lifecycle, input, clipboard and reconnect behavior
- integrates file redirection through Files capabilities
- preserves Julgate until functional parity is verified

### Docker

- discovers hosts, Compose projects, services and containers
- provides health, logs and controlled lifecycle actions
- identifies applications using stable host/project/service identities
- proposes discovered applications for approval
- detects unhealthy, restarting, stopped and unreachable resources

### Proxmox

- reads nodes, VMs, LXCs, storage, tasks and backups
- provides explicitly enabled control actions
- requests console sessions through Remote
- never stores a competing copy of Proxmox configuration

### Files

- provides local agent, SMB, SFTP and WebDAV providers
- uses one file-operation contract across providers
- provides upload, download, copy, move, rename, delete and preview
- requires explicit confirmation for destructive operations

### Caddy

- remains a small integration package
- reads stable Caddy UI integration APIs
- shows status, route summary, certificate problems and reload errors
- opens Caddy UI for detailed management
- never reads Caddy UI database tables directly

### Discovery

- combines agent-visible ARP, ICMP, mDNS, SSDP and optional SNMP sources
- records discovered devices as proposals
- requires approval before management
- supports ignored devices without repeated alerts

## Package repository strategy

Current repository:

```text
Juloc/julos
```

Create `Juloc/julos-package-template` only after the package SDK and manifest are stable. Do not create separate package repositories during the initial implementation.
