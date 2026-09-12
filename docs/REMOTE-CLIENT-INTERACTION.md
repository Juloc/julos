# Remote client interaction

Status: implemented in the Remote 1.0.5 frontend. Browser product work is not part of this scope.

## Default mobile controls

The connected Remote surface is remote-first: the connection form is hidden and the remote display owns the available surface. A small top handle reveals an overlay toolbar without permanently reducing the remote viewport.

Touch defaults are intentionally optimized for controlling a desktop operating system from a phone or tablet:

- direct touch is the default;
- tap sends the normal pointer/click interaction at the touched position;
- press-and-hold performs right click;
- two-finger drag scrolls;
- an explicit `RMB` toolbar button is always available;
- the remote cursor is visible by default;
- trackpad mode remains available per saved connection;
- right-click gesture, long-press timing and two-finger scroll sensitivity are configurable.

## Keyboard and Gboard

Desktop hardware-keyboard events continue through `Guacamole.Keyboard`. Touch devices additionally use one `Guacamole.InputSink` for software keyboards and IMEs.

Composition commits, multi-character paste, Unicode and emoji are converted to remote keysyms exactly once. The paired browser input event after a Gboard paste/composition is suppressed so pasted text is not duplicated. Backspace, Delete, Enter and Tab retain explicit keysym handling.

The toolbar exposes keyboard focus plus Ctrl, Alt, Shift, Win, Tab, Esc and Ctrl+Alt+Del. `Ctrl+Alt+Shift+Esc` remains the local keyboard-release shortcut.

## Clipboard

Clipboard transfer follows the provider clipboard policy. Local clipboard access happens only after an explicit user action. Remote text is buffered in memory only and is cleared when the display client detaches.

## Resolution and scaling

Saved Remote targets may select:

- Auto/window size;
- native device resolution;
- 1920×1080, 1600×900, 1366×768 or 1280×720;
- a bounded custom resolution;
- Fit, 100%, 125%, 150% or 200% local scaling.

RDP provider default resize policy is `display-update` (Guacamole/RDP dynamic display update). Per saved connection the client exposes three behaviors:

- **Dynamic (recommended)**: debounce viewport changes for 150 ms and send the new size to the active display;
- **Reconnect**: debounce the change, detach the display client and resume the same durable JulOS Remote session with a fresh Guacamole/RDP tunnel;
- **Keep remote resolution**: do not send later resize events and scale only locally.

All modes send one initial display size when the client first connects. Fixed resolution presets do not reconnect merely because the local browser viewport changed.

## System navigation

Remote does not manipulate browser history to capture Android/iOS system Back. System Back remains owned by the JulOS Shell Back dispatcher. This preserves a guaranteed JulOS escape/control path even while Remote input capture is active.

## Persistence and security

Interaction settings are stored in the package-owned saved launch target. Passwords and other credentials remain opaque Secret References and are never written into launch-target metadata.
