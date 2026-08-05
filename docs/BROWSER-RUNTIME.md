# Browser runtime

## Purpose

BRW-001 provides one isolated Chromium runtime image for the JulOS Browser package. Runtime creation remains owned by Runtime Manager and session ownership remains on the protocol-neutral REM-004 path. The Browser package does not receive a second container or session subsystem.

## Image inputs

The image is built from:

- a Debian Bookworm slim base pinned by OCI digest;
- Debian and Debian Security package indexes pinned to explicit snapshot timestamps;
- Chromium pinned to one exact Debian package version;
- Xvfb for the deterministic virtual display;
- Openbox as the minimal window manager;
- x11vnc as the internal display endpoint;
- Tini as PID 1 for signal and child-process handling.

The snapshot timestamps and Chromium version are build arguments with immutable defaults in the Dockerfile. This prevents a previously valid build from failing when Debian rotates packages out of its current mirrors. Updating Chromium therefore requires one deliberate commit that updates the package version and, when needed, the matching snapshot timestamps.

The image runs as UID and GID `10001`. It adds no default account password, VNC password, API key or other credential.

## Runtime definition

`packages/JulOS.Browser/runtime/runtime-definition.json` is the single Browser runtime definition consumed by BRW-003.

It declares:

- package `de.juloc.julos.browser`;
- image repository `ghcr.io/juloc/julos-browser-runtime`;
- VNC on internal port `5900`;
- 2 CPU limit;
- 1024 MiB memory limit;
- 256 PID limit;
- `JULOS_VNC_PASSWORD` as required secret environment;
- `JULOS_START_URL=about:blank` as the non-secret default.

BRW-003 combines the repository version with the digest produced by the publication workflow. Runtime Manager accepts only the resulting digest-pinned image reference. It applies the declared CPU, memory and PID limits, the exact configured network, dropped capabilities and `no-new-privileges`.

No host port is published. The display proxy reaches port `5900` only through the package runtime network. x11vnc uses an explicit IPv4 listener and disables its optional IPv6 listener so startup and health checks cannot vary with the host's dual-stack socket behavior.

## Display credential

The launcher requires exactly eight printable ASCII characters because the VNC password mechanism used by x11vnc has an eight-character protocol limit. The value is supplied through Runtime Manager's secret-environment channel.

The launcher:

1. refuses startup when the secret is absent or invalid;
2. writes the derived VNC password file with user-only permissions;
3. removes the secret from its environment before child processes start;
4. removes the password file with the complete temporary runtime directory during cleanup.

There is no default or fallback credential.

## Deterministic display

The image fixes:

- locale to `en_US.UTF-8`;
- timezone to UTC;
- display to `:99`;
- screen to `1280x800x24`;
- DPI to `96`;
- a bounded installed font set.

BRW-002 and BRW-004 may expose user-visible profile and viewport choices, but they must map to explicit bounded runtime values rather than mutate host display state.

## Process lifecycle

Tini runs one shell launcher. The launcher starts Xvfb, Openbox, x11vnc and Chromium, records their PIDs and waits for Chromium.

On normal exit, interruption or termination it:

- terminates all recorded child processes;
- waits for them to exit;
- removes the complete temporary profile, logs and VNC password file;
- returns Chromium's exit code when Chromium ends normally.

The health probe requires all four processes and the local IPv4 VNC port. It has a 30-second startup grace period and becomes unhealthy after three failed bounded checks.

## Chromium sandbox boundary

Chromium runs as a non-root user. Runtime Manager drops all Linux capabilities and enables `no-new-privileges`. Chromium's own setuid sandbox cannot initialize under that policy, so the launcher uses `--no-sandbox`. The security boundary is therefore the dedicated unprivileged container, exact runtime network, resource limits and same-origin display proxy. Broad host networking, host mounts and host browser execution remain forbidden.

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

No `latest` tag is created. BRW-003 must use the recorded `repository@sha256:digest` reference, never the mutable version tag alone.
