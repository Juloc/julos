# JulOS Browser

JulOS Browser provides isolated full-browser sessions instead of embedding private applications in iframes.

Current package components:

- `worker/` contains lifecycle, Browser profile policy and package-owned profile metadata storage;
- `frontend/` contains the Desktop custom element;
- `runtime/` contains the unprivileged Chromium image, launcher, health probe and Runtime Manager definition.

## Profiles

BRW-002 defines three modes:

- `Persistent` — a named profile retained for exactly one JulOS user;
- `Temporary` — session-local state with no persistent profile volume;
- `Application` — a retained user-owned profile with a fixed HTTP/HTTPS start target.

Retained profile metadata is stored only in the Browser package database. Chromium profile bytes remain in package-owned runtime volumes. Profile reads and removals are always scoped by the authenticated user ID, so retained profiles are not shared between users.

Browser network profiles map a stable package-local key to an exact administrator-allowlisted Runtime Manager network. Optional proxy credentials are stored only as opaque JulOS secret-reference IDs; the Browser package never stores the secret value.

The package database implementation supports the JulOS SQLite alpha deployment and PostgreSQL deployments. Temporary mode is intentionally rejected by the persistent profile store and receives no persistent runtime volume.

The runtime image is published as an immutable, attested GHCR artifact. BRW-003 will request it only through Runtime Manager with the exact digest, declared limits, selected allowlisted network and a generated runtime-only VNC password.

Architecture and operations are documented in `docs/BROWSER-RUNTIME.md`. Runtime/session creation belongs to BRW-003, full Browser controls to BRW-004 and application-mode presentation to BRW-005.
