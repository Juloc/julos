# Complete JulOS concept

## 1. Product definition

JulOS is a lightweight browser-based desktop environment for homelabs. It replaces a collection of browser tabs, remote-access tools and status dashboards with one workspace that behaves like a simple desktop operating environment.

JulOS is not a replacement for Proxmox, Docker, Caddy UI or other specialized products. It provides a common desktop, application registry, session layer, status surface and package system. Detailed domain management remains in the specialized product or its dedicated JulOS package.

Initial deployment domain: `os.juloc.de`.

## 2. Main user problem

A homelab user currently needs many separate tools:

- Proxmox for nodes, VMs, LXCs, storage, backups and console access
- Docker management for stacks, containers, images, volumes and logs
- Caddy UI for routes, certificates and access rules
- Julgate or other gateways for RDP, SSH and VNC
- file tools for SMB, SFTP, WebDAV and local files
- many open browser tabs for internal web applications
- separate dashboards for CPU, memory, disk and service problems

JulOS combines access and context without duplicating every management feature.

## 3. Product principles

1. **Small core:** the core contains only platform behavior shared by all deployments.
2. **Installable capabilities:** Docker, Proxmox, Remote, Files, Browser, Caddy and Discovery are packages.
3. **No iframe dependency:** full websites use a real browser runtime inside the target network.
4. **One desktop:** applications and sessions open as movable, resizable and snap-enabled windows.
5. **Existing products stay authoritative:** JulOS stores integration state and presentation state, not competing copies of domain configuration.
6. **Observable failure:** offline, stale and failed states are explicit.
7. **Secure by default:** write access, file transfer, clipboard and destructive actions are permission-controlled.
8. **No workarounds:** blocked architecture work remains blocked until the correct design exists.
9. **Simple implementation:** add abstraction only for proven repeated needs.
10. **Documentation is implementation:** every behavior and contract change updates the documentation.

## 4. Target users

### 4.1 Primary user

A technical homelab owner operating one or more Proxmox nodes, Docker hosts, VMs, containers and private web applications.

### 4.2 Secondary users

- family members who need a limited app launcher
- administrators with read-only infrastructure access
- package developers after the SDK is stable

JulOS 1.0 is optimized for a single-owner homelab but must not hard-code single-user assumptions into permissions or data models.

## 5. Core user journeys

### 5.1 Open the homelab workspace

1. User signs in.
2. JulOS resolves the current client device, workspace class and shared or device-scoped layout.
3. Widgets show current or clearly marked last-known metrics.
4. The taskbar restores active applications and reconnectable sessions.
5. Current critical problems appear without blocking desktop use.

### 5.2 Open an internal website while away from home

1. User opens Browser or a discovered web application.
2. JulOS requests a browser session using the configured network profile.
3. Browser package starts an isolated Chromium runtime inside that network.
4. Remote package transports display, mouse, keyboard and clipboard according to permissions.
5. The browser appears in a normal JulOS window.
6. Closing the window applies the configured disconnect, suspend or terminate policy.

The internal website is not exposed publicly and is not embedded in an iframe.

### 5.3 Inspect a Docker problem

1. Docker package receives inventory and health observations through a Host Connector.
2. A stable problem identity is created or updated.
3. The desktop problem widget shows the issue.
4. Opening the problem launches the Docker application at the affected service.
5. Read-only users see diagnostics only.
6. Authorized users may restart the service after confirmation and audit logging.

### 5.4 Use several applications at once

1. User opens Proxmox, Caddy status, Browser and Files.
2. Each application receives an independent window.
3. User snaps windows left, right or into quarters.
4. Layout is stored with optimistic concurrency.
5. Reload restores windows without confusing a window record with a live remote session.

### 5.5 Install a feature package

1. User opens Package Manager.
2. JulOS downloads the extension artifact, verifies integrity and evaluates publisher trust.
3. Required permissions, runtime resources, signature status and dependencies are shown; an unsigned or unknown publisher produces a warning rather than a hidden rejection.
4. Installation creates package records, storage and optional runtime resources.
5. Configuration is completed before enablement.
6. Enabled applications, widgets and capabilities appear dynamically.
7. A faulted package is isolated and can be disabled without preventing core startup.

## 6. Desktop model

The desktop consists of:

- desktop surface
- taskbar
- launcher and searchable command palette
- notification and problem area
- window manager
- widget layer
- package manager
- settings
- session manager

