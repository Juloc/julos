# Browser runtime

## Purpose

BRW-001 provides one isolated Chromium runtime image for the JulOS Browser package. Runtime creation stays behind Runtime Manager and presentation/session ownership stays on the protocol-neutral Remote path. The Browser package does not introduce a second container, streaming or session subsystem.

BRW-002 adds Browser-owned profile metadata and network policy. BRW-003 resolves that product-specific policy inside the Browser worker and hands Core one generic `interactive.session/1.0.0` runtime plan. Core contains no Browser-, Chromium- or VNC-specific orchestration.

## Image inputs

The image is built from:

- a Debian Bookworm slim base pinned by OCI digest;
- Debian and Debian Security package indexes pinned to explicit snapshot timestamps;
- Chromium pinned to one exact Debian package version;
- Xvfb for the deterministic virtual display;
- Openbox as the minimal window manager;
- x11vnc as the internal display endpoint;
- Tini as PID 1 for signal and child-process handling.

The snapshot timestamps and Chromium version are build arguments with immutable defaults in the Dockerfile. Updating Chromium therefore requires a deliberate repository change instead of silently consuming a moving package.

The image runs as UID and GID `10001`. It contains no default account password, display password, API key or other credential.

## Runtime definition and image selection

`packages/JulOS.Browser/runtime/runtime-definition.json` is the Browser-owned source definition for the runtime image, internal display endpoint and resource limits.

It declares:

- package `de.juloc.julos.browser`;
- image repository `ghcr.io/juloc/julos-browser-runtime`;
- internal display port `5900`;
- 2 CPU limit;
- 1024 MiB memory limit;
- 256 PID limit;
- `JULOS_VNC_PASSWORD` as required secret environment;
- `JULOS_START_URL=about:blank` as the non-secret default.

The Browser package configuration supplies the published immutable image through `runtimeImage`. The value must be a lowercase `repository@sha256:digest` reference. Missing configuration does not prevent package activation, but session creation fails closed with `browser.runtime_not_configured`.

Runtime Manager validates and applies the package identity, immutable image, CPU/memory/PID limits, exact configured network, package-owned volumes and secret environment. No host port is published.

## Browser profile policy

BRW-002 defines three modes:

- `Persistent` retains one named profile for one JulOS user;
- `Temporary` exists only for one runtime and has no persistent profile volume;
- `Application` retains a user-owned profile for one fixed application identity and fixed HTTP/HTTPS start URL.

Every retained profile stores its owning JulOS user ID. Reads and deletes filter by both profile identity and owner identity.

Only profile metadata belongs in the package database. Chromium profile bytes remain in isolated runtime volumes. Persistent volume names are derived from owner/profile identities and contain no user-visible names. Runtime Manager accepts only package-owned volume names and mounts retained Browser profiles at `/var/lib/julos-browser/profile`. Temporary sessions use `/tmp/julos-browser/profile`, which disappears with the runtime.

Browser profile metadata uses the package-owned database supplied by the package worker supervisor and supports both the SQLite alpha deployment and PostgreSQL package storage. No Browser-specific database service is introduced.

## Browser network policy

Browser network profiles are package-owned configuration, not arbitrary network input from the frontend.

Each profile contains:

- a stable Browser-local key;
- one exact Runtime Manager network from the Browser allowlist;
- an optional opaque JulOS secret-reference ID reserved for explicit proxy support;
- an optimistic revision.

`allowedNetworks` is the exact package allowlist. `defaultNetwork`, when configured, must be a member of that list. Unknown networks fail in the Browser worker before Core may allocate a runtime.

The selected network must also be reachable by the configured Remote presentation provider. For the current Browser runtime, the Remote network profile therefore needs the same runtime network, internal port `5900`, and the narrow target pattern `julos-interactive-*`. Remote policy accepts a trailing wildcard only in the `<dns-label>-*` form; arbitrary prefix wildcards remain invalid.

## Generic interactive-session boundary

The Browser manifest requires `interactive.session/1.0.0`. The capability exposes generic create, read and terminate operations for package-owned interactive runtimes.

The Browser frontend sends an opaque Browser request containing the start URL and profile selection. Core does not interpret those fields. It forwards the opaque request only to the already-running Browser worker through the private package-worker command boundary.

The Browser worker validates the URL, user-owned profile, Browser network, immutable runtime image and timeout. It then returns a generic runtime plan containing:

