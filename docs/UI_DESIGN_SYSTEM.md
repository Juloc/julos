# JulOS UI Design System

Status: Accepted product direction for the next JulOS Desktop iteration. The first implementation scope stays deliberately small while the shared shell and state models remain extensible.

## 1. Product direction

JulOS must feel like its own desktop environment, not like a dashboard and not like a Windows/macOS clone.

The interaction model stays familiar:

- desktop surface with wallpaper;
- optional desktop shortcuts and modular widgets;
- one global taskbar;
- launcher/search;
- multiple movable application windows;
- minimize, maximize/layout, restore and close controls;
- global status, clock, notifications and settings;
- package applications use the same shell rather than creating their own desktop chrome.

Visual baseline: the approved Ocean Breeze direction. It uses soft surfaces, blue/cyan accents, scenic wallpapers, compact status information and optional Liquid/Aero-like material effects.

## 2. First implementation scope

Required for the first UI iteration:

- one Desktop shell;
- one Window Manager;
- one Taskbar and Launcher model;
- one token-based design system;
- System, Light and Dark themes;
- global accent-color system;
- Full / Balanced / Simple appearance presets;
- reduced-motion support;
- wallpaper support;
- reusable Widget Host and size system;
- desktop layout persistence;
- taskbar app identity and running/minimized/focused state;
- quick status surface with clock, connectivity, notifications and Settings access;
- Desktop, Tablet and Mobile support using the same application model.

Not required initially:

- taskbar on every screen edge;
- arbitrary per-component visual tuning;
- animated wallpapers;
- third-party themes;
- public widget marketplace;
- advanced virtual desktops;
- user-defined snap templates;
- large built-in widget collections.

Future options must extend the same shell and state models rather than adding parallel implementations.

## 3. Accepted defaults

### Theme and accent

- Default theme: `System`.
- Supported themes: `System`, `Light`, `Dark`.
- Light uses soft neutral surfaces with cool blue depth.
- Dark uses deep blue/graphite rather than pure black.
- Default accent: Ocean Blue/Cyan.
- Accent is token-driven globally and user-selectable.
- JulOS ships a small curated accent palette for fast selection.
- A custom color picker is also supported for arbitrary user-selected accent colors.
- Custom colors must be normalized through the same contrast/readability rules as curated accents.

### Appearance presets

Use exactly three top-level visual-effect presets:

- `Full`: Liquid enabled, normal restrained motion, full elevation and material effects.
- `Balanced`: reduced material intensity and restrained motion.
- `Simple`: opaque surfaces, Liquid disabled, minimal shadows and reduced/non-essential motion removed.

Default: `Full` when supported.

The user can switch to Simple when transparency, Aero-style effects or animation are unwanted or too expensive.

`prefers-reduced-motion` is always respected independently of the selected preset.

### Liquid material

Liquid ON:

- translucent windows, taskbar and popovers;
- backdrop blur when supported;
- subtle edge highlights;
- restrained shadows/depth;
- wallpaper color may softly influence material;
- active window receives stronger focus/depth;
- inactive windows remain readable and visually quieter.

Liquid OFF:

- identical geometry/layout;
- opaque surfaces;
- no backdrop blur;
- restrained borders/shadows remain.

Liquid is cosmetic only. No behavior, dimensions or layout may depend on it.

### Geometry

Baseline:

- Fluent-compatible system font stack;
- compact desktop typography;
- 4 px spacing grid;
- 8-12 px normal radius;
- 12-16 px major floating-surface radius;
- subtle 1 px borders when contrast requires them;
- visible focus rings in every theme/material mode.

Exact values live in central design tokens.

## 4. Icon system

System/navigation icons:

- simple Fluent-like line icons without copying Windows assets;
- consistent stroke weight;
- filled selected state only where useful;
- neutral default color, accent for selection/state.

Application icons:

- may be more colorful and identifiable;
- package icons obey shared sizing/padding rules;
- JulOS-owned apps support two shared icon canvases: squircle and circle;
- default JulOS-owned app-icon canvas: squircle;
- users may choose circle as an appearance preference without changing icon identity or package metadata.

Initial size tokens:

- 16 px dense inline/status;
- 20 px normal controls;
- 24 px primary controls;
- taskbar sizes configurable in Settings;
- 48 px default launcher application icon;
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

Accepted first-scope behavior:

- controls remain on the right;
- stable order: minimize, layout/maximize, close;
- layout/maximize uses the existing snap model;
- double-click title bar toggles maximize/restore;
- active window gets restrained accent/elevation focus;
- inactive windows remain readable and visually quieter;
- geometry is identical between Liquid ON/OFF.

Initial snap targets:

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

