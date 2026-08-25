# Agent protocol, diagnostics and update foundation

Status: Implemented migration source only. This document describes the currently released legacy Agent protocol so existing beta deployments can be diagnosed and migrated. No new feature may extend this contract. `HCON-002` replaces it atomically with the Host Connector contract in `HOST_CONNECTOR.md`; the old runtime route then executes no commands and exists, if needed, only as a time-bounded `426 Upgrade Required` tombstone.

This document covers protocol compatibility, diagnostics and manual update preparation for the legacy binary. It does not authorize automatic updates.

## Protocol version

Every request below `/api/v1/agent` must include:

```text
X-JulOS-Agent-Protocol: 1
```

The Server returns these headers on every Agent runtime response:

```text
X-JulOS-Agent-Protocol: 1
X-JulOS-Agent-Protocol-Min: 1
X-JulOS-Agent-Protocol-Max: 1
```

A missing, malformed, older or newer protocol receives HTTP `426 Upgrade Required` with code `agent.protocol_incompatible`. The Agent treats HTTP 426, a missing confirmation header or a different confirmation version as a terminal protocol error. It does not silently downgrade or retry indefinitely.

The protocol version is independent from the Agent binary semantic version. A binary version change must not change protocol behavior without updating `AgentProtocolContract`, tests and this document.

## Enrollment and reconnect

Enrollment sends the protocol header before credentials are persisted. A successful response must confirm the exact requested protocol version.

After enrollment, every heartbeat, metric upload, command poll and command completion uses the same protocol contract. Transport and ordinary HTTP failures use bounded reconnect backoff. A successful heartbeat resets the active failure count and the next backoff to one second. Protocol failures stop the process with exit code `5` and an actionable error code.

## Capability inventory

`AgentCapabilityInventory` is the single source for heartbeat advertisement and diagnostics. JulOS 1.0 advertises:

- `host.metrics.linux`
- `agent.commands.core`
- `agent.diagnostics.core`

The diagnostics snapshot and heartbeat must never maintain separate capability lists.

## Diagnostics snapshot

The allowlisted `diagnostics.snapshot` command returns:

- Agent semantic version
- Agent protocol version
- operating system, process architecture and framework
- process start and observation timestamps
- capability names, versions, enabled state and metadata versions
- connection attempts and successful heartbeats
- consecutive failures
- last connected and failure timestamps
- bounded failure kind and next retry delay
- future update contract and automatic-action flags

The result excludes credentials, enrollment tokens, raw exception text, environment variables and machine identity source values.

## Manual diagnostic procedure

1. Verify the Agent process is running and inspect its exit code.
2. Exit code `5` means protocol incompatibility. Compare the Agent and Server release versions before restarting.
3. From JulOS, queue `diagnostics.snapshot` only when `agent.commands.core` advertises that command.
4. Verify `protocolVersion` is `1` and inspect `capabilities` and `reconnect`.
5. A rising `consecutiveFailures` value indicates an active connection problem. Historical `lastFailureAtUtc` remains after recovery by design.
6. Do not request or expose the local identity file during ordinary diagnostics.

## Update preparation

`AgentUpdatePolicy` only validates a manually supplied future artifact. It checks:

- semantic current and target versions
- unchanged-version rejection
- explicit approval for downgrades
- lowercase SHA-256 syntax
- constant-time digest equality

A successful preparation result states:

- update contract version `1`
- current and target versions
- whether the operation is a downgrade
- verified artifact digest
- `requiresManualInstallation: true`
- `automaticApplySupported: false`

JulOS 1.0 does not download, replace or restart the Agent automatically. Those actions require a later versioned contract, security review, rollback design and dedicated release gates.

## Manual installation or replacement

1. Stop the Agent service.
2. Back up the existing binary and its service definition. Do not copy the identity credential into logs or release artifacts.
3. Verify the replacement artifact digest against the signed release metadata.
4. Run update preparation and retain its result with the change record.
5. Replace the binary while preserving the protected identity file and its permissions.
6. Start the service.
7. Confirm enrollment is not repeated, protocol negotiation succeeds and a heartbeat is recorded.
8. Queue `diagnostics.snapshot` and verify the expected binary and protocol versions.
9. Roll back manually if startup, protocol negotiation or heartbeat fails.

## Security invariants

- no silent protocol downgrade
- no automatic update download, apply or restart
- no credential or token in diagnostics
- no arbitrary command execution
- no unbounded exception or environment data
- no separate heartbeat and diagnostics capability inventories
- no retry loop for terminal protocol incompatibility