- installed package version and digest-pinned image;
- bounded resource limits;
- non-secret environment;
- one exact runtime network;
- optional package-owned volumes;
- presentation protocol and internal port;
- one short-lived presentation credential;
- initial viewport and timeouts.

Core executes that plan through existing generic platform boundaries only:

1. validate the authenticated package caller and idempotency key;
2. ask that caller's own worker for the generic runtime plan;
3. validate the plan's presentation target and runtime network against Remote policy;
4. allocate the runtime through Runtime Manager;
5. store the presentation credential as an encrypted package-owned Secret Reference;
6. create the existing protocol-neutral Remote session targeting the internal runtime DNS name;
7. use the existing Remote provisioner to produce the same-origin display descriptor.

The response contains only session ID, state, timestamps, same-origin display descriptor and caller-safe failure. Internal runtime DNS names, networks, Runtime Manager identities and credentials are never returned to the package frontend.

The runtime identity is deterministic from authenticated user, caller package, operation key and opaque package request. A short generic creation lock prevents concurrent calls with the same idempotency key from racing runtime, secret and Remote-session creation. The durable Remote session remains the single session record; BRW-003 adds no Browser session table.

## Display credential

The Browser worker generates a new eight-character printable Base64 credential for each runtime because the current x11vnc password mechanism is limited to eight characters.

The credential crosses only the private worker channel and generic trusted Core boundaries. Runtime Manager injects it through secret environment, while the encrypted Secret Reference lets the existing Remote provider authenticate to the display endpoint. The value is not included in capability responses or audit payloads.

The Browser launcher:

1. refuses startup when the secret is absent or invalid;
2. writes the derived display password file with user-only permissions;
3. removes the secret from its environment before child processes start;
4. removes the password file with the temporary runtime directory during cleanup.

There is no default or fallback credential.

## Cleanup

Remote session lifecycle remains authoritative for presentation-provider cleanup. The same lifecycle worker performs one bounded generic interactive-session cleanup pass after normal Remote reconciliation.

For terminal interactive sessions the cleanup service:

- removes the package runtime idempotently through Runtime Manager;
- tombstones the encrypted presentation secret only after runtime removal succeeds;
- leaves package-owned retained volumes untouched;
- retries on a later reconciliation pass when cleanup fails.

The undeleted interactive-session secret is the retry marker, so no second cleanup table or scheduler is introduced. Temporary Browser profile data has no named volume and disappears with the runtime.

## Deterministic display

The image fixes:

- locale to `en_US.UTF-8`;
- timezone to UTC;
- display to `:99`;
- screen to `1280x800x24`;
- DPI to `96`;
- a bounded installed font set.

BRW-004 may expose user-visible viewport choices, but they must map to explicit bounded runtime values rather than mutate host display state.

## Process lifecycle

Tini runs one shell launcher. The launcher starts Xvfb, Openbox, x11vnc and Chromium, records their PIDs and waits for Chromium.

On normal exit, interruption or termination it:

- terminates all recorded child processes;
- waits for them to exit;
- removes the complete temporary runtime directory, logs and password file;
- preserves an explicitly mounted retained profile directory;
- returns Chromium's exit code when Chromium ends normally.

The health probe requires all four processes and the local IPv4 display port. It has a 30-second startup grace period and becomes unhealthy after three failed bounded checks.

## Chromium sandbox boundary

Chromium runs as a non-root user. Runtime Manager drops all Linux capabilities and enables `no-new-privileges`. Chromium's own setuid sandbox cannot initialize under that policy, so the launcher uses `--no-sandbox`. The security boundary is the dedicated unprivileged container, exact runtime network, resource limits and same-origin display proxy. Broad host networking, host mounts and host browser execution remain forbidden.

## Publication

`.github/workflows/publish-browser-runtime.yml` runs only for the integration branch.

It:

1. validates the complete repository;
2. builds the image and runs a lifecycle smoke test with Runtime Manager-equivalent limits;
3. proves that missing credentials fail and no host port is bound;
4. refuses to overwrite an existing version;
5. builds Linux AMD64 and ARM64 images;
6. publishes only the repository-version tag to GHCR;
7. creates a GitHub provenance attestation for the image digest;
8. uploads the exact digest reference as retained workflow evidence.

No `latest` tag is created. The published `repository@sha256:digest` reference is configured as the Browser package `runtimeImage`; BRW-003 never depends on a mutable tag.
