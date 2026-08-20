# JulOS Interface Plan

This document defines the completed visual and interaction direction for the JulOS production desktop shell. It refines `UX_SPECIFICATION.md` and `DESKTOP_UX_COMPLETION.md`; it does not introduce a second application, window, launcher, taskbar or mobile model.

## Product direction

JulOS uses Fluent 2 Web interaction conventions with a distinct JulOS presentation. The chosen direction is a restrained Liquid Glass / Aero treatment: translucent shell hierarchy, clear depth, one accent system and readable application surfaces. It must feel like a desktop environment without cloning Windows or macOS.

Glass is reserved for shell chrome such as the taskbar, launcher, window title bars, switcher, authentication card and widgets. Application content remains substantially opaque for readability.

## Visual defaults

- System theme is the default; light and dark remain selectable.
- The existing JulOS accent token family is the single accent source for shell state, focus, app marks and snap previews.
- Motion is enabled by default but every shell transition obeys the existing reduced-motion preference and the browser `prefers-reduced-motion` setting.
- Typography remains Segoe UI Variable / Segoe UI / system fallback as defined by the shared tokens.
- Shell controls use consistent rounded geometry, subtle hairline borders and restrained elevation.
- System glyphs remain simple, monochrome and visually consistent; application glyphs use the JulOS accent treatment.
- Decorative effects must never reduce text contrast or create an ambient wallpaper behind application content.

## Desktop shell

### Taskbar

The taskbar is a floating glass surface at the bottom edge with safe-area support. Launcher, running applications and status controls keep stable identities and familiar desktop behavior. Active, minimized and attention state must remain visible without decorative badges.

### Launcher

The launcher is a glass flyout anchored to the taskbar. Application rows use large enough pointer/touch targets, clear titles and optional secondary package/target text. Keyboard focus and hover use the same visual hierarchy.

### Windows

Window behavior continues to be owned by the existing `WindowStore`, `WindowInteractionController`, snapping and taskbar models. The presentation layer provides:

- clear active/inactive depth;
- translucent title-bar chrome with readable opaque application bodies;
- consistent control sizing;
- visible snap previews;
- maximized/full-screen edge-to-edge presentation;
- no duplicate window layout implementation.

## Desktop edit mode

Desktop edit mode is a shell-level state for arranging and configuring desktop content.

- Long-press duration: 520 ms.
- Pointer movement above 12 px cancels the gesture.
- Long-press does not start from buttons, form controls, dialogs, open application windows, the launcher or taskbar.
- Entering edit mode shows a persistent shell toolbar.
- `Escape` or the Done/Fertig action exits edit mode.
- Widgets receive an explicit edit-state treatment without changing package ownership or widget rendering contracts.
- The mode uses Pointer Events so the same gesture works with touch and pen and remains usable with a deliberate mouse hold.

This change establishes the shared edit-mode entry/exit contract. Widget placement persistence continues to be owned by the existing desktop layout subsystem; no parallel local layout store is permitted.

## Responsive behavior

JulOS keeps one shell and one application model.

### Desktop (`>= 1100 px`)

- free window positioning/resizing and snapping;
- full taskbar and launcher;
- widgets available;
- keyboard window switching remains first-class.

### Tablet (`720-1099 px`)

- same desktop model with larger touch targets;
- window title bars and taskbar controls expand for touch;
- Pointer Events remain the only interaction path.

### Mobile (`< 720 px`)

- applications use the existing responsive full-screen presentation rather than floating windows;
- taskbar and launcher respect safe areas and use larger targets;
- nonessential labels collapse before core actions;
- launcher becomes a wide bottom flyout;
- widgets remain excluded by the existing runtime mobile rule;
- this is not a separate mobile application shell.

## Accessibility and fallbacks

- All new motion is disabled by JulOS reduced-motion mode and `prefers-reduced-motion`.
- Forced-colors mode removes translucent decoration and preserves explicit borders.
- Browsers without `backdrop-filter` fall back to solid Fluent-compatible surfaces.
- Existing focus rings, keyboard navigation and shell shortcuts remain authoritative.
- Touch targets are at least 44 px on tablet/mobile shell controls.

## Implementation ownership

- `appearance.ts` and `design-tokens.css`: existing theme/motion contract.
- `interface-plan.ts`: presentation bootstrap, responsive presentation classification and edit-mode gesture/state only.
- `interface-plan.css`: final production-shell visual layer.
- `desktop-runtime.ts`, `window-store.ts`, `window-interactions.ts`, `window-snapping.ts`, `window-taskbar.ts`: remain the only owners of desktop/window behavior.
- `layout-persistence.ts`: remains the only owner of persisted desktop layout.

## Validation

Repository validation for this plan requires:

1. Desktop TypeScript typecheck.
2. Desktop unit tests, including interface-plan viewport/edit-mode tests.
3. Production Desktop build.
4. Existing solution build/test gates.
5. Deployed acceptance from `DESKTOP_UX_COMPLETION.md` on Windows, macOS and basic tablet/touch before the overall Desktop UX gate is called release-ready.

The repository implementation may be complete before item 5; deployed cross-platform acceptance is a release gate, not a reason to add a second code path.
