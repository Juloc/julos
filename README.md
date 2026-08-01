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

## Repository status

The repository is being initialized documentation-first. Architecture, implementation order, package contracts and contribution rules are defined before production code is added.

## Documentation

The complete documentation will live under `docs/`. Contributors and AI agents must read `AGENTS.md` before changing the repository.

## License

No license has been selected yet. Until a license is added, all rights are reserved.
