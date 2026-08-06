# Julgate Remote extraction map

## Status

This document completed the REM-002 inventory boundary for Julgate 0.7.8 and now records the accepted REM-003 extraction result.

REM-003 is complete. One shared implementation, `JulOS.Remote.Transport`, is maintained in the JulOS monorepo, published as immutable package `0.1.0`, consumed directly by JulOS Remote and consumed as the exact package by Julgate.

No Julgate code was copied into a second permanent source tree. Julgate's original Guacamole payload, HMAC and AES implementation was removed after package adoption and full repository validation.

## Verified Julgate sources

| Source | Verified responsibility |
|---|---|
| `README.md` | Product scope, supported connection families, Guacamole/guacd topology and UI ownership. |
| `Matgate/Program.cs` | Cookie authentication, authorization, request policy, rate limiting, auditing, service composition and startup migration calls. |
| `Matgate/Models/ServerEndpoint.cs` | One combined model for Remote, Files and Browser targets, protocol options and stored connection fields. |
| `Matgate/Services/JsonDataStore.cs` | Shared JSON persistence for users, server definitions and workspaces. |
| `Matgate/Services/CredentialProtector.cs` | Julgate-specific storage encryption, key rotation and legacy Matgate value conversion. |
| `Matgate/Services/GuacamoleLauncher.cs` | Julgate-specific target resolution, launch lifetime, session identity and URL construction; shared payload/crypto behavior is now delegated to `JulOS.Remote.Transport`. |
| `Matgate/Services/GuacamoleConfigWriter.cs` | Removal of persistent plaintext Guacamole file-auth configuration and stable connection naming. |
| `docker-compose-simple.yaml` | Edge, application, Guacamole and guacd services; private backend, target egress and drive volume. |

Unverified behavior remains an explicit later investigation. It must not be presented as implemented JulOS behavior.

## Steady-state ownership

```text
JulOS Core        generic contracts, caller policy, grants, audit and deadlines
Remote package    concrete protocols, profiles, provider selection and error mapping
Runtime Manager   allowlisted runtime lifecycle, private networks, limits and cleanup
Shared transport  reusable provider translation and launch encoding consumed by Julgate and JulOS
Provider runtime  concrete daemon/library integration and target connection
Desktop           windows, focus, layout and user interaction
Migration tool    temporary import and compatibility behavior only
```

Core does not reference Julgate, Guacamole, guacd or concrete protocol types.

## Responsibility map

| Julgate responsibility | JulOS destination | Decision |
|---|---|---|
| Login and session cookie | Existing JulOS authentication | Do not import Julgate authentication. |
| Administrator and target permissions | JulOS authorization, capability grants and audit | Import only supported target ownership/access mappings. |
| Users, servers and workspaces JSON | Existing JulOS persistence and package-isolated storage | Never use Julgate JSON as a live JulOS backend. |
| Combined Remote/File/Website profile | Separate Remote, Files and Browser profiles | Split records during an explicit import. |
| Stored value encryption | JulOS secret references | Julgate decryption exists only in a temporary offline importer. |
| Legacy Matgate value conversion | Migration tool | Remove after the documented compatibility window. |
| Concrete protocol identities and default ports | Remote package and shared transport adapter | Core receives only a bounded package-defined identifier. |
| Concrete option translation | `JulOS.Remote.Transport` | Implemented once with behavior and Julgate parity tests. |
| Secret resolution for a connection | Authorized Remote provider boundary | Never place secret material in REM-001, Desktop state or browser payloads. |
| Guacamole JSON-auth generation | `JulOS.Remote.Transport` | Implemented entirely server-side and runtime-private. |
| HMAC signing and Guacamole-required encryption | `JulOS.Remote.Transport` | Implemented once, tested with known vectors and published with provenance. |
| Persistent Guacamole mapping cleanup | Installation and migration validation | Persistent plaintext mappings remain prohibited. |
| Guacamole web application | Remote provider runtime | Not a Core or Desktop dependency. |
| guacd | Runtime Manager-owned private service | Backend and approved target egress only; never public. |
| `/guacamole` reverse proxy | JulOS same-origin display endpoint | Replace provider URLs with a short-lived authenticated display descriptor. |
| Target egress | Runtime Manager network profile | Attach only the selected approved network. |
| Internal backend network | Runtime Manager private runtime network | Package frontend cannot access guacd directly. |
| Drive redirection | Remote adapter plus optional Files capability | Mapping is shared; live parity remains REM-006/Files work. |
| Tabs and session restore | JulOS Desktop and Remote frontend | Do not migrate Julgate UI code. |
| Durable session lifecycle | Remote session service | Map provider state to REM-001 and keep provider processes subordinate. |
| Request/session audit | JulOS audit | Record stable operations and caller-safe outcomes. |
| SFTP, FTP and SMB | Files package | Excluded from Remote. |
| Website profiles/proxy | Browser package | Excluded from Remote. |
| Workspaces/shared files | Later explicit product decision | No automatic REM-002 destination. |
| Network tools/archive extraction | None | Explicitly rejected from Remote scope. |

