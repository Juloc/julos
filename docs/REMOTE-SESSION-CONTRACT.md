# Protocol-neutral Remote session contract

REM-001 defines the JulOS 1.0 boundary for Remote sessions before any provider or display transport is selected. Core, Desktop and package code must use these contracts instead of importing Julgate, Guacamole, RDP, VNC or SSH implementation types.

## Capability

- name: `remote.session`
- contract version: `1.0.0`
- operations: `create`, `read`, `list`, `cancel`

The capability broker remains responsible for package identity, grants, deadlines and auditing. REM-001 only defines the payloads and validation rules.

## Supported protocols

The contract recognizes the stable identifiers:

- `rdp`, conventional port `3389`
- `vnc`, conventional port `5900`
- `ssh`, conventional port `22`

The target contract always contains an explicit port. Conventional ports are helpers for UI defaults, not hidden server behavior.

## Create request

A create request contains:

- bounded caller-owned operation key
- protocol identity
- DNS name or IP address without URI scheme, path, query, fragment or user information
- explicit TCP port
- secret-reference UUID
- optional Remote profile UUID
- optional network profile UUID
- viewport width, height and device scale factor
- idle timeout
- maximum session duration
- request timestamp and absolute acceptance deadline

The contract contains no username/password pair, access token, private key or provider-specific credential object. Secret material is resolved later through the existing secret-reference boundary.

## Exact idempotency

`RemoteSessionContractValidator.ComputeRequestIdentity` serializes a validated normalized request with the Web JSON contract and computes lowercase SHA-256.

A later session service must persist both `operationKey` and `requestIdentity`:

- the same operation key plus the same identity returns the existing session
- the same operation key plus a different identity fails with an idempotency conflict
- the operation key must never cause a second provider runtime to be allocated

## Time and size bounds

- request timestamp: at most 10 minutes old and at most 1 minute in the future
- acceptance deadline: after the request, no more than 10 minutes after it and still in the future
- viewport width: 320 through 7680 CSS pixels
- viewport height: 240 through 4320 CSS pixels
- device scale factor: 0.5 through 4
- idle timeout: 60 through 86400 seconds
- maximum session duration: 300 through 604800 seconds
- idle timeout cannot exceed maximum session duration
- list page size: 1 through 200
- cancellation reason: at most 256 characters

These limits are contract limits. Providers may advertise stricter capabilities later, but cannot silently widen them.

## Lifecycle

The stable states are:

1. `requested`
2. `provisioning`
3. `connecting`
4. `connected`
5. `disconnecting`
6. terminal `disconnected`, `cancelled`, `expired` or `failed`

Allowed transitions are explicit in `RemoteSessionContractValidator`. Terminal sessions cannot reconnect or return to provisioning. Reconnect behavior, when implemented, creates a new operation and session unless a later versioned contract explicitly defines resume semantics.

## Display transport

`RemoteDisplayTransportResponse` exposes only:

- kind: `graphical` or `terminal`
- transport contract version
- authenticated same-origin relative endpoint
- descriptor expiry

It contains no access token. Authentication is performed by the JulOS same-origin session and later transport-specific authorization.

## Failures

The contract provides stable caller-safe codes for unsupported protocol, invalid target, unavailable credentials or network profile, expired request, unavailable runtime, trust confirmation, authentication failure, connection loss and invalid state transition.

Provider exception text, raw command output and protocol-library objects must not cross this boundary.

## Responsibilities left to later work

REM-001 does not:

- inventory or extract Julgate behavior
- resolve secret material
- create Runtime Manager containers
- implement RDP, VNC or SSH
- proxy display or terminal data
- persist Remote profiles
- create the Remote user interface

Those items begin only after these contracts and tests are green.
