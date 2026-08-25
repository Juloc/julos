# Product

## Identity

- Product name: **JulOS**
- Repository: `Juloc/julos`
- Initial domain: `os.juloc.de`
- Product category: lightweight PWA-first homelab desktop and application workspace
- Default language: English
- Supported language: German

JulOS is a product name, not a claim that it is a general-purpose operating system.

## Vision

JulOS replaces many open browser tabs with one fast workspace for a homelab. Users open applications, remote sessions, files and infrastructure views in independent windows and arrange them like a desktop environment.

JulOS does not replace specialized products. It connects them through packages and opens the correct detailed tool when deeper management is required.

## Primary outcomes

- Reach local-only systems securely while away from home.
- See host, VM, container, storage and service problems without opening multiple dashboards.
- Launch discovered applications from one searchable app registry.
- Work with several applications at once using movable, resizable and snap-enabled windows.
- Add only the capabilities a deployment needs.
- Keep the core lightweight when no optional package is installed.
- Connect existing services or install Docker images and Compose applications from user-selected sources.
- Continue the appropriate workspace on phone, tablet, single-display desktop or multi-display desktop.

## Desktop experience

The desktop provides:

- taskbar and application launcher
- search and command palette
- independent application windows
- minimize, maximize, resize and close
- left, right and quarter snapping
- multi-window support per application where allowed
- shared layouts per user and workspace class, with optional per-device layouts
- desktop widgets
- notifications and problem center
- session restore after reload or reconnect

Phones use one full-screen application by default and an explicitly activated split view with at most two visible applications. Tablets use a touch-optimized desktop model with maximized, split and—where space permits—free windows. Background application surfaces suspend by default; a user may keep a selected surface active on a best-effort basis.

## Application types

- native JulOS package application
- connected existing service
- catalog-installed Docker image or Compose application
- full remote browser session
- RDP, VNC or SSH session
- file manager window
- external product integration window
- settings or package-management window

JulOS does not use iframes as its general application runtime.

## Package strategy

JulOS distinguishes platform extension packages from catalog applications. Extension packages contribute trusted or isolated JulOS code. Catalog applications connect or deploy external services and remain authoritative in their own runtime. The Store presents both without conflating their security models.

Initial packages:

- Browser: isolated Chromium sessions inside the target network
- Docker: hosts, Compose projects, containers, application installation, health, logs and discovered apps
- Proxmox: nodes, VMs, LXCs, storage, tasks and console links
- Remote: reusable Julgate-derived RDP, VNC and SSH session capabilities
- Files: Host Connector-local files, SMB, SFTP and WebDAV providers
- Caddy: small status and deep-link integration for Caddy UI
- Discovery: network discovery and device approval

## Host access

An optional Host Connector is installed only on hosts where JulOS needs local metrics, Docker, files or network access. It connects outbound and exposes typed capabilities. It is not an assistant, chat product or general shell. Hermes and similar assistants are ordinary user-selected Docker/Compose applications.

## Existing products

- Caddy UI remains the full Caddy management product.
- Julgate remains available during extraction and migration. It can be archived only after JulOS Remote reaches documented functional parity.
- Proxmox, Docker and other systems remain authoritative for their own state.

## Non-goals for 1.0

- a centrally governed commercial marketplace or mandatory publisher review service
- Kubernetes support
- replacing complete domain products
- native mobile applications
- unrestricted host shell execution
- automatic destructive remediation
- long-term metrics platform
- high-availability control plane

## Success criteria for 1.0

A single-user homelab deployment can install JulOS as a PWA, enroll Host Connectors, connect Proxmox and Docker hosts, connect or install applications from official and custom sources, accept a clear warning for unsigned definitions, discover applications, view current problems, open local or streamed internal web applications, use remote sessions, access files and restore the correct phone, tablet, single-display or multi-display workspace without directly exposing internal services.
