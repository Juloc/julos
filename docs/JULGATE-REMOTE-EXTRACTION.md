# Julgate Remote extraction map

## Status

This document completes the REM-002 inventory boundary for Julgate 0.7.8 on its `main` branch. It classifies verified responsibilities before shared transport extraction begins.

It does not authorize copying Julgate code. REM-003 must extract one shared implementation that remains consumable by both Julgate and JulOS Remote until migration is complete.

## Verified Julgate sources

| Source | Verified responsibility |
|---|---|
| `README.md` | Product scope, supported connection families, Guacamole/guacd topology and UI ownership. |
| `Matgate/Program.cs` | Cookie authentication, authorization, request policy, rate limiting, auditing, service composition and startup migration calls. |
| `Matgate/Models/ServerEndpoint.cs` | One combined model for Remote, Files and Browser targets, protocol options and stored connection fields. |
| `Matgate/Services/JsonDataStore.cs` | Shared JSON persistence for users, server definitions and workspaces. |
| `Matgate/Services/CredentialProtector.cs` | Julgate-specific storage encryption, key rotation and legacy Matgate value conversion. |
| `Matgate/Services/GuacamoleLauncher.cs` | Target and secret resolution, concrete option translation, launch lifetime, session identity and Guacamole JSON-auth payload creation. |
| `Matgate/Services/GuacamoleConfigWriter.cs` | Removal of persistent plaintext Guacamole file-auth configuration. |
| `docker-compose-simple.yaml` | Edge, application, Guacamole and guacd services; private backend, target egress and drive volume. |

Unverified behavior remains an explicit later investigation. It must not be presented as implemented JulOS behavior.

## Steady-state ownership

```text
JulOS Core        generic contracts, caller policy, grants, audit and deadlines
Remote package    concrete protocols, profiles, provider selection and error mapping
Runtime Manager   allowlisted runtime lifecycle, private networks, limits and cleanup
Shared transport  reusable display/input/session behavior consumed by Julgate and JulOS
Provider runtime  concrete daemon/library integration and target connection
Desktop           windows, focus, layout and user interaction
Migration tool    temporary import and compatibility behavior only
```

Core must not reference Julgate, Guacamole, guacd or concrete protocol types.

## Responsibility map

| Julgate responsibility | JulOS destination | Decision |
|---|---|---|
| Login and session cookie | Existing JulOS authentication | Do not import Julgate authentication. |
| Administrator and target permissions | JulOS authorization, capability grants and audit | Import only supported target ownership/access mappings. |
| Users, servers and workspaces JSON | Existing JulOS persistence and package-isolated storage | Never use Julgate JSON as a live JulOS backend. |
| Combined Remote/File/Website profile | Separate Remote, Files and Browser profiles | Split records during an explicit import. |
| Stored value encryption | JulOS secret references | Julgate decryption exists only in a temporary offline importer. |
| Legacy Matgate value conversion | Migration tool | Remove after the documented compatibility window. |
| Concrete protocol identities and default ports | Remote package | Core receives only a bounded package-defined identifier. |
| Concrete option translation | Shared/provider transport layer | Preserve behavior with parity tests; keep concrete names outside Core. |
| Secret resolution for a connection | Authorized Remote provider boundary | Never place secret material in REM-001, Desktop state or browser payloads. |
| Guacamole JSON-auth generation | Shared Guacamole adapter if retained | Keep entirely server-side and runtime-private. |
| Persistent Guacamole mapping cleanup | Installation and migration validation | Persistent plaintext mappings remain prohibited. |
| Guacamole web application | Remote provider runtime | Not a Core or Desktop dependency. |
| guacd | Runtime Manager-owned private service | Backend and approved target egress only; never public. |
| `/guacamole` reverse proxy | JulOS same-origin display endpoint | Replace provider URLs with a short-lived authenticated display descriptor. |
| Target egress | Runtime Manager network profile | Attach only the selected approved network. |
| Internal backend network | Runtime Manager private runtime network | Package frontend cannot access guacd directly. |
| Drive redirection | Remote adapter plus optional Files capability | Remote works without Files and reports disabled file features explicitly. |
| Tabs and session restore | JulOS Desktop and Remote frontend | Do not migrate Julgate UI code. |
| Durable session lifecycle | Remote session service | Map provider state to REM-001 and keep provider processes subordinate. |
| Request/session audit | JulOS audit | Record stable operations and caller-safe outcomes. |
| SFTP, FTP and SMB | Files package | Excluded from Remote. |
| Website profiles/proxy | Browser package | Excluded from Remote. |
| Workspaces/shared files | Later explicit product decision | No automatic REM-002 destination. |
| Network tools/archive extraction | None | Explicitly rejected from Remote scope. |

## Shared transport extraction boundary

REM-003 may extract only behavior that both Julgate and JulOS require, such as:

- connection/session adapter interfaces
- display and input transport
- resize and reconnect mechanics
- clipboard mechanics
- concrete provider option mapping
- caller-safe diagnostic translation

The extraction must follow this sequence:

1. add behavior tests around the current Julgate implementation;
2. move one coherent component without changing behavior;
3. make Julgate consume the extracted component;
4. verify Julgate remains deployable;
5. make JulOS Remote consume the same component;
6. delete the old implementation location;
7. prove there is no duplicated source path.

Authentication, JSON persistence, Julgate UI, workspaces, file gateway and website proxy are not shared transport components.

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

REM-004 owns session/runtime orchestration. REM-005 owns the display client. Provider-specific transport work begins in REM-006.

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
2. REM-002 — Julgate inventory and extraction boundaries — this document
3. REM-003 — extract the shared transport implementation consumed by Julgate and JulOS Remote
4. REM-004 — implement Remote worker and session orchestration
5. REM-005 — implement the Remote display client
6. REM-006 — integrate RDP
7. REM-007 — integrate VNC
8. REM-008 — integrate SSH
9. REL-005 — controlled migration, parity acceptance and Julgate retirement

No later item may reintroduce a Core dependency on product or protocol implementation types.

## REM-002 completion criteria

REM-002 is complete when:

- this inventory is linked from the documentation map and migration specification;
- each verified responsibility has one destination or explicit rejection;
- shared transport, steady-state architecture and migration-only behavior are separated;
- Remote, Files and Browser ownership is unambiguous;
- the backlog names REM-003 as the next item;
- the full repository validator passes and leaves the tree unchanged.
