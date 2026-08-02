# Quality and testing

## 1. Quality objective

JulOS controls access to infrastructure. A feature is not complete when it only works in the happy path. It must have clear boundaries, tests, diagnostics, documentation and safe failure behavior.

## 2. Test layers

### 2.1 Domain unit tests

Test pure rules without databases or network access:

- package lifecycle transitions
- permission and scope evaluation inputs
- window bounds and snap calculations
- layout revision conflicts
- problem deduplication and state transitions
- session lifecycle policies
- application instance policy
- discovery approval state

### 2.2 Application service tests

Test use cases with explicit fake ports:

- package install coordination
- Agent enrollment and revocation
- capability resolution
- layout persistence
- problem observation processing
- secret lease authorization
- session create, disconnect and terminate orchestration

Fakes model only documented ports and must not reproduce infrastructure implementation complexity.

### 2.2a Infrastructure adapter tests

`tests/JulOS.Infrastructure.Tests` covers control-plane adapters that need no live dependency, such as identifier generation. Adapters that require PostgreSQL or another running system belong in the integration tests instead.

### 2.3 Architecture tests

Architecture tests enforce:

- Domain references no other JulOS project
- Core projects contain no package product namespaces
- package projects do not reference each other
- Contracts contain no EF Core or ASP.NET implementation types
- Agent contains no package-specific business logic
- Server is the composition root
- public package SDK surface is explicitly reviewed

Tests scan project references, assembly references and forbidden namespaces. Do not add a third-party architecture framework unless the custom rules become materially harder to maintain.

`tests/JulOS.Architecture.Tests` implements these rules against the repository itself rather than against its own compile-time dependencies:

- committed project files supply the project graph, so a forbidden reference cannot hide behind the test project's references
- compiled assembly metadata supplies real type usage, so implicit usings cannot hide a dependency that never appears as a `using` directive
- committed C# sources supply terminology, so a product type defined inside Core is caught even though it creates no external reference

The allowed project graph is a complete table. A new project fails the coverage test until its allowed dependencies are declared, which makes every boundary an explicit decision.

Architecture tests read build output, so the whole solution must be built before they run. `dotnet test --solution JulOS.slnx` does this; a missing assembly fails with an explicit message instead of passing silently.

### 2.4 Persistence integration tests

Run against a real supported PostgreSQL container or isolated database:

- clean migration from empty database
- upgrade from previous supported release fixture
- optimistic concurrency
- package schema isolation
- transactional package migration failure
- backup metadata consistency
- query pagination and indexes

SQLite is not used as a substitute for PostgreSQL behavior.

Continuous integration starts the pinned supported PostgreSQL image and sets `JULOS_TEST_POSTGRES` to its maintenance database. Each persistence test creates an isolated database, applies committed migrations and drops the database afterward. A local run without that variable reports the PostgreSQL tests as inconclusive rather than pretending they passed.

`API-001` covers clean migration, database enforcement of representative domain invariants, schema ownership and append-only audit storage. Later persistence work items extend the same real-database suite for concurrency, package isolation, upgrade fixtures and backup metadata.

`API-002` adds a real two-writer PostgreSQL test that proves the second stale save fails and the first committed revision remains authoritative. A Server integration test separately verifies the HTTP 409 code and `currentRevision` extension, so storage and transport behavior cannot silently diverge.

`API-003` extends the migrated PostgreSQL suite with the real Identity schema and drives the production HTTP host. It verifies serialized one-time administrator setup, protected API fallback, secure cookie attributes, indistinguishable credential failures, account lockout, per-IP rate limiting, antiforgery-protected logout and configured session expiry.


`API-007` adds real PostgreSQL and production-host tests for queued state, user-scoped idempotency, immutable progress events, durable cancellation requests and safe failure causes. The suite proves that no creation response reports success and that a new HTTP request reads the same authoritative state.

### 2.5 API integration tests

`tests/JulOS.Integration.Tests` starts the real ASP.NET Core application in memory through `WebApplicationFactory`. It is deliberately not a web SDK project, so the architecture rule keeping `JulOS.Server` the only web project stays strict.

Start the real ASP.NET Core application with controlled dependencies:

- authentication and authorization
- anti-forgery behavior
- Problem Details responses
- idempotency
- revision conflicts
- package fault isolation
- Agent enrollment endpoints
- layout and window persistence
- operation resources

### 2.6 Contract tests

Every versioned contract has fixtures for:

- minimum valid message
- complete valid message
- unknown optional fields
- missing required fields
- unsupported major version
- stable serialization names

