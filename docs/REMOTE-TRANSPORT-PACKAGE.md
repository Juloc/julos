# Shared Remote transport package

## Purpose

`JulOS.Remote.Transport` is the single source package for reusable transport behavior consumed by JulOS Remote and Julgate during the controlled product transition.

It applies decisions D002 and D006:

- source remains in the JulOS monorepo
- reusable behavior is extracted once
- Julgate consumes an immutable package artifact
- JulOS Remote uses the same source through a project reference
- no vendored copy, submodule or permanent compatibility wrapper exists

## Source and artifact

Source project:

```text
packages/JulOS.Remote/shared/JulOS.Remote.Transport/
```

NuGet identity:

```text
JulOS.Remote.Transport
```

The repository `VERSION` file is the package version source. Published versions are immutable. A changed package requires a new version; an existing package version is never overwritten.

## Initial shared surface

The REM-003 first slice contains:

- `RemoteTransportProtocols`
  - concrete RDP, VNC and SSH identities
  - conventional UI default ports
- `GuacamoleLaunchRequest`
  - provider-side target and option input
  - password represented as caller-owned UTF-8 memory
- `GuacamoleJsonLaunchEncoder`
  - Guacamole parameter mapping
  - JSON-auth payload construction
  - HMAC-SHA256 signing
  - Guacamole-required AES-CBC encryption
  - client identifier construction
  - clearing of secret-bearing intermediate buffers
- `GuacamoleLaunchToken`
  - encrypted provider data and non-secret metadata

These types are provider-side. They are not Core contracts and are never serialized by the public JulOS API.

## Security boundary

- callers obtain target values only through an authorized provider operation
- the shared library does not store values or own key management
- caller-owned password buffers remain caller-owned and must be cleared after use
- intermediate JSON, signature, signed payload and copied key buffers are cleared after encoding
- Guacamole's required AES-CBC format is isolated to this adapter and locally documented
- browser code never receives a raw provider key or target secret
- later JulOS display endpoints keep the encrypted provider payload server-side

## Consumption model

### JulOS Remote

The Remote worker references the source project directly. The first slice consumes the shared protocol catalog in its health model. REM-004 later consumes the launch encoder through provider orchestration.

### Julgate

Julgate consumes a published `JulOS.Remote.Transport` version. Its adapter remains responsible for:

- reading Julgate configuration
- mapping `MatgateUser` and `ServerEndpoint`
- choosing launch expiry and session identity
- constructing the Julgate URL
- returning Julgate-specific errors

The shared package replaces the duplicated payload, parameter, signing and encryption implementation.

## Publication gate

A package version may be published only when:

- solution build and tests pass
- behavior vectors decrypt and verify successfully
- the worker integration test passes
- `dotnet pack` succeeds deterministically
- package contents contain no build output outside the expected assembly, symbols and metadata
- package digest and provenance are recorded
- the source commit is merged

Julgate is updated only after the package is available from the configured immutable source.

## Removal gate

REM-003 is complete only when:

- JulOS Remote consumes the shared project
- Julgate consumes the published package
- Julgate's original shared implementation is removed
- both repositories validate
- Julgate remains deployable
- no source duplication or fallback switch remains
