# Julgate migration to JulOS Remote

## 1. Goal and current status

Move reusable Julgate remote-session behavior into shared transport components and JulOS packages without copying implementations, breaking Julgate early or preserving two permanent code paths.

Current status:

- REM-001 protocol-neutral session contracts: implemented
- REM-002 Julgate inventory and extraction boundaries: implemented in [`JULGATE-REMOTE-EXTRACTION.md`](JULGATE-REMOTE-EXTRACTION.md)
- REM-003 shared transport extraction: complete
- `JulOS.Remote.Transport` `0.1.0`: published immutably with digests and attestations
- JulOS Remote and Julgate: consuming the same implementation
- REM-004 worker and lifecycle integration: next
- Julgate retirement: prohibited until parity and migration acceptance

Final target:

```text
JulOS Remote package
├─ concrete protocol definitions and profiles
├─ shared transport adapters
├─ Remote worker and session orchestration
├─ display and input client
├─ clipboard and resize policy
├─ optional Files integration
└─ caller-safe session diagnostics

Runtime Manager
├─ allowlisted runtime lifecycle
├─ private backend networks
├─ approved target egress
├─ resource limits and health
└─ cleanup
```

## 2. Migration principles

- inventory before extraction
- one implementation of reusable behavior
- no Julgate, Guacamole or concrete protocol dependency in JulOS Core
- no Julgate UI copied into JulOS
- no permanent compatibility wrapper
- every extraction has behavior tests
- Julgate remains deployable while it is a rollback path
- security behavior is preserved or improved
- temporary import code is isolated and removable
- unsupported behavior is explicit

## 3. J0 inventory — REM-002

The completed inventory is maintained in [`JULGATE-REMOTE-EXTRACTION.md`](JULGATE-REMOTE-EXTRACTION.md).

It records, from verified Julgate source files:

- current responsibility
- current owner
- JulOS destination
- migration action
- steady-state decision
- known parity risk

The inventory is stored in JulOS because it defines JulOS boundaries. Julgate source paths remain referenced as evidence. No production Julgate component may be extracted unless it is classified there first.

Acceptance status:

- verified Remote responsibilities are classified
- Files and Browser behavior is separated from Remote
- migration-only behavior is separated from steady state
- known Android keyboard, drive-redirection and connection risks are recorded

## 4. J1 contracts — REM-001

The implemented contract is documented in [`REMOTE-SESSION-CONTRACT.md`](REMOTE-SESSION-CONTRACT.md).

Core concepts are:

- create, read, list and cancel operations
- bounded package-defined protocol identity
- target and secret references
- optional profile and network-profile references
- viewport and duration policy
- exact idempotency identity
- stable lifecycle states
- caller-safe failures
- same-origin display descriptor

The stable lifecycle is:

```text
requested → provisioning → connecting → connected → disconnecting → disconnected
     ├──────────────→ cancelled
     ├──────────────→ expired
     └──────────────→ failed
```

Terminal states do not resume. Reconnect policy is implemented later without changing these contracts silently.

## 5. J2 shared transport extraction — REM-003

REM-003 is complete.

The reusable Guacamole transport boundary is implemented once in the JulOS monorepo as `JulOS.Remote.Transport`. It owns:

- concrete supported-protocol identifiers used by the adapter;
- Guacamole connection-parameter translation;
- JSON-auth payload serialization;
- client identifier construction;
- HMAC-SHA256 signing;
- Guacamole-required AES-CBC encryption;
- bounded input validation and temporary-buffer clearing.

It does not own Julgate authentication, JSON persistence, authorization, target storage, workspaces, file gateway, website proxy, UI or launch-URL policy.

Completed sequence:

1. behavior tests were added around payload, protocol and cryptographic compatibility;
2. the dependency boundary was isolated outside Core and Contracts;
3. JulOS Remote began consuming the shared project directly;
4. immutable NuGet package `JulOS.Remote.Transport` `0.1.0` was built and published;
5. package, symbol and checksum artifacts were recorded with SHA-256 and GitHub attestations;
6. a separate verification probe restored the exact package and verified evidence and provenance;
7. Julgate was migrated to the exact published package;
8. Julgate's original payload, signing and encryption implementation was removed;
9. Julgate's complete build, test, container, security, E2E, protocol and operations gates passed;
10. no duplicate or fallback implementation remains.

Acceptance status:

- no copied source tree: passed
- Julgate and JulOS consume one implementation: passed
- Julgate remains deployable: passed
- concrete dependencies remain outside Core and Contracts: passed
- affected repository validators are green: passed
- package version is immutable and attested: passed

## 6. J3 worker and lifecycle integration — REM-004

Implement:

- capability-broker session creation
- provider and target authorization
- Runtime Manager allocation
- durable REM-001 lifecycle state
- operation and lifecycle events
- inactivity and maximum-duration enforcement
- reconnect policy
- explicit cancellation and termination behavior
- runtime cleanup
- problem creation after runtime failure

Acceptance:

- window detach follows explicit policy and does not silently terminate the session
- repeated create operations preserve exact idempotency
- expired display grants cannot reconnect
- runtime failure produces a caller-safe failure and an operator-visible problem
- no provider launch material is returned to package JavaScript
- provider runtimes are allowlisted, bounded and cleaned up after every terminal state