## Accepted shared transport boundary

`JulOS.Remote.Transport` owns the coherent reusable boundary accepted in REM-003:

- supported concrete transport identifiers used by the adapter;
- Guacamole connection parameter construction;
- RDP, VNC and SSH option translation included in the extracted contract;
- JSON-auth payload serialization;
- client identifier construction;
- HMAC-SHA256 signing;
- Guacamole-required AES-CBC encoding;
- input validation and temporary sensitive-buffer clearing.

It deliberately does not own:

- authentication or authorization;
- Julgate JSON persistence;
- target/profile persistence;
- secret storage or retrieval;
- Runtime Manager lifecycle;
- display grants or public endpoints;
- Julgate UI, workspaces, file gateway or website proxy;
- live reconnect, clipboard, resize or input transport.

Those remaining runtime responsibilities belong to REM-004 through REM-008.

## REM-003 completion evidence

- packable library and behavior tests are in the JulOS monorepo;
- JulOS Remote consumes the project directly;
- NuGet package `JulOS.Remote.Transport` `0.1.0` and symbol package were generated by the standard validator;
- publication requires complete validation, exact package identity, SHA-256 evidence and GitHub attestations;
- the package cannot overwrite an existing version;
- a separate read-only probe verified the successful publication run, downloaded the evidence bundle, checked hashes and attestations, and restored the exact package;
- Julgate consumes only package version `0.1.0` through package source mapping;
- Julgate Docker builds pass the repository token only as a BuildKit secret;
- Julgate's former local payload, signing and encryption implementation no longer exists;
- Julgate passed unit, parity, protocol integration, Compose, container, migration, Playwright, Trivy, CodeQL, NuGet-audit and backup/restore gates.

## Display authorization boundary

Julgate currently places encrypted Guacamole launch data in a client URL. This is not the JulOS public contract.

JulOS must instead:

1. authorize a REM-001 session request;
2. start an allowlisted provider runtime through Runtime Manager;
3. issue a short-lived display grant bound to caller, package, session, runtime and expiry;
4. return only a same-origin relative display descriptor;
5. validate the JulOS session and grant on display connection;
6. keep provider launch/tunnel data server-side;
7. invalidate the grant on expiry, cancellation or termination.

REM-004 owns session/runtime orchestration. REM-005 owns the display client. Provider-specific live transport work begins in REM-006.

## Migration-only boundary

A future explicit migration command may read Julgate `servers.json` offline. It must:

- require operator invocation and a verified backup;
- classify each record as Remote, Files, Browser or unsupported;
- create package-specific profiles;
- create JulOS secret references without displaying or logging recovered values;
- report unsupported fields before writing;
- be idempotent and create a migration report;
- support rollback of only the objects created by that operation.

JulOS does not read Julgate files during normal operation.

## Known parity risks

These are acceptance risks, not implemented JulOS claims:

- user-reported duplicate characters from Android software-keyboard input;
- deployment permission failures around drive redirection;
- RDP authentication and security-mode combinations;
- reconnect, clipboard, resize, mobile input and diagnostic parity.

Each risk requires a reproducible test or an explicit unsupported decision before Julgate retirement.

## Work-breakdown sequence

1. REM-001 — protocol-neutral session contracts — complete
2. REM-002 — Julgate inventory and extraction boundaries — complete
3. REM-003 — shared transport implementation consumed by Julgate and JulOS Remote — complete
4. REM-004 — Remote worker and session orchestration — next
5. REM-005 — Remote display client
6. REM-006 — RDP live integration
7. REM-007 — VNC live integration
8. REM-008 — SSH live integration
9. REL-005 — controlled migration, parity acceptance and Julgate retirement

No later item may reintroduce a Core dependency on product or protocol implementation types or create a second transport implementation.

## REM-003 completion criteria

REM-003 is complete because:

- one versioned implementation exists;
- JulOS Remote and Julgate consume it;
- behavior and parity tests cover the extracted contract;
- immutable publication, digests and provenance are verified;
- both repositories validate successfully;
- the original Julgate implementation was removed;
- documentation and backlog identify REM-004 as the next item.
