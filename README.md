# JulOS

JulOS is a lightweight, browser-based desktop environment for homelabs.

It provides a fast desktop shell with windows, snapping, widgets, applications, sessions and installable feature packages. Large management features remain in focused services such as Caddy UI. JulOS integrates them through small, stable package APIs instead of rebuilding every product inside the core.

Initial deployment domain: `os.juloc.de`.

## Product principles

- The core stays small and knows no Docker, Proxmox, Caddy, file or remote protocol details.
- Features are enabled through explicit packages and capabilities.
- Applications run in independent desktop windows; no iframe-based application integration.
- Browser access uses a real isolated browser runtime inside the target network.
- Remote sessions reuse the proven Julgate session backbone after it is extracted into packages.
- Existing products remain the source of truth for their domains.
- No workarounds, hidden compatibility layers or temporary architecture shortcuts.
- Prefer simple, readable and testable code over generic frameworks and premature abstraction.
- Documentation changes are part of every functional change.

## Planned packages

- Browser
- Docker
- Proxmox
- Remote
- Files
- Caddy
- Discovery

## Documentation

- [`AGENTS.md`](AGENTS.md): mandatory engineering and AI-agent rules
- [`docs/PRODUCT.md`](docs/PRODUCT.md): product scope, branding and user experience
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md): system boundaries and runtime design
- [`docs/PACKAGES.md`](docs/PACKAGES.md): package contracts and lifecycle
- [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md): ordered junior-ready delivery plan
- [`docs/BACKLOG.md`](docs/BACKLOG.md): current implementation status and next work
- [`docs/DECISIONS.md`](docs/DECISIONS.md): accepted architecture decisions

## Repository status

The repository is in the documentation and foundation phase. Production code starts only after the contracts in this repository are internally consistent.

## License

No license has been selected yet. Until a license is added, all rights are reserved.