### 6.1 Window behavior

Every window supports the behavior appropriate for its application policy:

- focus and z-order
- move and resize
- minimize and restore
- maximize and restore
- close
- snap left, right and into four quarters
- keyboard movement and resizing
- touch-safe controls
- full-screen mode for remote applications
- single-instance or multi-instance behavior

### 6.2 Workspace behavior

- Desktop single-display: free windows, snapping and simultaneous applications.
- Desktop multi-display: a separate persisted layout with stable logical display slots.
- Tablet: the same desktop model with maximized/split defaults, larger controls and free placement where space and input allow it.
- Phone: one primary full-screen application by default and an explicitly activated split with at most two visible applications.

Layouts are shared per workspace class by default and may be overridden for one registered client device. Phone use cannot overwrite tablet or desktop placement. The complete resolution and persistence contract is `MOBILE_PWA.md`.

### 6.3 Session behavior

A window is presentation state. A session is runtime state.

A frontend Surface has a third, separate execution state: foreground-focused, foreground-visible, background-active, suspended, faulted or terminated. Suspending a Surface stops frontend work but does not imply that its Window is closed or its runtime Session is terminated.

A closed window may:

- disconnect and preserve the session
- suspend the session
- terminate the session

The application definition chooses a default. The user sees the consequence before destructive termination.

## 7. Package model

The JulOS core has no Docker, Proxmox, Caddy, RDP, VNC, SSH, SMB or discovery implementation.

Packages may contribute:

- desktop applications
- widgets
- settings sections
- background workers
- capability providers
- problem detectors
- API endpoints behind a package boundary
- package-owned migrations and storage
- optional isolated runtime containers

Official packages are developed in the monorepo until the contracts are stable.

### 7.1 Application catalog

JulOS extension packages and user applications are separate technical models shown in one Store. A catalog application may connect an existing service, install one Docker image, install a standard Compose stack or reference a native JulOS extension. Standard Compose remains authoritative and JulOS metadata is limited to `x-julos`.

Official, community, Git, HTTPS, OCI and local catalogs are supported. Unsigned application definitions remain installable after a clear administrator warning; invalid claimed signatures are rejected as corrupted. Exact definitions and resolved images are locked by digest. `APPLICATION_CATALOG.md` is the complete contract.

### 7.2 Initial official packages

#### Browser

Provides real Chromium sessions with:

- persistent profiles
- temporary profiles
- full-browser mode
- fixed-application mode
- multiple tabs
- downloads and uploads
- internal DNS and private network access
- configurable clipboard and file transfer

#### Remote

Provides protocol-neutral session management and adapters for:

- RDP
- VNC
- SSH
- Proxmox console
- browser display and input transport

Reusable Julgate code is extracted into this package. Julgate remains deployable until parity is proven.

#### Docker

Provides:

- host and engine connections through Host Connectors
- connection, single-image and Compose application installation
- Compose project, service and container inventory
- images, volumes and networks
- health, logs and controlled lifecycle actions
- application discovery
- Docker-specific problem detection

#### Proxmox

Provides:

- clusters, nodes, VMs and LXCs
- CPU, memory, load and storage
- tasks, backups and snapshots
- controlled lifecycle actions
- console requests through Remote

#### Files

Provides one file-manager application and provider-neutral operations for:

- Host Connector-local paths
- SMB
- SFTP
- WebDAV
- later Docker volume providers

#### Caddy

Provides a small native integration:

- availability and configuration status
- routes summary
- certificate status
- reload and ACME problems
- deep link or Browser launch into Caddy UI

Caddy UI remains the complete management product.

#### Discovery

Provides network and service observations through Host Connectors:

- ARP
- ICMP
- mDNS
- SSDP
- optional SNMP
- known service probes

Discovery creates proposals. It never grants management access automatically.

## 8. Infrastructure hierarchy

JulOS represents infrastructure with stable resources and relationships:

```text
Proxmox cluster
  └─ Node
      ├─ VM or LXC
      │   └─ Host Connector
      │       └─ Docker engine
      │           └─ Compose project
      │               └─ Service
      │                   └─ Container
      └─ Storage
```

An application may be related to a service, route, connection and browser launch definition. Problems can propagate visually upward without replacing the original source problem.

## 9. Discovery and application registration

### 9.1 Docker application discovery order

