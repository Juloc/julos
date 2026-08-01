# AGENTS.md

This file is mandatory for every human or AI contributor.

## Required reading order

Before changing code, read:

1. `README.md`
2. `AGENTS.md`
3. `docs/PRODUCT.md`
4. `docs/ARCHITECTURE.md`
5. `docs/PACKAGES.md`
6. `docs/IMPLEMENTATION_PLAN.md`
7. `docs/BACKLOG.md`
8. `docs/DECISIONS.md`

Read the relevant package documentation before changing a package.

## Non-negotiable engineering rules

- Never add a workaround, hidden fallback, compatibility hack or temporary duplicate implementation.
- Fix the root cause. If the correct design is not yet available, keep the work explicitly blocked instead of creating a misleading partial solution.
- Keep the core independent of Docker, Proxmox, Caddy, remote protocols, filesystems and discovery implementations.
- Packages communicate through versioned contracts and capabilities, never through another package's database, internal classes or private endpoints.
- Existing domain products remain the source of truth. JulOS stores integration state, not competing copies of domain data.
- Prefer the smallest clear implementation that satisfies the documented requirement.
- Do not introduce abstraction before at least two real consumers require it.
- Do not add dependencies when the platform or existing repository code can solve the requirement clearly.
- Do not suppress warnings broadly, swallow exceptions or convert failures into fake success states.
- Do not leave dead code, commented-out implementations or untracked TODO comments.
- Security and permission checks belong in the backend, not only in the UI.
- Secrets must never be returned to frontend packages or written to logs.
- Package failure must not prevent the JulOS core and desktop from starting.

## Documentation rule

Documentation is part of the implementation.

Every pull request must update all affected Markdown files in the same change. At minimum verify:

- architecture and package contracts
- implementation status and backlog
- setup and operational instructions
- decisions that changed
- user-visible behavior

A task is not complete while its documentation describes an older state.

## Change workflow

1. Select one backlog item with a clear acceptance criterion.
2. Create `agent/<short-description>` from `main`.
3. Inspect existing contracts, tests and documentation before editing.
4. Implement one coherent change without unrelated cleanup.
5. Update tests and documentation in the same commit or pull request.
6. Run the smallest relevant validation set, then the repository validation before merge.
7. Open a draft pull request with purpose, design impact, validation and remaining limitations.
8. Merge only when acceptance criteria, tests and documentation are complete.

## Definition of done

A change is done only when:

- the documented requirement is implemented
- package and core boundaries remain intact
- relevant automated tests pass
- errors are observable and actionable
- permissions and secrets were reviewed
- no workaround or duplicate path was introduced
- affected Markdown files match the implementation
- the backlog status is updated

## Encoding and naming

- Repository text uses UTF-8 with BOM and CRLF unless a runtime format requires LF.
- Shell scripts, Docker entrypoints and other Unix-executed text use UTF-8 and LF.
- Code, contracts, commits and repository documentation use English.
- User-facing text is localizable; English is the default language and German is supported.
- Public types and APIs use clear domain names. Avoid abbreviations that are not established domain terms.
