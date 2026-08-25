# AGENTS.md

This file is mandatory for every human or AI contributor.

## Required reading order

Before changing code, read:

1. `README.md`
2. `AGENTS.md`
3. `docs/README.md`
4. `docs/PRODUCT.md`
5. `docs/CONCEPT.md`
6. `docs/ARCHITECTURE.md`
7. `docs/TECHNICAL_SPECIFICATION.md`
8. `docs/UX_SPECIFICATION.md` for user-facing work
9. `docs/MOBILE_PWA.md` for Desktop, PWA, layout or lifecycle work
10. `docs/PACKAGES.md` for package work
11. `docs/APPLICATION_CATALOG.md` for catalog, Docker application or connection work
12. `docs/HOST_CONNECTOR.md` for host access, local adapters or Agent migration work
13. `docs/DATA_AND_API_CONTRACTS.md` for persistence or transport work
14. `docs/SECURITY_AND_OPERATIONS.md` for infrastructure, credentials or deployment work
15. `docs/QUALITY_AND_TESTING.md`
16. `docs/IMPLEMENTATION_PLAN.md`
17. `docs/WORK_BREAKDOWN.md`
18. `docs/BACKLOG.md`
19. `docs/DECISIONS.md`
20. `docs/GLOSSARY.md`

Read the relevant package documentation and linked external-repository specifications before changing a package or migration target.

## Non-negotiable engineering rules

- Never add a workaround, hidden fallback, compatibility hack or temporary duplicate implementation.
- Fix the root cause. If the correct design is not available, keep the work explicitly blocked instead of creating a misleading partial solution.
- Keep Domain and Core independent of Docker, Proxmox, Caddy, remote protocols, filesystems and discovery implementations.
- Packages communicate through versioned contracts and capabilities, never through another package's database, internal classes or private endpoints.
- Package backend logic runs out of process. A package failure must not terminate JulOS Server.
- JulOS Server never receives unrestricted access to the container runtime. Runtime Manager exposes only its narrow JulOS-owned runtime API.
- Existing domain products remain authoritative. JulOS stores connections, approvals, presentation state, derived problems and operational references, not competing domain copies.
- Prefer the smallest clear implementation that satisfies the documented requirement.
- Do not introduce abstraction before at least two real consumers require it, unless the architecture explicitly defines the boundary in advance.
- Do not add dependencies when the platform or existing repository code can solve the requirement clearly.
- Do not suppress warnings broadly, swallow exceptions or convert failures into fake success states.
- Do not leave dead code, commented-out implementations or untracked TODO comments.
- Security and permission checks belong in the backend, not only in the UI.
- Secrets must never be returned to frontend packages, embedded in URLs or written to logs.
- No arbitrary shell or Docker API proxy may be exposed through Host Connector, Server or Runtime Manager.
- Use stable identities. Never use ephemeral container IDs, IP addresses or display names as persistent application identity.
- Window lifecycle, frontend-surface execution lifecycle and runtime-session lifecycle remain separate.
- User-facing text is localizable from its first implementation.

## Documentation rule

Documentation is part of the implementation.

Every change must update all affected Markdown files in the same commit. At minimum verify:

- product and user-visible behavior
- architecture and dependency boundaries
- technical, data and API contracts
- package lifecycle and capabilities
- security and operational instructions
- tests and acceptance criteria
- implementation status and backlog
- accepted decisions

A task is not complete while its documentation describes an older state.

## Change workflow

The repository is maintained trunk-based. One completed work item becomes one commit on `main`.

1. Select one item from `docs/WORK_BREAKDOWN.md` or an approved bug issue with clear acceptance criteria.
2. Confirm every dependency of that item is already on `main`.
3. Inspect current contracts, tests, documentation and affected external repositories before editing.
4. Write or update the failing test or validation fixture when practical.
5. Implement one coherent change without unrelated cleanup.
6. Update tests and every affected Markdown file in the same commit.
7. Run the smallest relevant validation set, then the full repository validation.
8. Commit only when acceptance criteria, tests, documentation and backlog are complete.
9. Push regularly so `main` never holds long-lived unpublished work.

Use a `agent/<short-description>` branch and a pull request when a change is large, risky or needs external review before it reaches `main`. The commit content requirements are identical in both cases.

The `agent/` contributor-branch prefix is historical workflow terminology and does not name the JulOS Host Connector product component.

## Definition of done

A change is done only when:

- the documented requirement is implemented
- package, process and core boundaries remain intact
- relevant automated tests pass
- errors are observable and actionable
- cancellation, timeout and retry behavior is defined
- permissions, scopes and secret handling were reviewed
- data migration and compatibility impact is documented
- no workaround, duplicate path, dead code or unowned TODO was introduced
- English and German user-facing text exists where applicable
- affected Markdown files match the implementation
- `docs/BACKLOG.md` is updated

The full checklist in `docs/QUALITY_AND_TESTING.md` is authoritative.

## Change scope

- One work item should produce one coherent reviewable commit.
- Do not combine feature work with broad cleanup.
- Split work when separate parts can be validated independently.
- Do not split work so narrowly that temporary invalid architecture must land between commits.
- One commit may update multiple projects when one vertical contract requires it.

## Error handling

- Return typed errors with stable codes and correlation IDs.
- Preserve useful external failure causes after sanitization.
- Never report success until the requested state is verified.
- Do not retry non-idempotent operations automatically.
- Log an error once at the layer that owns handling responsibility.

## Testing

- Use real PostgreSQL for persistence integration tests.
- Add architecture tests for every new dependency rule.
- Add contract fixtures for every versioned message.
- Add regression tests for bugs.
- Do not replace protocol integration tests with mocks that cannot detect transport behavior.
- Keep test data deterministic and free of real credentials or personal infrastructure data.

## Encoding and naming

- Repository text uses UTF-8 with BOM and CRLF unless a runtime format requires LF.
- Shell scripts, Docker entrypoints and other Unix-executed text use UTF-8 and LF.
- Code, contracts, commits and repository documentation use English.
- User-facing text is localizable; English is the default language and German is supported.
- Public types and APIs use terms from `docs/GLOSSARY.md`.
- Avoid abbreviations that are not established domain terms.

## Prohibited shortcuts

- iframe as the default application runtime
- direct package-to-package references
- cross-package database reads
- raw Docker socket in Server
- arbitrary Host Connector commands
- package worker logic hosted inside Core
- secrets in browser storage
- manual database edits as a supported recovery procedure
- silent downgrade to an older protocol or implementation
- indefinite dual old/new runtime paths
- placeholder implementations that return static success