Package workers and Server run the same contract fixture set.

### 2.7 Desktop logic tests

Desktop tests are `*.test.ts` files next to the module they cover. `npm test` in `src/JulOS.Desktop` compiles them and runs the built-in Node test runner.

Pure TypeScript tests cover:

- window store commands
- snap geometry
- z-order
- viewport layout selection
- event deduplication
- stale-state calculations
- keyboard shortcut routing

### 2.8 End-to-end tests

Playwright tests cover critical journeys:

- initial administrator setup
- login and logout
- open, move, resize, snap and restore windows
- reload layout restoration
- install and enable a test package
- faulted package does not break desktop
- enroll a test Agent
- show and acknowledge a problem
- start and terminate a fake session
- switch English and German
- mobile task switching

Full protocol tests for RDP, VNC, SSH and Browser runtime use dedicated integration environments and do not run in every small unit-test job.

## 3. Package tests

Every official package includes:

- manifest validation tests
- worker startup and health tests
- permission tests
- configuration validation tests
- external API adapter tests using recorded safe fixtures or controlled test systems
- problem deduplication tests
- application and widget registration tests
- upgrade migration tests
- fault and timeout tests

A package cannot depend on another package being installed unless it declares a required capability and tests the unavailable-provider state.

## 4. Agent tests

- enrollment token consumption
- credential storage permissions
- reconnect and backoff
- heartbeat and offline detection
- capability allowlist
- malformed command rejection
- deadline and cancellation
- output-size limit
- path normalization
- Docker target scoping
- discovery range enforcement
- upgrade compatibility

Linux-specific collectors are tested using fixture data plus at least one real integration environment.

## 5. Runtime Manager tests

Runtime Manager is security-critical. Tests verify:

- only approved image digests
- mandatory ownership labels
- denial of privileged mode
- denial of arbitrary host mounts
- denial of host network unless an accepted profile explicitly allows it
- denial of unrelated container IDs
- resource limits applied
- cleanup after failed startup
- idempotent stop and remove
- authentication and request replay limits

## 6. Browser and Remote tests

### Browser

- persistent profile survives allowed restart
- temporary profile is deleted
- two users cannot share profile data
- internal DNS target loads
- denied destination fails clearly
- download policy works
- inactivity termination works
- resize updates display

### Remote

- session state transitions
- reconnect token expiry
- clipboard direction policies
- file-transfer permission
- keyboard and pointer input
- resize
- disconnect versus terminate
- runtime crash recovery
- protocol adapter timeout and safe diagnostics

## 7. Security tests

- authorization for every mutation
- cross-user layout and session isolation
- package schema access isolation
- secret values absent from APIs, logs and audit events
- anti-forgery rejection
- login rate limits
- unsafe redirect rejection
- Content Security Policy validation
- package signature rejection
- Agent revocation
- expired session token rejection
- path traversal rejection
- runtime allowlist enforcement

Security findings are not deferred behind feature completion when the affected feature is intended to merge.

## 8. Accessibility tests

Automated checks are supplemented by manual keyboard validation.

Required checks:

- shell usable without pointer
- visible focus
- accessible names for window controls
- taskbar and launcher semantics
- notifications announced without stealing focus
- 200% zoom
- reduced motion
- mobile touch targets
- color-independent problem severity

## 9. Performance tests

Measured scenarios:

- cold and warm desktop load
- five and ten simultaneous native windows
- window drag and resize frame rate
- 50 widgets with normal refresh intervals
- 1000 discovered applications in search
- 10,000 active and resolved problems with pagination
- Agent event burst
- package worker restart
- Browser and Remote runtime startup

Regressions against documented budgets require investigation before merge.

## 10. Resilience tests

- PostgreSQL unavailable during startup
- package worker unavailable
- Runtime Manager unavailable
- Agent disconnect during operation
- external API timeout
- Browser runtime exits unexpectedly
- duplicate event delivery
- Server restart during active session
- stale layout revision
- full package storage
- full Browser profile storage

Expected behavior is documented for each test. Failures must not become empty data or silent success.

## 11. Validation commands

The repository provides one top-level validation command for local and CI use:

```text
sh tools/validate.sh
```

Windows equivalent:

```text
pwsh tools/validate.ps1
```

Both files are thin wrappers around `tools/validate.mjs`, which holds the only implementation. They cannot drift apart because there is nothing in them to drift.

The command returns non-zero on failure and names the failed stage. `--list` prints the stages, `--stage <name>` runs a single stage.

Current stages:

