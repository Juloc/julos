# JulOS

JulOS is a lightweight browser-based desktop environment for homelabs.

It provides a fast desktop shell with independent windows, snapping, widgets, applications, sessions and installable feature packages. Large management features remain in focused services such as Caddy UI. JulOS integrates them through stable package APIs instead of rebuilding every product inside the core.

Initial deployment domain: `os.juloc.de`.

## Alpha release

`0.3.0-alpha.1` is the first installable evaluation build. It is intended to verify installation, desktop interaction and the package architecture before the remaining 1.0 packages are implemented.

Use the minimal [`deploy/alpha/compose.yaml`](deploy/alpha/compose.yaml) stack and follow the [`alpha installation guide`](deploy/alpha/README.md). The exact scope and known boundaries are documented in the [`0.3.0-alpha.1 release notes`](docs/releases/0.3.0-alpha.1.md).

The alpha is not intended for direct public internet exposure. Docker, Proxmox, Files, Caddy, discovery, final hardening and Julgate migration remain outside this release.

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

After a fresh migration, `GET /api/v1/auth/status` reports that initial setup is required. Create the first administrator once through `POST /api/v1/auth/setup`; subsequent API calls use the secure `.JulOS.Session` cookie. Authenticated users read their current profile from `GET /api/v1/profile` and update validated language, time-zone, theme and motion preferences through `PUT /api/v1/profile/preferences` with an antiforgery token and current revision. Authentication and profile payloads, failures and operational defaults are specified in [`docs/DATA_AND_API_CONTRACTS.md`](docs/DATA_AND_API_CONTRACTS.md) and [`docs/SECURITY_AND_OPERATIONS.md`](docs/SECURITY_AND_OPERATIONS.md).

## Run locally

For the released alpha, use [`deploy/alpha`](deploy/alpha/README.md). For source development:

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

The foundation, core model, persistence, authentication, desktop shell and package platform are implemented. Agent, Remote and Browser foundations are included in the alpha with explicit deployment-validation limits. The remaining official packages and final 1.0 hardening continue through the dependency order in [`docs/WORK_BREAKDOWN.md`](docs/WORK_BREAKDOWN.md); [`docs/BACKLOG.md`](docs/BACKLOG.md) remains the authoritative item status.

## Initial repository strategy

Core, Desktop, Agent, Package SDK, official packages and runtime images remain in this monorepo until the package contracts and release process are stable. Separate package repositories are not created during the initial implementation.

## License

No license has been selected yet. Until a license is added, all rights are reserved.

## Durable operations

Long-running control-plane work uses versioned operation resources backed by the core database (SQLite by default, PostgreSQL optional). Creation is idempotent, progress and cancellation survive reconnects, and success is recorded only after the owning executor verifies the requested state. See [`docs/DATA_AND_API_CONTRACTS.md`](docs/DATA_AND_API_CONTRACTS.md#7-background-operations).
