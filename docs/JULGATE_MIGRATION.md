# Julgate migration to JulOS Remote

## 1. Goal

Move reusable Julgate remote-session capabilities into JulOS packages without copying implementations, breaking the existing product early or preserving two permanent code paths.

Final target:

```text
JulOS.Remote
├─ protocol-neutral session contracts
├─ guacd adapter
├─ RDP adapter
├─ VNC adapter
├─ SSH adapter
├─ display and input transport
├─ clipboard policy
├─ file redirection integration
└─ session diagnostics
```

Julgate is archived only after parity, migration and operational acceptance are complete.

## 2. Migration principles

- inventory before extraction
- preserve one implementation of reusable logic
- no direct dependency from JulOS Core to Guacamole or protocol types
- no old Julgate UI copied into JulOS
- no compatibility wrapper kept after all callers migrate
- each extraction has tests or a documented integration harness
- Julgate remains deployable during the transition
- security behavior is preserved or improved, never silently relaxed

## 3. Phase J0 — Inventory

Create a component inventory in the Julgate repository:

- connection model
- credential handling
- guacd communication
- websocket or tunnel transport
- display rendering client
- keyboard and mouse input
- clipboard
- drive and file redirection
- session lifecycle
- reconnect behavior
- authorization
- logging and diagnostics
- deployment and runtime containers
- tests

For every component record:

```text
Location
Current responsibility
Dependencies
Protocol-specific or reusable
Test coverage
Known defects
Target JulOS component
Migration action
```

Acceptance:

- no production component is unclassified
- known Android keyboard duplication and other active defects are explicitly recorded
- secrets and authentication paths are identified

## 4. Phase J1 — Define JulOS Remote contracts

Create protocol-neutral contracts before moving implementation.

Required concepts:

- RemoteConnectionDefinition
- RemoteSessionRequest
- RemoteSessionReference
- RemoteDisplayDescriptor
- RemoteInputEvent
- ClipboardTransfer
- FileTransferRequest
- SessionPolicy
- SessionState
- RemoteFailure

Contracts must not contain Guacamole connection parameter names unless inside a protocol adapter-owned payload.

Acceptance:

- Browser runtime can use the same session reference model
- Proxmox console can request a Remote capability without referencing Guacamole
- disconnect, suspend and terminate have distinct semantics

## 5. Phase J2 — Extract transport libraries

Move reusable backend and client transport code into neutral libraries in the JulOS monorepo or a shared package location chosen by an accepted decision.

Steps:

1. add tests around current Julgate behavior
2. move code without behavior changes
3. make Julgate consume the extracted library
4. verify Julgate deployment and tests
5. make JulOS Remote worker consume the same library

Acceptance:

- no copied source tree exists
- Julgate and JulOS use the same transport implementation
- protocol-specific dependencies remain outside Core and Contracts

## 6. Phase J3 — Session lifecycle integration

Map Julgate lifecycle into JulOS:

```text
Requested → Starting → Connecting → Active
                              ├→ Reconnecting
                              ├→ Suspended
                              ├→ Disconnected
                              ├→ Failed
                              └→ Terminated
```

Implement:

- session creation through capability broker
- user and target authorization
- short-lived client signaling token
- lifecycle events
- inactivity timeout
- maximum duration
- reconnect
- explicit termination
- runtime cleanup

Acceptance:

- closing a window does not automatically imply session termination
- stale or expired session tokens cannot reconnect
- runtime failure creates a diagnostic problem

## 7. Phase J4 — Protocol parity

### RDP

Verify:

- username, password and domain handling
- security mode and certificate policy
- display resizing
- keyboard layouts
- Android and mobile keyboard input
- clipboard directions
- audio policy
- drive redirection
- reconnect
- useful error mapping

### VNC

Verify:

- authentication
- pixel format and scaling
- resize behavior where supported
- clipboard
- reconnect
- error mapping

### SSH

Verify:

- password and key authentication
- host-key policy
- terminal resizing
- clipboard
- session termination
- error mapping

### Console

Verify Proxmox or other console launch through a provider adapter without protocol assumptions in Core.

Acceptance:

- parity checklist is complete for every supported Julgate function
- unsupported features are explicitly documented, not silently omitted
- connection errors retain actionable cause codes

## 8. Phase J5 — Browser runtime integration

Use Remote transport to display and control isolated Chromium.

Steps:

- start Browser runtime through Runtime Manager
- create display endpoint inside private runtime network
- create Remote session reference
- stream display and input
- resize browser display from JulOS window
- route clipboard and downloads through policy
- terminate and clean runtime

Acceptance:

- Browser package does not know guacd internals
- multiple browser sessions remain isolated
- temporary profiles are removed

## 9. Phase J6 — Files integration

Replace Julgate-specific file redirection with capability requests to JulOS Files where appropriate.

Rules:

- Remote owns protocol file-redirection mechanics
- Files owns provider browsing, transfer storage and user-visible transfer queue
- permissions are checked for both remote transfer and target file scope
- credentials are not duplicated between packages

Acceptance:

- Remote works when Files is absent, with file features disabled explicitly
- Files can continue a transfer UI while the remote window is minimized

## 10. Phase J7 — Operational migration

Provide deployment migration instructions:

1. install JulOS Remote without disabling Julgate
2. import or recreate connection definitions through a supported migration tool
3. validate credentials through opaque secret migration
4. test representative RDP, VNC and SSH connections
5. compare session behavior and errors
6. move launch links to JulOS
7. keep Julgate available for defined rollback period
8. stop Julgate after acceptance
9. archive Julgate only after final backup and release notes

Connection secrets are never exported as plaintext through a browser workflow.

## 11. Parity matrix

Maintain a table during implementation:

| Capability | Julgate | Shared library | JulOS Remote | Tests | Accepted |
|---|---:|---:|---:|---:|---:|
| RDP connect | Yes | Planned | Planned | Planned | No |
| RDP resize | Yes | Planned | Planned | Planned | No |
| Mobile keyboard | Defect review | Planned | Planned | Planned | No |
| VNC connect | Yes | Planned | Planned | Planned | No |
| SSH connect | Yes | Planned | Planned | Planned | No |
| Clipboard | Review | Planned | Planned | Planned | No |
| Drive redirection | Review | Planned | Planned | Planned | No |
| Reconnect | Review | Planned | Planned | Planned | No |
| Diagnostics | Review | Planned | Planned | Planned | No |

The real table must be updated from repository evidence during J0.

## 12. Archive criteria

Julgate may be archived only when:

- all supported production connection types pass JulOS tests
- connection migration is documented and tested
- active users no longer require Julgate UI
- JulOS Remote has at least one stable release
- rollback window has ended
- open Julgate issues are migrated, resolved or explicitly closed as not applicable
- security and backup review is complete
- repository README points to JulOS Remote
- final Julgate release notes identify the successor

## 13. Prohibited migration shortcuts

- copying Julgate source and modifying both copies
- embedding Julgate UI in an iframe
- exposing guacd directly to the public client
- placing Guacamole types in JulOS Core
- migrating credentials through plaintext export
- archiving Julgate before parity
- hiding unsupported features behind silent fallback to Julgate
- keeping an indefinite runtime switch between old and new implementations
