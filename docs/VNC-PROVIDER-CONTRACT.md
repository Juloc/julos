# VNC provider contract

## Scope

REM-007 defines VNC-specific launch policy owned by `JulOS.Remote.Transport` and the Remote package. Core session orchestration remains protocol-neutral and receives no VNC or Guacamole types.

This contract targets Apache Guacamole 1.6.0. A later upstream upgrade requires an explicit compatibility review.

## Compatibility

`GuacamoleLaunchRequest` keeps its published 0.1.0 constructor. `VncOptions` is an additive init property:

- existing Julgate 0.1.0 callers that omit it retain the previous payload;
- new JulOS VNC profiles supply explicit options;
- VNC options are rejected for RDP and SSH;
- RDP options are rejected for VNC.

The shared transport remains the only Guacamole JSON-auth encoder.

## Authentication

VNC authentication uses the existing bounded UTF-8 password field. The target username is not emitted for VNC because the VNC protocol defines password-based authentication only.

A VNC server that requires no password may use an empty password. Invalid UTF-8 and values larger than the shared 4096-byte limit fail before payload creation.

## Display and scaling

The provider exposes two resize policies:

- `dynamic` writes `disable-display-resize=false`, allowing Guacamole to request server display-size updates;
- `fixed` writes `disable-display-resize=true`, keeping the VNC server display size unchanged.

Optional display controls are mapped directly:

- color depth: 8, 16, 24 or 32 bits;
- local or remote cursor;
- read-only input;
- server-local input suppression;
- red/blue channel correction;
- lossless-only display updates;
- compression and JPEG quality levels from 0 through 9.

Unsupported values fail closed.

## Clipboard

Clipboard direction uses the common Guacamole parameters:

| Policy | `disable-copy` | `disable-paste` |
|---|---:|---:|
| `bidirectional` | `false` | `false` |
| `browser-to-remote` | `true` | `false` |
| `remote-to-browser` | `false` | `true` |
| `disabled` | `true` | `true` |

The optional VNC clipboard encoding accepts only:

- `ISO8859-1`
- `UTF-8`
- `UTF-16`
- `CP1252`

The default VNC-standard encoding is selected explicitly by new JulOS profiles. Existing callers that omit `VncOptions` keep the old payload without an added encoding parameter.

## Retry and reconnect

The optional Guacamole `autoretry` value is bounded from 0 through 10. It controls provider connection retries without adding a second reconnect implementation. Active-session resume, display reconnection and terminal-state enforcement remain owned by the existing REM-004 and REM-005 paths.

## Secret and protocol isolation

- passwords remain inside the provider boundary;
- launch JSON and provider parameters never enter package JavaScript;
- VNC-only parameters are written only for the VNC protocol;
- RDP certificate, security, domain, drive and resize parameters remain absent from VNC payloads;
- SSH terminal parameters remain absent from VNC payloads.

## Remaining live acceptance

Repository tests prove parameter translation, validation and protocol isolation. Live validation still requires a real VNC server to verify:

- authenticated and passwordless connections;
- dynamic and fixed display behavior;
- clipboard direction and encoding compatibility;
- provider retry behavior;
- remote cursor and display compatibility options;
- browser, mobile and Android input behavior through the existing REM-005 client.
