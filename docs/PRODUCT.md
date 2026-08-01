# Product

## Identity

- Product name: **JulOS**
- Repository: `Juloc/julos`
- Initial domain: `os.juloc.de`
- Product category: lightweight homelab web desktop
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

## Desktop experience

The desktop provides:

- taskbar and application launcher
- search and command palette
- independent application windows
- minimize, maximize, resize and close
- left, right and quarter snapping
- multi-window support per application where allowed
- saved layouts per user and viewport class
- desktop widgets
- notifications and problem center
- session restore after reload or reconnect

Mobile layouts prioritize application switching, full-screen windows and safe touch targets rather than reproducing every desktop gesture.

## Application types

- native JulOS package application
- full remote browser session
- RDP, VNC or SSH session
- file manager window
- external product integration window
- settings or package-management window

JulOS does not use iframes as its general application runtime.

## Package strategy

Initial packages:

- Browser: isolated Chromium sessions inside the target network
- Docker: hosts, Compose projects, containers, health, logs and discovered apps
- Proxmox: nodes, VMs, LXCs, storage, tasks and console links
- Remote: reusable Julgate-derived RDP, VNC and SSH session capabilities
- Files: local agent files, SMB, SFTP and WebDAV providers
- Caddy: small status and deep-link integration for Caddy UI
- Discovery: network discovery and device approval

## Existing products

- Caddy UI remains the full Caddy management product.
- Julgate remains available during extraction and migration. It can be archived only after JulOS Remote reaches documented functional parity.
- Proxmox, Docker and other systems remain authoritative for their own state.

## Non-goals for 1.0

- public third-party marketplace
- Kubernetes support
- replacing complete domain products
- native mobile applications
- unrestricted remote shell execution
- automatic destructive remediation
- long-term metrics platform
- high-availability control plane

## Success criteria for 1.0

A single-user homelab deployment can install JulOS, connect Proxmox and Docker hosts, discover applications, view current problems, open a real internal browser, use remote sessions, access files and restore a multi-window desktop layout without relying on iframes or direct internet exposure of internal services.
