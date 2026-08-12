# JulOS UI Design System

Status: Draft product direction based on the approved Ocean Breeze concept. The beta scope stays deliberately small while the shared shell remains extensible.

## 1. Product direction

JulOS must feel like its own desktop environment, not like a dashboard and not like a Windows/macOS clone.

The interaction model stays familiar:

- desktop surface with wallpaper;
- application shortcuts and widgets;
- one global taskbar;
- launcher/search;
- multiple movable application windows;
- minimize, maximize/layout, restore and close controls;
- global status, clock, notifications and settings;
- package applications use the same shell rather than creating their own desktop chrome.

Visual baseline: the Ocean Breeze concept. It uses soft surfaces, an airy blue/cyan accent, scenic wallpapers, compact status information and optional liquid-glass/Aero-like material effects.

## 2. Beta scope

Required for beta:

- one Desktop shell;
- one Window Manager;
- one Taskbar and Launcher model;
- one token-based design system;
- System, Light and Dark themes;
- one accent-color system;
- Liquid material on/off;
- reduced-motion / no-animation support;
- wallpaper support;
- reusable Widget Host and size system;
- desktop layout persistence;
- taskbar app identity and running/minimized/focused state;
- quick status surface with clock, connectivity, notifications and Settings access;
- existing Desktop/Tablet/Mobile responsive model.

Not required for beta:

- taskbar on every screen edge;
- arbitrary per-component visual tuning;
- animated wallpapers;
- third-party themes;
- public widget marketplace;
- advanced virtual desktops;
- user-defined snap templates;
- large built-in widget collections.

Future options must extend the same shell and state models rather than adding parallel implementations.

## 3. Proposed defaults

### Theme

- Default: `System`.
- Supported: `System`, `Light`, `Dark`.
- Light uses soft neutral surfaces with cool blue depth.
- Dark uses deep blue/graphite rather than pure black.
- Default accent: Ocean Blue/Cyan.
- Accent is token-driven globally.

### Liquid material

Liquid ON:

- translucent windows, taskbar and popovers;
- backdrop blur when supported;
- subtle edge highlights;
- restrained shadows/depth;
- wallpaper color may softly influence material;
- active window receives stronger focus/depth;
- inactive windows use calmer, less transparent material for readability.

Liquid OFF:

- identical geometry/layout;
- opaque surfaces;
- no backdrop blur;
- restrained borders/shadows remain.

Liquid is cosmetic only. No behavior or layout may depend on it.

### Simple mode / performance

Reserve three appearance presets:

- `Full`: Liquid + normal restrained motion + full elevation effects.
- `Balanced`: reduced material intensity and restrained motion.
- `Simple`: Liquid off, opaque surfaces, minimal shadows, motion off/reduced.

Beta may expose this as one quality preset plus the accessibility motion setting instead of many advanced switches.

`prefers-reduced-motion` is always respected.

### Geometry

Proposed baseline:

- Fluent-compatible system font stack;
- compact desktop typography;
- 4 px spacing grid;
- 8-12 px normal radius;
- 12-16 px major floating-surface radius;
- subtle 1 px borders when material contrast is insufficient;
- visible focus rings in every theme/material mode.

Exact values must live in central design tokens.

## 4. Icon system

System/navigation icons:

- simple Fluent-like line icons;
- consistent stroke weight;
- filled selected state only where useful;
- neutral default color, accent for selection/state.

Application icons:

- may be more colorful and identifiable;
- one consistent rounded app-icon canvas for JulOS-owned icons;
- package icons must obey shared sizing/padding rules.

Initial size tokens:

- 16 px dense inline/status;
- 20 px normal controls;
- 24 px primary controls;
- 32 px default taskbar application icon;
- 48 px default desktop/launcher application icon;
- 64 px large launcher/accessibility option.

Do not maintain separate Light/Dark icon sets unless a branded icon genuinely requires it.

## 5. Window system

The existing JulOS Window Manager remains authoritative.

Standard title bar:

- application icon;
- title;
- optional context subtitle;
- optional app actions;
- minimize;
- maximize/restore + layout affordance;
- close.

Proposed beta defaults:

- controls on the right;
- stable order: minimize, layout/maximize, close;
- layout/maximize uses the existing snap model;
- double-click title bar toggles maximize/restore;
- active window gets restrained accent/elevation focus;
- inactive windows remain readable and visually quieter;
- geometry is identical between Liquid ON/OFF.

Beta snap targets:

- left half;
- right half;
- four quarters;
- maximize;
- preview before commit.

Future:

- thirds and richer templates;
- user-defined layouts;
- optional left-side window controls;
- always-on-top where application policy allows it.

## 6. Taskbar

Proposed beta default: bottom floating taskbar with JulOS-specific interaction and styling.

Contents:

1. Launcher/Search.
2. Pinned applications.
3. Running applications.
4. Flexible spacer.
5. Notification/problem indicator.
6. Connectivity/global status.
7. Clock/date.
8. Quick Settings/User menu.

Proposed defaults:

- bottom placement;
- centered app section;
- Medium size;
- 32 px app icons;
- running/focused/minimized states visible without relying on color alone;
- multiple windows use one app identity plus count/window picker.

Future-compatible settings:

- Small / Medium / Large;
- left / center app alignment;
- bottom / left / right placement;
- auto-hide;
- optional labels.

