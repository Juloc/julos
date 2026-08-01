# Security and operations

## 1. Security objective

JulOS provides access to private infrastructure and therefore has a high-impact trust position. Security controls are implemented from the first relevant milestone and validated before feature completion.

## 2. Trust boundaries

```text
Public or remote client
  │ HTTPS
JulOS Server
  ├─ PostgreSQL
  ├─ Runtime Manager
  ├─ package workers
  └─ outbound-connected Agents
       └─ local infrastructure and services
```

Separate trust boundaries exist between:

- browser client and Server
- Server and package worker
- Server and Runtime Manager
- Server and Agent
- package worker and external system
- remote runtime and target system
- browser runtime and target network

Authentication at one boundary does not automatically authorize another boundary.

## 3. Authentication

### 3.1 Local authentication

Initial deployment supports local accounts with:

- secure password hashing through ASP.NET Core Identity
- secure, HTTP-only and same-site cookies
- configurable session timeout
- login rate limiting
- account lockout with safe recovery
- forced initial administrator creation during setup

### 3.2 OIDC

OIDC is added through a provider-neutral configuration after local authentication is stable. External identity maps to a JulOS user and roles. Provider-specific logic does not enter Domain.

### 3.3 Re-authentication

Sensitive operations may require recent authentication:

- deleting package data
- revealing or replacing sensitive connection configuration
- changing authentication providers
- revoking all sessions
- restoring backups
- changing package signing trust

## 4. Authorization

Permissions use explicit strings and optional scopes.

Examples:

```text
core.settings.read
core.settings.write
packages.read
packages.install
packages.configure
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
discovery.read
discovery.approve
```

Rules:

- every API mutation has a backend policy
- package workers receive only the user and scope claims required for one operation
- read and control permissions are separate
- destructive actions use narrower permissions where necessary
- a package cannot grant permissions to itself
- denied actions create security diagnostics but not normal audit-success events

## 5. Secrets

Secrets include passwords, API tokens, client certificates, private keys and recovery materials.

Requirements:

- encrypted at rest
- encryption keys stored separately from normal database backups
- secret values never returned after creation
- frontend receives only presence, purpose and rotation metadata
- logs and audit records contain no secret values
- package workers receive short-lived operation-specific credential leases
- secret rotation does not require recreating unrelated configuration
- deletion is explicit and auditable

Development examples use placeholders only.

## 6. Agents

### 6.1 Enrollment

- one-time token
- short expiration
- single successful use
- administrator-visible requested identity
- durable Agent identity after enrollment

### 6.2 Connection

- outbound connection from Agent
- mutual authentication
- certificate or credential rotation
- protocol version negotiation
- heartbeat and revocation checks

### 6.3 Command restrictions

Agent has no general shell endpoint. Every command is a typed capability with:

- allowlisted operation
- validated arguments
- target scope
- deadline
- correlation ID
- result limit

File operations normalize paths and enforce configured roots. Docker operations use configured engine scope. Discovery obeys configured network ranges and rate limits.

## 7. Package security

### 7.1 Trust model for 1.0

JulOS 1.0 accepts only official or administrator-trusted signed packages.

Installation verifies:

- package identity
- publisher
- manifest schema
- artifact digest
- signature chain
- core compatibility
- declared permissions
- declared runtime resources

### 7.2 Isolation

- backend package workers run out of process
- each package has private storage credentials
- frontend modules use Custom Elements and Shadow DOM
- package APIs are versioned and authenticated
- package resource limits are enforced
- a faulted package can be disabled from safe mode

The 1.0 frontend trust model does not claim hostile JavaScript sandboxing. Public third-party marketplace support requires a separate isolation design.

## 8. Runtime Manager security

Runtime Manager has the highest local container-runtime privilege and therefore exposes a deliberately narrow API.

Allowed operations:

- create JulOS-labeled runtime container from approved image digest
- attach approved JulOS network
- attach approved profile or staging volume
- set allowlisted environment keys
- apply CPU, memory, process and inactivity limits
- inspect status of JulOS-owned runtime resources
- stop and remove JulOS-owned runtime resources

Forbidden:

- arbitrary image or command execution
- host-path mounts outside explicit allowlist
- privileged containers
- host network by default
- management of unrelated containers
- arbitrary Docker API proxying

Server validates package policy before calling Runtime Manager. Runtime Manager independently validates ownership and allowlists.

## 9. Browser runtime security

Browser runtime intentionally accesses private networks. Each configured network profile defines:

- allowed networks or attached Docker networks
- DNS servers
- optional denied destinations
- internet access policy
- profile persistence policy
- download and upload policy
- clipboard policy
- maximum session duration
- inactivity timeout

Runtime uses an unprivileged user, read-only base filesystem where possible and isolated profile storage.

Temporary sessions delete profile and staging data after termination. Cleanup failure creates a critical runtime problem.

## 10. Remote session security

- credentials are leased for session startup and not sent to the browser
- signaling tokens are short-lived and scoped to one session
- session transport uses TLS
- clipboard directions can be disabled separately
- upload, download and drive mapping are separate permissions
- session recording is disabled by default and outside 1.0 unless explicitly designed
- inactivity and maximum-duration policies are configurable
- active sessions are visible and terminable by authorized users

