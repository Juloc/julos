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

## Build and test

Building requires the .NET SDK version pinned in [`global.json`](global.json).

```bash
dotnet restore JulOS.slnx
dotnet build JulOS.slnx
dotnet test --solution JulOS.slnx
```

`dotnet test` runs on Microsoft.Testing.Platform, which `global.json` selects.

The Desktop client is built from `src/JulOS.Desktop` with Node 24 or newer:

```bash
npm ci
npm run typecheck
npm test
npm run build
```

One command runs everything the repository can validate:

```bash
sh tools/validate.sh
```

On Windows:

```bash
pwsh tools/validate.ps1
```

Both entry points call `tools/validate.mjs`, so they run identical checks. Add `--list` to see the stages, `--stage <name>` to run one. A failed stage exits non-zero and names itself. `node tools/normalize-encoding.mjs` corrects encoding-policy violations.

The core database is migrated only by the explicit migration command:

```bash
dotnet tool restore
dotnet run --project src/JulOS.Server -- --migrate-database
```

Normal Server startup never changes the schema. The development Compose stack runs this command in its one-shot `migrate` service before starting Server.

## Run locally

```bash
cd deploy/compose
cp .env.example .env
docker compose up --build
```

Set `JULOS_POSTGRES_PASSWORD` in `.env` first; the stack refuses to start without it. See [`deploy/compose/README.md`](deploy/compose/README.md).

## Versioning

[`VERSION`](VERSION) is the single version source. It drives every assembly, the container image label and `GET /api/v1/system/version`. No deployment depends on an unpinned `latest` reference.

[`docs/RELEASE_NOTES_TEMPLATE.md`](docs/RELEASE_NOTES_TEMPLATE.md) is the release-note template.

## Repository status

The product and architecture specification is complete, and the engineering foundation of phase 0 is in place: the solution builds, architecture tests enforce the dependency boundaries, the Desktop toolchain type checks and builds, one command validates the repository, the development stack runs, and continuous integration runs that same command.

Work continues through the dependency order in [`docs/WORK_BREAKDOWN.md`](docs/WORK_BREAKDOWN.md); [`docs/BACKLOG.md`](docs/BACKLOG.md) names the current item.

## Initial repository strategy

Core, Desktop, Agent, Package SDK, official packages and runtime images remain in this monorepo until the package contracts and release process are stable. Separate package repositories are not created during the initial implementation.

## License

No license has been selected yet. Until a license is added, all rights are reserved.