## 7. Launcher and search

Beta stays small:

- compact launcher panel;
- search at the top;
- pinned/recent applications;
- all installed applications;
- Settings and Package Manager access.

The existing command/resource search contract remains available for later growth.

## 8. Desktop surface

Desktop supports:

- wallpaper or solid color;
- approved application shortcuts;
- widgets on a grid;
- saved placement per viewport class;
- explicit `Edit desktop` mode for move/resize/remove.

Proposed beta behavior: widgets and shortcuts are locked during ordinary use to avoid accidental movement.

Wallpaper foundation:

- bundled JulOS wallpapers;
- custom user image;
- fit/fill behavior.

Future presentation-only options may include separate Light/Dark wallpapers, dimming, blur and parallax.

## 9. Widget architecture

Widgets remain package-contributed lightweight summaries rendered through the shared Widget Host.

Reusable model:

`WidgetDefinition -> WidgetInstance -> package data/provider -> widget renderer`

WidgetDefinition owns:

- stable type ID;
- title/icon;
- owning package;
- supported sizes;
- configuration schema;
- data/action contract;
- refresh/event behavior.

WidgetInstance owns user presentation state only:

- instance ID;
- widget type ID;
- desktop/viewport identity;
- grid position;
- selected size;
- user configuration.

Core must not learn Docker, Proxmox, Caddy or other package-specific metric structures.

### Widget sizes

Keep the existing semantic model and map it to one responsive grid:

- `Small`: one primary value/state;
- `Medium`: value + secondary value/trend;
- `Wide`: compact list/grouped metrics;
- `Large`: richer summary/problem list.

A widget declares only sizes for which it has a deliberate layout.

### Beta widgets

Keep the beta set intentionally small:

- Clock/Date only if useful beyond the taskbar;
- Host Metrics widget from the Host Metrics package;
- one package/application-status example widget;
- notification/problem summary only if the shared host already supports it cleanly.

Docker/Proxmox/Caddy/Files widgets arrive with their owning packages, not Core.

### Common states

Every widget uses shared shell states:

- loading;
- live;
- stale;
- offline;
- unauthorized;
- error;
- empty.

The Widget Host owns common chrome, focus and edit/resize affordances. Packages own domain content.

## 10. Quick Settings and status

Proposed beta status area:

- time/date;
- Agent/global connectivity summary;
- notifications/problems;
- user/account;
- Quick Settings/Settings.

Quick Settings initial scope:

- Light/Dark/System;
- accent color;
- Liquid or appearance preset;
- motion/reduced motion;
- basic global status.

Network, audio and brightness controls appear only when JulOS actually owns a meaningful capability for them. Do not add decorative fake OS controls.

## 11. Motion

Animation is functional and restrained.

Normal motion may include:

- short hover/focus transitions;
- window open/restore/minimize;
- snap preview;
- launcher/popover entrance;
- widget edit/resize feedback.

Rules:

- no constant decorative motion;
- no long bounce effects;
- no animation that harms technical-data readability;
- reduced motion removes non-essential translation/scale;
- duration/easing come from shared tokens.

## 12. Responsive behavior

Reuse one application model:

- Desktop: free multi-window desktop.
- Tablet: larger targets, reduced placement freedom, simpler snap zones.
- Mobile: one primary maximized app plus task switching and compact widgets.

Do not create a second mobile shell/application model.

## 13. Accessibility baseline

Beta requires:

- keyboard access to launcher, taskbar, window controls and dialogs;
- visible focus rings;
- readable contrast with Liquid ON and OFF;
- reduced-motion support;
- touch-safe targets;
- semantic labels for icon-only controls;
- shell must survive normal browser/UI scaling.

Explicit JulOS UI-scale presets are future work until representative displays are measured.

## 14. Settings structure

Proposed beta Appearance section:

- Theme: System / Light / Dark;
- Accent color;
- Visual effects: Full / Balanced / Simple, or Liquid On/Off depending final decision;
- Motion: Normal / Reduced;
- Wallpaper.

Future:

- taskbar size/alignment/placement;
- desktop shortcut behavior;
- material intensity controls;
- window-control side;
- UI scaling;
- per-theme wallpaper;
- advanced widget layout options.

## 15. Open product decisions

1. Taskbar default: floating compact or full-width edge bar?
2. Launcher: compact popover or larger centered app panel?
3. Desktop shortcuts: enabled by default? Which Core apps should appear?
4. Effects setting: `Full / Balanced / Simple` preset or independent `Liquid` and `Animations` toggles?
5. Liquid default: enabled when supported or opt-in?
6. Window controls: right side only for beta or expose left/right placement immediately?
7. Widget editing: explicit `Edit desktop` mode or direct drag/resize?
8. Notifications: right-side panel or compact anchored popover?
9. Taskbar app icon default: 32 px Medium or another size?
10. Clock: time only with date in popover, or always time + date?
11. Desktop app icon default: 48 px or another size?
12. Density: compact technical or more spacious consumer default?

Until confirmed, these remain product questions rather than architecture requirements.

## 16. Future extension rules

Future personalization extends the same design tokens and state models. It must not create alternate Desktop shells.

Packages may contribute application icons, widgets, Settings sections and app-specific commands.

Packages may not contribute competing taskbars, competing global launchers, alternate window chrome, arbitrary global CSS, global animation systems or unapproved global token overrides.