Both forms are supported by one taskbar model:

- floating compact taskbar;
- full-width edge taskbar.

Default: floating compact taskbar at the bottom.

Contents:

1. Launcher/Search.
2. Pinned applications.
3. Running applications.
4. Flexible spacer.
5. Notification/problem indicator.
6. Connectivity/global status.
7. Clock/date.
8. Quick Settings/User menu.

Accepted behavior:

- bottom placement for the first scope;
- centered app section by default;
- taskbar size is configurable already in the first scope;
- Small / Medium / Large sizes;
- auto-hide is included in the first scope;
- running/focused/minimized states visible without relying on color alone;
- multiple windows use one app identity plus count/window picker.

Initial taskbar size tokens are deliberately easy to retune after real-device testing:

- `Small`: 40 px bar / 24 px app icon;
- `Medium`: 48 px bar / 32 px app icon;
- `Large`: 60 px bar / 40 px app icon;
- default: `Medium`.

Future-compatible settings:

- left / center app alignment;
- left / right placement;
- optional labels.

## 7. Launcher and search

Both launcher presentations use the same app/search model:

- compact launcher panel;
- larger centered launcher.

Default: compact launcher panel.

Initial contents:

- search at the top;
- pinned/recent applications;
- all installed applications;
- Settings and Package Manager access.

The existing command/resource search contract remains available for later growth.

## 8. Desktop surface

Desktop supports:

- wallpaper or solid color;
- optional application shortcuts;
- widgets on a grid;
- saved placement per viewport class;
- explicit `Edit desktop` mode for move/resize/remove.

Default desktop: no application shortcuts. The normal desktop starts visually clean and users add shortcuts if wanted.

Widgets and shortcuts are locked during normal use.

Desktop edit mode is entered through either:

- a normal explicit `Edit desktop` command;
- press-and-hold on an unused desktop area on touch/pointer devices.

Press-and-hold must not conflict with normal application/window interaction and must have keyboard-accessible equivalent actions.

Desktop shortcuts use configurable labels:

- `Always`: label is always shown below the shortcut;
- `On focus/hover`: label stays compact until pointer hover or keyboard focus/selection;
- default: `Always`;
- touch interaction must never depend on hover to reveal the app identity.

Wallpaper foundation:

- bundled JulOS wallpapers;
- custom user image;
- fit/fill behavior;
- separate Light and Dark wallpaper selection.

Future presentation-only options may include dimming, blur and parallax.

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
- refresh/event behavior;
- supported presentation styles.

WidgetInstance owns user presentation state only:

- instance ID;
- widget type ID;
- desktop/viewport identity;
- grid position;
- selected size;
- selected presentation style;
- user configuration.

Core must not learn Docker, Proxmox, Caddy or other package-specific metric structures.

### Widget grid

Use a relatively fine shared desktop grid rather than large phone-style tiles.

- widgets remain aligned and collision-aware;
- the fine grid allows visually flexible placement;
- widgets still use semantic supported sizes rather than arbitrary pixel dimensions;
- exact grid-unit size and outer margins are design tokens and may be tuned after real-device testing.

### Widget sizes

Use one responsive grid with semantic sizes:

- `Small`: one primary value/state;
- `Medium`: value + secondary value/trend;
- `Wide`: compact list/grouped metrics;
- `Large`: richer summary/problem list.

A widget declares only sizes for which it has a deliberate layout.

### Widget presentation

Two shared presentation styles are supported:

- `Card`: default. The Widget Host provides the common JulOS surface, radius, border, elevation and Liquid/opaque material behavior.
- `Borderless`: optional. Widget content sits directly on the desktop without the normal card surface, useful for clocks, dates, weather and similarly lightweight content.

Rules:

- every widget supports `Card` unless there is a documented reason not to;
- a widget may advertise `Borderless` only when it has a deliberate readable layout for that style;
- users may switch between the styles only when the widget declares both;
- packages provide content, not arbitrary outer chrome or global CSS;
- the Widget Host owns contrast handling, focus/edit affordances and accessibility in both styles;
- Borderless widgets must remain readable across user wallpapers and Light/Dark modes;
- Liquid affects `Card` material only and never changes widget behavior or geometry.

### Initial widgets

Keep the initial set intentionally small:

- Host Metrics widget from the Host Metrics package;
- one package/application-status example widget;
- notification/problem summary only when it fits cleanly;
- Clock/Date widget only if it adds value beyond the taskbar.

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

## 10. Notifications, problems and status

Use both transient and persistent presentation:

- short toast/popover for a newly arrived notification;
- notification/problem panel for history, unresolved items and deeper inspection.

Persistent operational faults belong in Problem Center rather than becoming endless toast spam.

