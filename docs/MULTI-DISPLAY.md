# Multi-display Desktop

JulOS Desktop supports an active multi-display workspace when the same authenticated JulOS origin is open in multiple browser windows in the same browser profile.

Persisted multi-display presentation is the `desktop-multi` Workspace class. It is separate from `desktop-single` and from any device-scoped override.

## Behavior

- Each browser window is one active display.
- Runtime displays are ordered by connection time from left to right and mapped to stable logical `DisplaySlot` values.
- A durable JulOS window has one active display owner.
- Existing persisted windows initially remain on the earliest connected display instead of being duplicated on every display.
- Releasing a normal window within 12 px of an adjacent display edge starts a handoff.
- The target display prepares the package application through the normal launcher/frontend path before accepting the handoff.
- The transferred window enters from the corresponding edge and its size is clamped to the target usable area.
- Vertical placement is preserved proportionally when displays have different sizes.
- The taskbar on each display represents the windows owned by that display.
- The durable `desktop-multi` layout contains the combined window set and each Window's logical `DisplaySlot`.
- If a layout write conflicts with another browser instance, Desktop re-reads the server document and retries only when the returned revision exactly matches the conflict revision.
- Closing a display sends an explicit leave message. A missing heartbeat is treated as an unexpected disconnect. The earliest surviving display recovers windows owned by the lost display.

## Transport and trust boundary

Display coordination uses the browser `BroadcastChannel` API on `julos.desktop.workspace.v1`.

This channel is presentation-only. Its participant IDs are ephemeral and are never persisted as `DisplaySlot` or Window identity. It does not authorize operations, expose authentication tokens, move package capabilities into the client or replace server persistence. Every package/API operation still uses the existing authenticated same-origin server contracts.

Browsers without `BroadcastChannel` continue to use the existing single-display Desktop behavior.

## Scope

JulOS persists logical slots, not physical monitor serials, coordinates or browser fingerprints. Active displays negotiate slots at runtime; users may correct the logical ordering. A missing slot recovers onto the earliest surviving display without rewriting the stored slot until the user saves a new placement.

Widgets remain viewport presentation state and may appear on more than one active display. Multi-display coordination currently applies to application windows.

## Acceptance

Production acceptance requires:

1. Open JulOS in two browser windows and place them on two monitors.
2. Verify persisted application windows are not duplicated across both displays.
3. Launch a package application on the first display.
4. Drag it through the shared edge and verify the same window appears functional on the second display.
5. Drag it back through the opposite edge.
6. Repeat with different display sizes and verify geometry stays reachable.
7. Close the second display and verify its windows recover on the first.
8. Repeat by terminating the second window without a normal unload and verify heartbeat recovery.
9. Reload both displays and verify durable layout remains valid.
10. Confirm server authorization and package capability checks are unchanged.