## 7. J4 display client — REM-005

Implement the Remote client surface for:

- display connection
- resize with debouncing
- mouse and pointer input
- keyboard capture and release
- mobile software keyboard
- full-screen behavior
- connection and reconnect status
- accessible input-capture indication

Acceptance:

- keyboard escape behavior is documented and tested
- resize does not create a reconnect storm
- no provider launch material is visible to package JavaScript
- the Android duplicate-character regression has an automated test

## 8. J5 protocol parity — REM-006 through REM-008

### REM-006 — RDP

Verify and implement:

- username, password and domain handling through secret references
- security mode and certificate policy
- display resize
- keyboard layout
- mobile keyboard input
- clipboard directions
- audio policy
- drive redirection
- reconnect
- caller-safe error mapping

Acceptance includes a duplicate-key regression test and distinguishable authentication/account errors when the upstream provider permits it.

### REM-007 — VNC

Verify and implement:

- authentication
- pixel format and scaling
- supported resize behavior
- clipboard
- reconnect
- caller-safe error mapping

### REM-008 — SSH

Verify and implement:

- password and key authentication through secret references
- host-key policy
- terminal resize
- clipboard policy
- session termination
- caller-safe error mapping

### Console providers

Proxmox and later console integrations request the generic Remote capability through provider adapters. They do not introduce protocol assumptions into Core.

## 9. Browser integration

The Browser package uses Remote transport for isolated Chromium after the Browser work items create the runtime and profile model.

Rules:

- Browser does not know guacd internals
- each browser session is isolated
- the display endpoint stays on the private runtime network
- temporary profiles are removed on cleanup
- downloads and clipboard follow explicit capability policy

## 10. Files integration

Rules:

- Remote owns protocol file-redirection mechanics
- Files owns provider browsing, transfer storage and user-visible transfer queue
- authorization is checked for both remote transfer and file scope
- secret references are not duplicated between packages
- Remote remains usable when Files is absent, with file behavior disabled explicitly

SFTP, FTP and SMB browsing from Julgate is migrated under the Files workstream, not REM-003.

## 11. Operational migration — REL-005

Required operator flow:

1. back up Julgate data and key material offline;
2. install JulOS Remote without disabling Julgate;
3. invoke the explicit migration command;
4. review classification and unsupported-field report;
5. create package-specific profiles and JulOS secret references;
6. test representative RDP, VNC and SSH sessions;
7. compare lifecycle, input, resize, clipboard and failures;
8. move launch links to JulOS;
9. keep Julgate available for the defined rollback window;
10. stop Julgate only after acceptance;
11. archive Julgate only after the final backup and release notes.

The importer must be idempotent and produce an operation-scoped rollback report. Recovered secret values are never returned through a browser, terminal output or log.

## 12. Parity matrix

| Capability | Julgate evidence | Shared transport | JulOS Remote | Tests | Accepted |
|---|---:|---:|---:|---:|---:|
| RDP payload and option mapping | Yes | Implemented | Consumed | Shared and Julgate parity tests | Yes |
| VNC payload and option mapping | Yes | Implemented | Consumed | Shared and Julgate parity tests | Yes |
| SSH payload and option mapping | Yes | Implemented | Consumed | Shared behavior tests | Transport only |
| Payload signing and encryption | Yes | Implemented once | Consumed | Known vectors, digest and attestation evidence | Yes |
| RDP live connect | Yes | Foundation ready | Planned | Planned | No |
| RDP resize | Yes | Not in REM-003 | Planned | Planned | No |
| Mobile keyboard | Defect risk | Not in REM-003 | Planned | Planned | No |
| VNC live connect | Yes | Foundation ready | Planned | Planned | No |
| SSH live connect | Yes | Foundation ready | Planned | Planned | No |
| Clipboard | Runtime review required | Not in REM-003 | Planned | Planned | No |
| Drive redirection | Permission risk | Mapping implemented | Planned | Julgate payload parity only | No |
| Reconnect | Runtime review required | Not in REM-003 | Planned | Planned | No |
| Diagnostics | Runtime review required | Not in REM-003 | Planned | Planned | No |

Transport acceptance does not imply live-session parity. Every remaining row must be updated from repository and runtime evidence. A package shell, mocked success response or undocumented manual test does not count as acceptance.

## 13. Archive criteria

Julgate may be archived only when:

- all supported production connection types pass JulOS tests
- connection migration is documented and tested
- active users no longer require the Julgate UI
- JulOS Remote has a stable release
- the rollback window has ended
- open Julgate issues are migrated, resolved or explicitly closed
- security, backup and restore reviews are complete
- the Julgate README points to JulOS Remote
- final Julgate release notes identify the successor

## 14. Prohibited shortcuts

- copying Julgate source and modifying both copies
- embedding Julgate UI in an iframe
- exposing guacd directly to a public client
- placing product or protocol types in JulOS Core
- migrating secrets through plaintext browser or command output
- archiving Julgate before parity
- silently falling back to Julgate for unsupported behavior
- keeping an indefinite switch between old and new implementations
- using Julgate JSON as a live JulOS backend
- presenting a package shell as a working Remote implementation
