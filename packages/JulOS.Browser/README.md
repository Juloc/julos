# JulOS Browser

JulOS Browser provides isolated full-browser sessions instead of embedding private applications in iframes.

Current package components:

- `worker/` contains the package worker lifecycle and configuration surface;
- `frontend/` contains the Desktop custom element;
- `runtime/` contains the unprivileged Chromium image, launcher, health probe and Runtime Manager definition.

The runtime image is published as an immutable, attested GHCR artifact. The Browser worker must later request it only through Runtime Manager with the exact digest, declared limits, configured network and a generated runtime-only VNC password.

Architecture and operations are documented in `docs/BROWSER-RUNTIME.md`. Profile policy belongs to BRW-002, runtime/session creation to BRW-003 and full Browser controls to BRW-004.