| Stage | Checks |
|---|---|
| `policy` | encoding, line endings and final newline against decision `D012`, and that `.gitattributes` pins every extension the policy covers |
| `restore` | .NET dependency restore |
| `build` | .NET solution build |
| `dotnet-test` | unit and architecture tests |
| `desktop-install` | Desktop dependencies, skipped when already installed |
| `desktop-typecheck` | Desktop type checking |
| `desktop-test` | Desktop logic tests |
| `desktop-build` | Desktop production assets |
| `markdown-links` | relative Markdown links resolve |
| `package-manifests` | package manifest validation |
| `container-build` | Compose configuration and container image build |

A stage whose subject does not exist yet reports `skipped` with the reason and the work item that implements it. It never reports a pass it did not perform. `PKG-001` implements manifest validation.

`container-build` reports `skipped` when no container runtime answers, because a developer without one must still be able to validate everything else. Continuous integration always has a runtime, so the images are built there.

`node tools/normalize-encoding.mjs` corrects every file violation the `policy` stage reports.

The policy is checked against the working tree, and git decides what the working tree contains. An extension without an explicit `eol` attribute is checked out with the platform line ending, so the same commit would satisfy the policy on Windows and fail on Linux. The stage therefore verifies first that `.gitattributes` pins every extension the policy covers.

## 12. CI structure

`.github/workflows/validation.yml` runs `sh tools/validate.sh` on every pull request and every push to `main`. Continuous integration must not maintain its own list of checks: a new check belongs in `tools/validate.mjs`, which makes local and CI runs identical by construction rather than by review.

Only downloaded packages are cached. Build output, `node_modules` and generated assets are never restored, so a missing generated dependency fails the run instead of being served from an earlier one. The workflow ends with `git diff --exit-code`, which fails when validation modified a tracked file.

### Pull requests

- repository policy
- build
- unit and architecture tests
- desktop logic tests
- container build without push

Still to be added to the validation stages, with the work item that adds them:

- PostgreSQL integration tests: `API-001`
- package manifest validation: `PKG-001`
- selected end-to-end tests: `DESK-012`
- dependency and secret scan: `OPS-005`

### Main

- all pull-request checks
- version calculation
- versioned development image publication only when release policy permits

### Release tag

- full validation
- signed versioned images and package artifacts
- software bill of materials
- release notes
- migration notes
- deployment example validation

No `latest` tag is required for deployment. Versioned tags are authoritative.

## 13. Test data rules

- no real credentials
- no personal infrastructure addresses in committed fixtures
- deterministic generated identities
- time-dependent tests use an injected clock
- external API fixtures are sanitized
- large binary fixtures require documented purpose
- protocol test images are version-pinned

## 14. Code review checklist

Reviewers verify:

- correct issue scope
- root cause and architecture fit
- dependency direction
- no duplicate implementation
- package boundaries
- authorization and secret handling
- error behavior
- cancellation and timeout behavior
- database migration impact
- test coverage
- localization
- performance impact
- Markdown updates
- backlog update

Profile and preference integration coverage verifies:

- authenticated reads return persisted defaults and revisions
- English and German, valid IANA time zones and all documented theme and motion values round-trip
- unsupported locale and time-zone values return the stable validation code
- missing antiforgery tokens fail before mutation
- stale revisions return HTTP 409 with the authoritative revision and do not overwrite newer preferences

## 15. Definition of done

A work item is complete only when:

1. acceptance criteria are implemented
2. relevant tests pass locally and in CI
3. architecture rules remain enforced
4. permission and secret handling are reviewed
5. errors are visible and actionable
6. cancellation, timeout and retry behavior are defined
7. database and contract compatibility are documented
8. English and German user-facing text exists where applicable
9. operational and setup documentation is updated
10. `docs/BACKLOG.md` reflects the state
11. no workaround, dead code or unowned TODO remains
12. the commit can be safely reverted within documented migration limits

## 16. Bug policy

A bug fix includes:

- reproducible failing test or explicit reproduction fixture
- root-cause explanation
- correction at the owning layer
- regression test
- documentation update when user-visible behavior or operations change

Do not fix symptoms through special-case UI conditions when the invalid state originates in backend, persistence or contract logic.

## 17. Technical debt policy

Technical debt is accepted only when:

- it is not a workaround or correctness risk
- the intended replacement is known
- it has a GitHub issue with owner and acceptance criteria
- the current implementation remains understandable and supported

Comments such as `TODO`, `FIXME` and `HACK` are rejected unless they reference an active issue and explain why the code remains correct until that issue is completed.
