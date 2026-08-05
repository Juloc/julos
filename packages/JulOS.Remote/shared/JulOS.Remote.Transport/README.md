# JulOS.Remote.Transport

Shared provider-side Remote transport primitives used by JulOS Remote and, during migration, Julgate.

The package currently provides:

- the supported RDP, VNC and SSH transport catalog
- additive explicit Apache Guacamole 1.6.0 RDP security, certificate, resize and clipboard policy
- Guacamole JSON-auth launch contracts
- Guacamole parameter mapping
- HMAC-SHA256 signing and Guacamole-compatible encrypted payload generation

The published 0.1.0 `GuacamoleLaunchRequest` constructor remains intact. Existing consumers that omit `RdpOptions` keep their prior behavior. New JulOS RDP integrations use the additive explicit options and are validated before any launch payload is created.

This package is not a public JulOS Core contract. It must be used only inside authorized Remote provider boundaries. Secret values must not be exposed to package frontends, browsers, logs or durable session contracts.

Source, security and migration rules are documented in `docs/REMOTE-TRANSPORT-PACKAGE.md` and `docs/RDP-PROVIDER-CONTRACT.md` in the JulOS repository.
