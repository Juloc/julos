# Protocol-neutral Remote session contract

REM-001 defines the JulOS 1.0 boundary for Remote sessions before any provider or display transport is selected. Core, Desktop and package code must use these contracts instead of importing Julgate, Guacamole or protocol-library implementation types.

## Capability

- name: `remote.session`
- contract version: `1.0.0`
- operations: `create`, `read`, `list`, `cancel`

The capability broker remains responsible for package identity, grants, deadlines and auditing. REM-001 only defines the payloads and validation rules.

REM-004 adds authenticated caller context at the broker boundary. The control plane passes the authorized package identity and authenticated user UUID as separate trusted inputs. The broker rejects any caller metadata already present in the untrusted request, creates the provider-visible context itself and records the user identity in capability audit events. Existing internal capability calls may omit a user identity, but a user-owned provider such as Remote must reject create, read, list or cancel operations that do not carry one.

Package or user identity must never be accepted from provider-specific JSON payload fields or a request-supplied `CapabilityCallerContext`. Providers receive only the broker-produced caller context.

## Protocol ownership

Core accepts a bounded lowercase protocol identity matching `^[a-z][a-z0-9.-]{0,31}$`. It does not know which concrete protocols exist.

The Remote package owns the JulOS 1.0 concrete identities and their UI default ports:

- `rdp`, conventional port `3389`
- `vnc`, conventional port `5900`
- `ssh`, conventional port `22`

The target contract always contains an explicit port. Conventional ports are package UI defaults, not hidden Core behavior. A later provider-selection service rejects a syntactically valid identity when no installed provider supports it.

## Create request

A create request contains:

- bounded caller-owned operation key
- package-defined protocol identity
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

Idempotency is scoped to the authenticated user and authorized caller package. One user or package cannot recover another caller's session by reusing its operation key.

## Time and size bounds

- protocol identity: 1 through 32 lowercase identifier characters
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

It contains no access token. The relative endpoint may carry non-secret package, revision and expiry selectors, but no bearer credential or provider address. Every browser connection is authenticated by the JulOS login cookie and authorized against the configured public Origin, authenticated user, caller package selector, active durable session, exact revision, exact stored descriptor and expiry before the hidden provider endpoint is resolved.

The Remote package owns display interaction behavior. `Ctrl+Alt+Shift+Escape` releases keyboard capture, resets pressed remote keys and returns focus to the local shell; a deliberate pointer press on the display captures it again. Re-capture changes focus only and never manufactures a connection-state transition; connection status remains driven by transport events. Resize observations are collapsed through one 150 ms scheduler, while the initial display size is sent immediately. Teardown cancels pending resize delivery and removes all input handlers.

## Failures

The contract provides stable caller-safe codes for malformed or unavailable protocol identity, invalid target, unavailable credentials or network profile, expired request, unavailable runtime, trust confirmation, authentication failure, connection loss and invalid state transition.

Provider exception text, raw command output and protocol-library objects must not cross this boundary.

## Responsibilities left to later work

REM-001 does not:

- inventory or extract Julgate behavior
- resolve secret material
- create Runtime Manager containers
- implement RDP, VNC or SSH transports
- proxy display or terminal data
- persist Remote profiles
- create the Remote user interface

REM-004 begins with authenticated caller propagation, then adds durable session ownership, exact idempotency, Runtime Manager orchestration, lifecycle events, cancellation and cleanup. Later transport and UI items remain separate.
