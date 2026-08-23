# Desktop UX Completion

JulOS 1.0 must feel like a real desktop environment rather than a web page that happens to contain windows.

This gate is completed before feature packages become the main implementation focus.

## Current implementation status

- **DESK-013 — implemented:** fresh deployments provide browser-native administrator setup and normal local sign-in.
- **DESK-014 — implemented:** the production shell composes the existing launcher, window store, taskbar, package frontend host, layout persistence, Package Manager, Settings, Agent status, notifications/problems and persisted package widgets. Package lifecycle changes refresh the desktop catalog without a page reload.
- **DESK-015 — implemented:** the production runtime uses the shared responsive viewport rules, Pointer Events, full-screen state, minimized taskbar state, shell keyboard controller and existing Alt-Tab switcher. Deployed Windows/macOS/touch acceptance remains a release gate (see `BACKLOG.md`).
- **DESK-016 — implemented:** system/light/dark theme, reduced motion and the Fluent-derived token and accent system are active, and server-confirmed theme and motion changes apply without reload. Deferred beyond this iteration: a user-selectable accent, the Full/Balanced/Simple presets and the wallpaper/density controls described in `UI_DESIGN_SYSTEM.md`; the shipped Settings surface exposes language, theme, motion and time zone only.

The implementation status above does not replace the deployed acceptance gate at the end of this document.

## Product target

JulOS keeps Fluent 2 Web as its visual foundation without becoming a Windows clone. Interaction patterns must be immediately understandable to both Windows and macOS users.

The desktop shell owns common desktop behavior. Packages only provide applications, widgets and package-specific commands; they must not implement their own competing window, taskbar, launcher or notification systems.

## DESK-013: First-run experience

A fresh JulOS installation must be usable entirely from the browser.

- detect `setupRequired` during shell startup;
- show a dedicated first-run setup view before the desktop becomes interactive;
- collect the initial administrator user name, display name and password;
- call the existing `/api/v1/auth/setup` endpoint through the same-origin Desktop API client;
- display field-level validation and caller-safe server failures;
- establish the authenticated session after successful setup;
- transition directly into the normal desktop without requiring a manual API call or page workaround;
- never expose the setup flow again after initialization is complete.

## DESK-014: Shell composition

The production entry point must compose the already implemented Desktop building blocks into one working shell.

- launcher lists installed and enabled applications;
- applications open as real JulOS windows;
- window state is reflected in the taskbar;
- minimize, restore, maximize, close, focus and z-order work consistently;
- drag, resize and snapping are active in the production shell;
- saved layout and window state restore after reload/login where allowed;
- Package Manager, Settings, notifications, problems and Agent status are reachable from the shell;
- Package Manager can install a signed package, apply configuration and control package enable/disable/removal lifecycle;
- package lifecycle changes refresh launcher/window/widget availability without a page reload;
- widgets render through the existing widget host from persisted placements;
- empty-state content appears only when there are genuinely no launchable applications.

No duplicate window store, launcher index, taskbar state, widget state or package frontend host may be introduced.

## DESK-015: Cross-platform desktop interaction pass

The interaction model must be neutral enough for Windows and macOS users while remaining recognizably JulOS.

### Windows and application behavior

- drag and resize with predictable hit targets;
- minimize, maximize/restore, close and full-screen behavior;
- double-clicking a title bar toggles maximize/restore;
- dragging a maximized window restores it into a movable window;
- active and inactive windows have clear but restrained focus treatment;
- window position and size persist where appropriate;
- edge and corner snapping provide visible preview before committing;
- keyboard focus never becomes trapped outside intentional modal contexts.

### Taskbar / dock behavior

- installed/running applications have stable identities;
- active, minimized and attention states are distinguishable;
- clicking a running application restores/focuses it instead of opening accidental duplicates unless the app explicitly supports multiple windows;
- badges are reserved for meaningful state, not decoration;
- taskbar placement may remain JulOS-specific, but interaction must be familiar to both Windows taskbar and macOS Dock users.

### Keyboard behavior

- Windows/Linux primary shortcuts use `Ctrl`;
- macOS maps equivalent primary shortcuts to `Cmd` where browser APIs permit reliable platform detection;
- `Escape` closes transient panels and dismissible dialogs;
- keyboard access exists for launcher, application switching and window actions;
- visible shortcut labels use the platform-appropriate modifier terminology.

### Pointer, touch and mobile

- pointer interactions use Pointer Events rather than separate mouse-only behavior;
- touch targets remain usable on tablets;
- resize/snap affordances do not make the desktop unusable on touch devices;
- responsive mode may simplify multi-window behavior on small screens but must not create a second application model.

## DESK-016: Appearance and personalization

- light, dark and system theme;
- one JulOS accent color system;
- reduced-motion support plus the existing animation preference;
- consistent typography, spacing, elevation and focus rings based on Fluent 2 Web;
- window controls may support left/right placement as a user preference if it can be implemented without duplicating title-bar layouts;
- taskbar alignment may support sensible alternatives if implemented as one layout rule rather than separate shells.

## Acceptance gate

Desktop UX Completion is done only when a clean installation can be tested through this complete path:

1. open a fresh JulOS deployment;
2. complete administrator setup in the UI;
3. enter the authenticated desktop;
4. open Package Manager and Settings;
5. install/enable a signed test or official package;
6. launch its application from the launcher;
7. move, resize, snap, minimize, restore, maximize and close its window;
8. reload and verify allowed desktop state persistence;
9. exercise the same shell with a desktop Windows browser and a desktop macOS browser;
10. verify keyboard-only and basic tablet/touch operation.

The gate must be validated against the production Server-served Desktop build, not a standalone development shell.

## Ordering

Implementation order from the current alpha state:

1. DESK-013 First-run experience
2. DESK-014 Shell composition
3. DESK-015 Cross-platform interaction pass
4. DESK-016 Appearance and personalization completion
5. deployed Agent/Host Metrics acceptance
6. deployed Remote acceptance
7. Browser completion
8. Docker and Proxmox
9. Files and Caddy
10. Discovery and operational hardening
11. JulOS 1.0 release gates and Julgate migration

Feature work may continue in parallel when it does not delay this gate, but JulOS must not call the Desktop foundation release-ready until this document's acceptance path passes.
