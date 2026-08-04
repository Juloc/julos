# Julgate Remote extraction map

## 1. Status and purpose

This document completes the REM-002 inventory boundary for the Julgate 0.7.8 implementation on its `main` branch.

It classifies verified Julgate responsibilities before any JulOS transport implementation begins. It is not a source-move plan and does not authorize copying Julgate code into JulOS.

The authoritative steady-state rule is:

```text
JulOS Core owns generic platform contracts and policy.
Official packages own product and protocol behavior.
Runtime Manager owns isolated process and container lifecycle.
Desktop owns windows and user interaction.
Migration code is temporary and removable.
```

## 2. Verified source inventory

The following Julgate files establish the current responsibilities used by this map:

| Source | Verified responsibility |
|---|---|
| `README.md` | Product scope, supported connection families, Guacamole/guacd topology, authentication and UI ownership. |
| `Matgate/Program.cs` | Cookie authentication, authorization, request policy, rate limiting, audit integration, service composition and startup migration calls. |
| `Matgate/Models/ServerEndpoint.cs` | Combined Remote, Files and Browser profile model including target, protocol, display options and stored connection fields. |
| `Matgate/Services/JsonDataStore.cs` | Shared JSON persistence for users, server definitions and workspaces plus startup rewriting of protected stored values. |
| `Matgate/Services/CredentialProtector.cs` | Julgate-specific AES-GCM storage format, key rotation and legacy Matgate value migration. |
| `Matgate/Services/GuacamoleLauncher.cs` | Target and secret resolution, protocol-option translation, launch lifetime, session identity and encrypted Guacamole JSON-auth payload generation. |
| `Matgate/Services/GuacamoleConfigWriter.cs` | Removal of persistent plaintext Guacamole file-auth configuration and mapping of profile protocol names. |
| `docker-compose-simple.yaml` | Edge, application, Guacamole and guacd services; backend and egress network ownership; drive volume; published edge boundary. |

Anything not established by these sources remains a later implementation investigation rather than an accepted fact.

## 3. Responsibility matrix

| Julgate responsibility | Current owner | JulOS destination | Migration action | Steady-state decision |
|---|---|---|---|---|
| User login and session cookie | Julgate application | JulOS Server authentication | None | Reuse JulOS authentication. Do not import the Julgate authentication subsystem. |
| Administrator and server permissions | Julgate application and JSON user records | JulOS authorization and capability grants | Map only connection ownership and access during import | Reuse JulOS policy, audit and package grants. |
| User, server and workspace JSON files | `JsonDataStore` | Existing JulOS persistence plus package-isolated storage | One-time importer reads supported connection records | Do not retain Julgate JSON as a live backend. |
| Combined Remote/File/Website profile | `ServerEndpoint` | Remote profile, Files provider profile and Browser profile | Split each imported record by connection family | No combined cross-package profile model. |
| Stored connection protection | `CredentialProtector` | JulOS secret references | Temporary offline importer decrypts only when explicitly supplied the matching legacy key, then writes a JulOS secret | Do not copy Julgate encryption, prefixes or key rotation into steady state. |
| Legacy Matgate secret conversion | `CredentialProtector` | Migration tool only | Supported only inside an explicit migration command | Delete migration support after the documented compatibility window. |
| Concrete protocol identities and default ports | `ServerEndpoint` | `JulOS.Remote` package | Recreate as package-owned definitions | Core receives only a generic protocol identifier. |
| Target and protocol-option translation | `GuacamoleLauncher` | Remote provider adapter | Reimplement against REM-001 and verified parity tests | Provider owns concrete parameter names and defaults. |
| Resolving secret material for launch | `GuacamoleLauncher` | Remote provider worker through an authorized secret-resolution boundary | None beyond imported secret references | Secret material never enters REM-001, Desktop state or browser-visible payloads. |
| Guacamole JSON-auth payload | `GuacamoleLauncher` | Remote provider internal adapter when Guacamole is selected | Reimplement only inside the isolated provider runtime | Never expose provider launch data through Core contracts. |
| Persistent Guacamole mapping cleanup | `GuacamoleConfigWriter` | Migration/installation validation | Verify legacy files are absent before provider start | No persistent plaintext connection mapping. |
| Guacamole web application | Compose `guacamole` service | Remote display provider runtime | Package-owned runtime image if retained | Not a Core or Desktop dependency. |
| guacd daemon | Compose `guacd` service | Runtime Manager-owned Remote runtime | Package-owned private service/container | Backend and target egress only; never publicly exposed. |
| Reverse proxy `/guacamole` route | Compose `edge` service | JulOS same-origin Remote display endpoint | Replace rather than import | Browser receives a short-lived authenticated descriptor, not a provider URL containing launch material. |
| Remote target egress | Julgate and guacd egress network | Runtime Manager network profile | Map target/network selection explicitly | Provider runtime receives only the approved network attachment. |
| Guacamole backend network | Compose internal backend | Runtime Manager private runtime network | Recreate per runtime policy | No direct public or package-frontend access to guacd. |
| RDP drive volume | Compose `drives` volume and launch parameters | Remote redirection adapter plus optional Files capability | Reimplement after explicit file-redirection policy | Remote remains usable without Files; file features degrade explicitly. |
| Connection tabs and window restore | Julgate PWA | JulOS Desktop and Remote frontend | No source migration | Use JulOS windows, session references and layout persistence. |
| Remote session lifecycle | Guacamole launch plus client state | Remote session service and provider runtime | Map observed states to REM-001 | Durable state belongs to JulOS; provider process state is subordinate. |
| Session audit | Julgate request audit | JulOS capability and operation audit | No audit-log import required for runtime parity | Emit stable operation and lifecycle events without provider exception text. |
| SFTP, FTP and SMB browsing | Julgate file gateway | JulOS Files package | Separate later Files migration | Excluded from Remote. |
| Website profile and proxy | `ServerEndpoint` and website service | JulOS Browser package | Separate later Browser migration | Excluded from Remote. |
| Workspaces and shared files | Julgate workspace service | No automatic REM-002 destination | Explicit later product decision | Not imported as Remote functionality. |
| Network tools and archive extraction | Julgate optional tools | No Remote destination | Explicit rejection for REM-002 | Must not enter the Remote package. |

