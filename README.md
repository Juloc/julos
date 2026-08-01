# JulOS

JulOS is a lightweight browser-based desktop environment for homelabs.

It provides a fast desktop shell with independent windows, snapping, widgets, applications, sessions and installable feature packages. Large management features remain in focused services such as Caddy UI. JulOS integrates them through stable package APIs instead of rebuilding every product inside the core.

Initial deployment domain: `os.juloc.de`.

## Product principles

- The core stays small and knows no Docker, Proxmox, Caddy, file or remote protocol details.
- Features are enabled through explicit signed packages and versioned capabilities.
- Applications run in independent desktop windows; iframe integration is not the general application runtime.
- Browser access uses a real isolated Chromium runtime inside the configured target network.
- Remote sessions reuse Julgate session and transport code through a controlled extraction, not source duplication.
- Existing products remain authoritative for their domains.
- Package workers and session runtimes are isolated from the core process.
- No workarounds, hidden fallbacks, temporary duplicate implementations or broad exception suppression.
- Prefer small readable code and proven abstractions over generic frameworks and premature complexity.
- Documentation changes are part of every functional change.

## Planned official packages

- Browser
- Remote
- Docker
- Proxmox
- Files
- Caddy
- Discovery

## Documentation

Start with the complete [documentation map](docs/README.md).

Main specifications:

- [`AGENTS.md`](AGENTS.md): mandatory engineering and AI-agent rules
- [`docs/PRODUCT.md`](docs/PRODUCT.md): product identity, scope and 1.0 success criteria
- [`docs/CONCEPT.md`](docs/CONCEPT.md): complete product and system concept
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md): system boundaries and dependency direction
- [`docs/TECHNICAL_SPECIFICATION.md`](docs/TECHNICAL_SPECIFICATION.md): concrete runtime and implementation rules
- [`docs/UX_SPECIFICATION.md`](docs/UX_SPECIFICATION.md): desktop, windows, widgets and responsive behavior
- [`docs/PACKAGES.md`](docs/PACKAGES.md): package format, lifecycle and package boundaries
- [`docs/DATA_AND_API_CONTRACTS.md`](docs/DATA_AND_API_CONTRACTS.md): data model and API conventions
- [`docs/SECURITY_AND_OPERATIONS.md`](docs/SECURITY_AND_OPERATIONS.md): security, deployment, backup and recovery
- [`docs/QUALITY_AND_TESTING.md`](docs/QUALITY_AND_TESTING.md): validation and definition of done
- [`docs/JULGATE_MIGRATION.md`](docs/JULGATE_MIGRATION.md): migration into JulOS Remote
- [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md): milestone sequence
- [`docs/WORK_BREAKDOWN.md`](docs/WORK_BREAKDOWN.md): junior-ready issue blueprint
- [`docs/BACKLOG.md`](docs/BACKLOG.md): current implementation status
- [`docs/DECISIONS.md`](docs/DECISIONS.md): accepted architecture decisions

## Repository status

The product and architecture specification is complete enough to begin the bounded foundation implementation. The next implementation item is the solution skeleton in GitHub issue #2. Later work must follow the dependency order in `docs/WORK_BREAKDOWN.md`.

## Initial repository strategy

Core, Desktop, Agent, Package SDK, official packages and runtime images remain in this monorepo until the package contracts and release process are stable. Separate package repositories are not created during the initial implementation.

## License

No license has been selected yet. Until a license is added, all rights are reserved.
