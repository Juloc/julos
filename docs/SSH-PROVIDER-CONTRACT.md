# SSH provider contract

## Scope

REM-008 defines SSH-specific launch policy owned by `JulOS.Remote.Transport` and the Remote package. Core session orchestration remains protocol-neutral and receives no SSH or Guacamole types.

This contract targets Apache Guacamole 1.6.0. A later upstream upgrade requires an explicit compatibility review.

## Compatibility

`GuacamoleLaunchRequest` keeps its published 0.1.0 constructor. `SshOptions` is an additive init property:

- existing callers that omit it retain the previous SSH payload and normalized terminal font size;
- new JulOS SSH profiles supply explicit options;
- SSH options are rejected for RDP and VNC;
- RDP and VNC options are rejected for SSH.

The shared transport remains the only Guacamole JSON-auth encoder.

## Authentication

The provider exposes three explicit modes:

- `password` requires a username and the existing bounded UTF-8 password;
- `public-key` requires a username and one bounded UTF-8 OpenSSH private key, with an optional bounded passphrase;
- `none` requires a username and rejects all credential material.

Contradictory credentials fail before payload creation. Private keys and passphrases remain caller-owned byte memory and are written only inside the provider boundary. Secret-bearing JSON and encryption buffers are cleared after encoding.

Interactive credential prompting is not part of the 1.0 provider contract because the browser must not receive target credentials.

## Host-key verification

Two explicit policies are available:

- `strict` requires one bounded OpenSSH `known_hosts` entry and writes it as Guacamole `host-key`;
- `disabled` omits `host-key` and rejects a supplied entry.

Strict mode validates that the value is one line, contains a supported SSH key type and contains valid Base64 key data. The provider does not add a second known-hosts store or trust-on-first-use implementation.

## Network behavior

The explicit SSH options write:

- `timeout` from 1 through 120 seconds;
- `server-alive-interval` as 0 to disable keepalives or 2 through 300 seconds.

These bounds prevent unbounded connection attempts while retaining Guacamole's native SSH behavior.

## Terminal display

The provider writes an explicit bounded `font-name` and `font-size` from 8 through 24 points. Guacamole renders the terminal server-side, so the selected font must exist in the provider image.

Window-size changes remain on the existing REM-005 Guacamole display path. No SSH-specific resize handler or second terminal client is introduced.

## Protocol isolation

- SSH private keys, passphrases and host keys are written only for SSH;
- RDP certificate, domain, drive and resize parameters remain absent from SSH payloads;
- VNC display and clipboard-encoding parameters remain absent from SSH payloads;
- omitted `SshOptions` preserve the previous `monospace` and normalized font-size payload.

## Remaining live acceptance

Repository tests prove parameter translation, validation, protocol isolation and compatibility. Live validation still requires a real SSH server to verify:

- password authentication;
- encrypted and unencrypted OpenSSH private keys;
- strict host-key success and mismatch failure;
- NONE authentication against a compatible appliance;
- keepalive and timeout behavior;
- terminal resize, keyboard input and mobile behavior through the REM-005 client.
