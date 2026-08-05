# RDP provider contract

## Scope

REM-006 defines the RDP-specific launch policy owned by `JulOS.Remote.Transport` and the Remote package. Core session orchestration remains protocol-neutral and receives no RDP, FreeRDP, Guacamole or Windows authentication types.

This contract targets Apache Guacamole 1.6.0. A later upstream upgrade must be an explicit compatibility change with regenerated tests and documentation.

## Compatibility

`GuacamoleLaunchRequest` keeps its published 0.1.0 constructor. `RdpOptions` is an additive init property:

- existing Julgate 0.1.0 callers that omit it retain `security=any`, legacy strict/ignore certificate behavior, `resize-method=reconnect` and bidirectional clipboard access;
- new JulOS Remote profiles must supply explicit options;
- explicit RDP options are rejected for VNC and SSH.

The shared package remains the only Guacamole JSON-auth encoder. No provider may copy its parameter, signing or encryption implementation.

## Security modes

The exact accepted identities are:

- `any`
- `nla`
- `nla-ext`
- `tls`
- `vmconnect`
- `rdp`

`nla` and `nla-ext` require a bounded username and a non-empty valid UTF-8 password before launch. JulOS package JavaScript never receives credentials and does not expose a provider-side interactive credential prompt.

## Certificate policy

Exactly one policy is selected:

- `strict` — normal certificate validation; no automatic trust
- `ignore` — writes `ignore-cert=true`; intended only for an explicit trusted-network choice
- `tofu` — writes `cert-tofu=true`
- `pinned` — writes a bounded comma-separated `cert-fingerprints` list

Pinned entries use `sha1:<40 hex>` or `sha256:<64 hex>`. Input may contain colon-separated hex and is normalized before encoding. Fingerprints are not secrets, but they remain provider configuration and are not part of the public display descriptor.

The legacy `IgnoreCertificate` flag may coexist with explicit options only when the explicit policy is also `ignore`. Conflicting controls fail closed.

## Resize

The accepted Guacamole values are:

- `display-update` for the RDP 8.1 Display Update channel
- `reconnect` for servers that require reconnect-based resizing

This provider setting is separate from the browser-side 150 ms resize scheduler. The scheduler controls request frequency; the provider policy controls how the RDP server applies a requested size.

## Clipboard

Clipboard direction is explicit:

| Policy | `disable-copy` | `disable-paste` |
|---|---:|---:|
| `bidirectional` | `false` | `false` |
| `browser-to-remote` | `true` | `false` |
| `remote-to-browser` | `false` | `true` |
| `disabled` | `true` | `true` |

`disable-copy` blocks remote-session clipboard data from reaching the browser. `disable-paste` blocks browser clipboard data from reaching the remote session.

## Validation and secret handling

- target password is bounded to 4096 UTF-8 bytes;
- caller, connection, session, host, user, domain, keyboard, drive and client names are bounded and reject control characters;
- drive redirection still requires both provider-local path and visible name;
- certificate policies and fingerprints are mutually validated;
- raw provider exceptions, launch JSON, passwords and encryption keys never cross the provider boundary;
- temporary sensitive buffers continue to be cleared by the existing encoder.

## Failure classification

Invalid credentials use `remote.authentication_failed`.

A provider that can reliably identify a disabled, locked or expired target account maps it to the separate caller-safe account-unavailable failure introduced by REM-006. The provider must not classify account state by forwarding or pattern-matching arbitrary localized exception text in package JavaScript.

## Remaining live acceptance

Repository tests prove parameter translation and validation. REM-006 still requires a real RDP provider runtime to prove:

- successful Windows authentication for supported security modes;
- strict, ignore, TOFU and pinned certificate behavior;
- clipboard direction;
- `display-update` and reconnect behavior;
- caller-safe distinction between invalid credentials and disabled/locked account where the upstream provider exposes that distinction;
- Android keyboard regression remains fixed.
