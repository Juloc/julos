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

The root `VERSION` file is the sole package-version source. Published versions are immutable. A changed package requires a new version; the publication workflow never uses `--skip-duplicate` and therefore fails when a version already exists.

Version `0.1.0` is the initial Julgate-compatible extraction. Version `0.2.0` adds explicit RDP and VNC policy. Version `0.3.0` preserves the published constructor and adds explicit SSH authentication, host-key, network and terminal policy.

The package metadata links every version to `https://github.com/Juloc/julos` and records the exact source commit supplied by the workflow.

## Shared surface

The package contains:

- `RemoteTransportProtocols`
  - concrete RDP, VNC and SSH identities
  - conventional UI default ports
- `GuacamoleLaunchRequest`
  - provider-side target and option input
  - password represented as caller-owned UTF-8 memory
  - additive explicit RDP, VNC and SSH policy properties
- `GuacamoleRdpOptions`
  - security, certificate, resize and clipboard policy
- `GuacamoleVncOptions`
  - resize, clipboard, cursor, display quality and bounded retry policy
- `GuacamoleSshOptions`
  - authentication, caller-owned private-key/passphrase memory, host-key verification, timeout, keepalive and terminal policy
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
- caller-owned password, private-key and passphrase buffers remain caller-owned and must be cleared after use
- intermediate JSON, signature, signed payload and copied key buffers are cleared after encoding
- Guacamole's required AES-CBC format is isolated to this adapter and locally documented
- browser code never receives a raw provider key or target secret
- later JulOS display endpoints keep the encrypted provider payload server-side

## Consumption model

### JulOS Remote

The Remote worker references the source project directly. It consumes the shared protocol catalog and launch encoder through the provider orchestration boundary.

### Julgate

Julgate consumes one exact published `JulOS.Remote.Transport` version. Its adapter remains responsible for:

- reading Julgate configuration
- mapping `MatgateUser` and `ServerEndpoint`
- choosing launch expiry and session identity
- constructing the Julgate URL
- returning Julgate-specific errors

The shared package replaces the duplicated payload, parameter, signing and encryption implementation.

## Publication workflow

`.github/workflows/publish-remote-transport.yml` is the only publication path.

It runs after a relevant commit reaches `agent/complete-julos-work-breakdown`. Pull requests receive no package write permission.

The workflow:

1. checks out the exact source commit without persisting Git credentials;
2. runs the complete repository validator with the same PostgreSQL service used by normal CI;
3. validates the root version as a bounded NuGet-compatible immutable version;
4. performs a Release pack with deterministic CI properties and the exact repository commit;
5. requires exactly one `.nupkg` and one `.snupkg` with the expected versioned names;
6. creates `SHA256SUMS` for both artifacts;
7. creates GitHub artifact attestations for package, symbols and checksums;
8. retains the complete evidence bundle as a workflow artifact for 90 days;
9. authenticates to the owner-scoped GitHub NuGet feed using the job-scoped `GITHUB_TOKEN`;
10. publishes only the `.nupkg` and refuses an existing version.

Required workflow permissions are limited to:

- `contents: read`
- `packages: write`
- `id-token: write`
- `attestations: write`

No PAT, package credential or plaintext token is committed to the repository.

## Cross-repository read boundary

After the first package version exists, the package must grant `Juloc/Julgate` read access under GitHub Packages Actions access. Julgate then authenticates during CI with its repository-scoped `GITHUB_TOKEN` and references an exact package version.

The package source URL may be committed. Credentials may not. Local developers configure the same source in their user-level NuGet configuration with an authorized token; repository files never contain that token.

## Publication gate

A package version may be published only when:

- solution build and tests pass
- behavior vectors decrypt and verify successfully
- the worker integration test passes
- Release `dotnet pack` succeeds deterministically
- package contents contain no unexpected artifact
- package and symbol digests are recorded
- provenance attestations are created
- the source commit is already on the integration branch
- the requested version does not already exist

Julgate is updated only after the exact package version is available from the configured immutable source.

## Removal gate

REM-003 is complete only when:

- JulOS Remote consumes the shared project
- the immutable package version is published with digest and provenance
- Julgate consumes that exact package version
- Julgate's original shared implementation is removed
- both repositories validate
- Julgate remains deployable
- no source duplication or fallback switch remains