Status area:

- time/date;
- notifications/problems;
- user/account;
- Quick Settings/Settings.

Host Connector state is shown under Settings → Hosts → Host access and through actionable Problems. It is not permanent decorative status chrome.

Quick Settings initial scope:

- Light/Dark/System;
- accent color;
- Full/Balanced/Simple appearance preset;
- motion/reduced motion;
- basic global status.

Network, audio and brightness controls appear only when JulOS actually owns a meaningful capability for them. Do not add decorative fake OS controls.

## 11. Clock

Default taskbar clock shows time only.

Date and additional detail appear when opening the clock/status surface.

Clock presentation is configurable so users may choose persistent time + date.

Locale, 12/24-hour presentation and date formatting follow user/localization settings rather than being hard-coded.

## 12. Density and scaling

Two density presets are included:

- `Compact`: default; technical and space-efficient without becoming an admin-console layout;
- `Comfortable`: larger spacing and controls for users who prefer a more relaxed layout.

The implementation must use shared density/size tokens rather than component-specific magic values.

Taskbar size is independently configurable as Small / Medium / Large.

Exact Compact/Comfortable token values may be retuned after representative desktop, tablet and mobile testing without changing the component model.

Explicit broader UI scale presets may be added later after representative display testing.

## 13. Motion

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
- duration/easing come from shared tokens;
- Simple mode remains fully usable without animation.

## 14. Responsive and input behavior

Desktop, Tablet and Mobile remain application viewport classes. Persisted Shell presentation uses Phone, Tablet, desktop-single and desktop-multi Workspace classes from `MOBILE_PWA.md`.

All essential functionality must be usable with the input methods reasonably available on each device.

### Desktop

- full free multi-window desktop;
- move/resize/snap;
- keyboard and mouse operation;
- launcher/taskbar/window actions keyboard-accessible.

### Tablet

- touch-safe controls;
- larger interaction targets where needed;
- maximized or split windows by default with at least two visible applications;
- free windows when screen area and precise pointer input permit;
- keyboard, trackpad, pen and mouse remain first-class;
- press-and-hold can enter desktop edit mode.

### Mobile

- one primary maximized application at a time by default;
- explicit Split with at most two visible applications and a touch-safe divider;
- reliable task switching;
- compact widgets where useful;
- no tiny desktop controls merely scaled down from desktop;
- all core user journeys remain accessible.

Background surfaces suspend by default. The open-app menu may expose best-effort Keep active when the package declares support. Shell Back behavior is overlay → app → split/task → workspace → platform exit.

Do not create a second mobile application model or separate mobile-only product shell.

## 15. Accessibility baseline

Initial scope requires:

- keyboard access to launcher, taskbar, window controls and dialogs;
- visible focus rings;
- readable contrast with Liquid ON and OFF;
- reduced-motion support;
- touch-safe targets;
- semantic labels for icon-only controls;
- normal browser/UI scaling must remain usable;
- no interaction that exists only through hover;
- press-and-hold actions have discoverable non-gesture alternatives.

## 16. Settings structure

Initial Appearance/Desktop settings:

- Theme: System / Light / Dark;
- Accent: curated JulOS palette + custom color picker;
- App icon shape: Squircle / Circle;
- Visual effects: Full / Balanced / Simple;
- Motion: Normal / Reduced;
- Wallpaper: bundled/custom and separate Light/Dark selection;
- Taskbar style: Floating / Full width;
- Taskbar size: Small / Medium / Large;
- Taskbar auto-hide: On / Off;
- Launcher style: Compact / Large centered;
- Clock: Time only / Time + Date;
- Density: Compact / Comfortable;
- Desktop shortcut labels: Always / On focus-hover;
- Desktop edit mode entry and desktop shortcut management;
- per-widget presentation style: Card / Borderless when supported by that widget.

Future:

- taskbar side placement/alignment;
- material intensity controls;
- window-control side;
- broader UI scaling;
- advanced widget layout options;
- animated/parallax wallpapers;
- virtual desktops and custom snap templates.

## 17. Product decisions complete for initial visual implementation

The major shell and presentation decisions required for the first visual implementation are now accepted.

Remaining numerical values such as exact spacing, density, grid-unit size and material intensity are implementation tokens. They may be tuned through real desktop, tablet and mobile testing without changing the accepted component architecture or user-facing model.

## 18. Future extension rules

Future personalization extends the same design tokens and state models. It must not create alternate Desktop shells.

Packages may contribute application icons, widgets, Settings sections and app-specific commands.

Packages may not contribute competing taskbars, competing global launchers, alternate window chrome, arbitrary global CSS, global animation systems or unapproved global token overrides.