1. explicit JulOS labels
2. saved manual mapping
3. Compose metadata
4. Caddy integration route
5. published ports
6. image and service heuristics

Stable Docker app identity:

```text
host-connector-id + docker-engine-id + compose-project + service-name + application-slot
```

Container IDs and ephemeral IP addresses are not application identities.

### 9.2 Approval lifecycle

```text
Observed → Proposed → Approved → Managed
                    └→ Ignored
```

Automatic discovery does not place applications on the desktop or grant permissions.

## 10. Widgets and problem center

Widgets provide small current summaries. They do not become replacement management applications.

Initial widgets:

- host CPU, load and memory
- storage usage
- network throughput
- VM and LXC status
- Docker service health
- current problems
- backup status
- certificate status
- active remote sessions

Every widget supports:

- loading
- current
- stale
- offline
- error
- unauthorized

A displayed value includes its observation time when it is not live.

Problems use a common model with source, stable resource identity, severity, timestamps, state, suggested action and deep link. Repeated observations update one problem instead of generating duplicates.

## 11. Security concept

JulOS intentionally reaches sensitive local systems. Security is therefore a product function, not a later hardening task.

- all authorization decisions occur on the backend
- Host Connectors establish outbound authenticated connections
- no general remote shell is exposed by a Host Connector
- secrets are encrypted and represented by opaque references
- runtime credentials are short-lived
- package permissions are visible before enablement
- destructive actions require explicit confirmation
- every mutation creates an audit event
- browser and remote runtimes have resource and inactivity limits
- clipboard, upload, download and drive redirection are independently permissioned
- Docker sockets and internal management APIs are never exposed publicly
- safe mode starts JulOS without optional packages

## 12. Deployment concept

JulOS 1.0 uses Docker Compose for the control plane.

Required services:

- JulOS Server
- JulOS Desktop assets served by Server
- core database (SQLite by default, PostgreSQL optional)
- Runtime Manager with narrowly scoped control of JulOS package runtime containers

Optional services:

- Browser runtime pool
- Remote runtime and guacd
- package workers
- Host Connectors on Proxmox nodes, Docker hosts and selected VMs

The Runtime Manager is control-plane infrastructure and is not the Docker package. It may manage only JulOS-owned runtime resources with mandatory labels and allowlisted configuration.

## 13. Data ownership

Core owns:

- users, roles and permissions
- package installations
- capability registrations
- application definitions
- desktop layouts and window state
- session references
- Host Connectors and connections
- client devices, workspace preferences and application-installation references
- problem, notification and audit metadata
- secret references

Packages own:

- package configuration
- package-specific operational state
- connection metadata for their domain

External products own:

- infrastructure configuration and history
- container, VM, route, certificate and file data

Browser runtime owns isolated profile data. File contents remain in the configured provider.

## 14. Reliability behavior

- Core startup does not depend on optional packages.
- A package crash becomes a visible package fault.
- Last-known data is never displayed as current.
- Reconnection does not require full-page reload.
- Mutations are idempotent where retries are expected.
- Settings and layouts use revisions to prevent silent overwrites.
- Database migrations are ordered, transactional where possible and tested for upgrade and rollback constraints.
- Backup and restore are release acceptance requirements.

## 15. JulOS 1.0 scope

JulOS 1.0 is complete when a fresh homelab deployment can:

1. install and start the control plane
2. create an administrator and sign in
3. enroll Host Connectors where local host access is required
4. install and configure official extension packages
5. connect Proxmox and Docker
6. discover and approve applications
7. show current host, storage, VM and container status
8. show actionable problems
9. open an internal website through a real Browser session
10. open RDP, VNC or SSH sessions where configured
11. access configured file providers
12. open Caddy UI through the Caddy integration
13. install the PWA and restore phone, tablet, single-display and multi-display workspaces, including an explicit two-app Phone split
14. connect existing services or install single-image and Compose applications from official and custom catalogs
15. back up and restore the control-plane and managed application state
16. recover through safe mode when an optional package fails

## 16. Explicit non-goals for 1.0

- a centrally governed commercial marketplace
- unsandboxed untrusted native extension execution
- Kubernetes
- high availability
- native mobile applications
- unrestricted host shell automation
- automatic destructive remediation
- long-term metrics analytics platform
- replacement of Proxmox, Docker or Caddy UI
- support for every remote or file protocol