## 4. Required JulOS boundaries

### 4.1 Core

Core owns:

- `remote.session` capability identity and versioned operations
- protocol-neutral request, session, display, state and failure contracts
- caller identity, authorization, operation deadlines and audit
- references to secrets and network profiles

Core must not own:

- concrete protocol names or ports
- Guacamole parameter names
- guacd process details
- target credentials
- display implementation details
- Julgate storage or migration formats

### 4.2 Remote package

The Remote package owns:

- concrete supported protocol definitions
- profile validation and protocol-specific options
- provider selection
- mapping REM-001 requests to provider commands
- caller-safe error mapping
- package UI and session status presentation

The package does not receive unrestricted Core storage access or a general-purpose secret API.

### 4.3 Runtime Manager

Runtime Manager owns:

- creating and terminating allowlisted Remote runtimes
- private backend networks
- approved target egress attachment
- resource limits, health, timeouts and cleanup
- runtime identity used by the display authorization boundary

It does not understand concrete Remote protocol parameters.

### 4.4 Provider runtime

A provider runtime owns:

- concrete transport libraries and daemons
- protocol parameter translation
- endpoint trust and authentication exchange
- display/input/clipboard protocol handling
- provider diagnostics mapped to stable JulOS failure codes

Any use of Guacamole or guacd remains fully inside this boundary.

### 4.5 Desktop and browser client

Desktop owns windows, tabs, layout, focus and user gestures. The package frontend owns Remote-specific controls.

The client receives only:

- the REM-001 session snapshot
- a short-lived same-origin display descriptor
- caller-safe status and failure information

It must not receive stored target secrets, a Guacamole JSON-auth payload or a direct guacd endpoint.

## 5. Display authorization decision

Julgate currently creates an encrypted launch value and places it in a Guacamole client URL. JulOS does not expose this pattern as its public contract.

The JulOS display flow must be:

1. the authenticated caller creates a REM-001 session;
2. the Remote provider runtime starts behind Runtime Manager;
3. the server creates a short-lived display grant bound to caller, package, session, runtime and expiry;
4. Core returns a same-origin relative display descriptor without secret material;
5. the display endpoint validates the active JulOS session and grant on connection;
6. provider-specific launch or tunnel data remains server-side;
7. expiry, cancellation or session termination invalidates the grant.

The final display transport implementation belongs to REM-004 and later work, not REM-002.

## 6. Profile and secret migration boundary

A future migration command may read Julgate `servers.json` offline. It must:

- require explicit operator invocation and an offline backup
- classify every record as Remote, Files, Browser or unsupported
- create package-specific profiles instead of one combined record
- create JulOS secret references for supported secret fields
- never return decrypted values to a browser or log
- report unsupported fields before writing
- be idempotent and produce a migration report
- support rollback by deleting only objects created by that migration operation

Julgate remains the rollback source until the later migration acceptance phase. JulOS does not read Julgate files during normal operation.

## 7. Known parity risks

The following are acceptance risks, not REM-002 implementation claims:

- Android software-keyboard input has a user-reported duplicate-character defect in the current Julgate experience.
- RDP drive redirection has had deployment-permission failures.
- RDP authentication and security-mode combinations require parity tests.
- reconnect, clipboard, resize, mobile input and caller-safe diagnostics require source and runtime verification.

Each risk must receive a reproducible test or an explicit unsupported decision before Julgate retirement.

## 8. Extraction sequence

The approved dependency order is:

1. REM-001 protocol-neutral contracts — complete
2. REM-002 Julgate inventory and extraction map — this document
3. REM-003 Runtime Manager Remote orchestration
4. REM-004 display transport and authorization
5. REM-005 concrete desktop transport
6. REM-006 concrete graphical fallback transport
7. REM-007 concrete terminal transport
8. REM-008 Remote package UI, lifecycle and parity completion
9. REL-005 controlled Julgate migration and retirement

No later item may reintroduce a Core dependency on Julgate, Guacamole or concrete protocol types.

## 9. REM-002 completion criteria

REM-002 is complete when:

- this source inventory is present and linked from the documentation map
- every verified responsibility has a destination or explicit rejection
- migration-only code is separated from steady-state architecture
- Remote, Files and Browser ownership is unambiguous
- `docs/BACKLOG.md` names REM-003 as the next implementation item
- the full repository validator passes and leaves the tree unchanged