## 11. Web application security

Required controls:

- HTTPS in production
- HSTS where deployment topology permits
- Content Security Policy
- anti-forgery protection for cookie-authenticated mutations
- secure cookie settings
- rate limits for authentication, package actions and expensive searches
- output encoding
- validated redirects and deep links
- no credentials in query strings
- correlation IDs without sensitive data
- dependency and container image scanning

## 12. Audit logging

Audit events are required for:

- login security changes
- user and role changes
- package install, update, enable, disable and removal
- Agent enrollment, rename and revocation
- secret creation, rotation and deletion metadata
- infrastructure mutations
- file writes and deletions where configured
- remote session creation and termination
- backup and restore

Audit logging records outcome and target, not secret payloads or file content.

## 13. Logging and diagnostics

All services emit structured logs with:

- timestamp UTC
- severity
- service and package identity
- event code
- correlation ID
- safe target identity
- message

Rules:

- normal external-system failures are not logged repeatedly at multiple layers
- expected authorization denial is not a server error
- stack traces remain server-side
- health failures have stable diagnostic codes
- package and Agent versions appear in diagnostics

## 14. Health model

Every long-running service exposes:

- liveness: process can respond
- readiness: service can perform its supported role
- dependency diagnostics: detailed authorized view

Core readiness does not require optional packages. A package worker has its own readiness state.

## 15. Deployment topology

### 15.1 Required Compose services

```text
julos-server
julos-postgres
julos-runtime-manager
```

### 15.2 Optional services

```text
julos-remote-runtime or guacd
julos-browser-runtime pool
package workers
reverse proxy when not supplied externally
```

Agents are deployed on target hosts and connect outbound.

### 15.3 Networks

- public edge network: reverse proxy to Server only
- control network: Server, PostgreSQL and Runtime Manager
- package network: package workers with only required Server access
- runtime networks: explicit network profiles for Browser and Remote targets

PostgreSQL, Runtime Manager, package control endpoints and Agent control endpoints are never published publicly.

## 16. Configuration

Configuration priority:

1. built-in safe defaults
2. configuration files or environment variables
3. database-backed administrator settings
4. package-specific settings

Secrets are referenced, not embedded, in ordinary configuration.

Missing required configuration fails startup or package enablement with an actionable error. There is no silent insecure fallback.

## 17. Backup scope

A complete control-plane backup includes:

- PostgreSQL database
- encryption key ring and documented restore secret
- package artifact metadata and package-owned schemas
- package configuration
- user layouts
- Browser persistent profiles when enabled
- required uploaded icons or assets

Runtime containers, temporary profiles, caches and active sessions are recreated and are not backup targets.

## 18. Backup process

Initial supported process:

1. enter backup-consistent mode or use database-consistent snapshot procedure
2. create PostgreSQL logical backup
3. archive key ring and required persistent volumes
4. record JulOS version and installed package versions
5. encrypt backup at rest
6. verify archive readability
7. publish backup result as operation and problem on failure

## 19. Restore process

1. deploy compatible JulOS version
2. stop normal Server processing
3. restore PostgreSQL and required persistent volumes
4. restore encryption key material
5. start in safe mode with optional packages disabled
6. validate core migration state
7. enable packages one at a time and run compatibility checks
8. verify Agents, secrets, layouts and package health
9. exit safe mode

A release cannot be marked stable until a clean restore test succeeds.

## 20. Updates

### 20.1 Core update

- pull versioned images, never an unpinned `latest` deployment reference
- verify release metadata
- create backup checkpoint
- apply database migrations
- start Server in compatibility mode for installed packages
- verify readiness
- roll back image only when the schema and documented downgrade policy permit it

### 20.2 Package update

- verify signature and digest
- validate core and capability compatibility
- stop old worker after draining operations
- apply package migration
- start new worker
- validate registrations and readiness
- retain old artifact until update is confirmed

Rollback is not promised when a package migration declares itself irreversible. The UI must state this before update.

## 21. Safe mode

Safe mode starts:

- authentication
- core APIs
- Package Manager
- settings
- diagnostics
- backup and restore

Optional package workers and runtime sessions remain disabled. Safe mode can be activated through configuration or after repeated package-start failure. It must not require editing database rows manually.

## 22. Retention and cleanup

Configurable retention applies to:

- notifications
- resolved problems
- audit events
- operation logs
- package logs
- temporary downloads
- terminated session metadata

Cleanup is observable and never deletes active resources. Persistent browser profiles and user files are not treated as cache.

## 23. Operational runbooks required before 1.0

- fresh installation
- first administrator setup
- Agent enrollment and revocation
- package installation and repair
- Browser runtime failure
- Remote runtime failure
- PostgreSQL backup and restore
- lost Agent
- full disk
- broken package migration
- safe-mode recovery
- key rotation
- JulOS update

Each runbook contains symptoms, checks, safe actions, expected result and rollback limits.
