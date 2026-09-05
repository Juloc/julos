# UX specification

## 1. Design goal

JulOS must feel like a fast, simple desktop environment rather than a dashboard with cards. The interface uses Fluent 2 principles, restrained animation, clear hierarchy and compact controls suitable for technical data.

The desktop shell remains usable when packages are unavailable. Package applications follow the same design tokens and interaction rules.

## 2. Global layout

```text
Desktop surface
├─ Desktop widgets
├─ Application windows
├─ Snap preview layer
├─ Notification and problem overlays
└─ Taskbar
   ├─ Launcher
   ├─ Search and command palette
   ├─ Running applications
   ├─ Session indicators
   └─ Status area
```

The taskbar is always available on desktop unless a user enters explicit full-screen session mode.

## 3. Desktop surface

The desktop surface contains:

- optional background image or neutral color
- widgets placed on a grid
- optional approved application shortcuts
- no permanent infrastructure navigation sidebar

Infrastructure and package navigation belongs inside applications or the launcher. The desktop must not become a dense monitoring dashboard.

## 4. Launcher and search

The launcher contains:

- installed applications
- approved discovered applications
- recent applications
- package manager
- settings
- session manager
- problem center

Search covers:

- applications
- infrastructure resources
- connections
- current problems
- commands the current user may execute

Search results show the owning package and target context. Unauthorized actions are not displayed as executable commands.

## 5. Window chrome

Every standard window contains:

- application icon
- title
- optional connection or resource subtitle
- application actions area
- minimize
- maximize or restore
- close

Desktop title bars present these as one standard three-control group. Windows/Linux use right-side window controls; macOS uses left-side traffic-light placement. Full-screen remains a distinct state but is not a fourth title-bar button; `F11` toggles it. Window chrome must not imitate browser tabs unless the application itself is Browser.

### 5.1 Window states

- `Normal`
- `Minimized`
- `Maximized`
- `SnappedLeft`
- `SnappedRight`
- `SnappedTopLeft`
- `SnappedTopRight`
- `SnappedBottomLeft`
- `SnappedBottomRight`
- `FullScreen`

The stored state includes restore bounds for maximized, snapped and full-screen transitions.

### 5.2 Focus

- clicking a window focuses it and raises its z-order
- opening a single-instance application focuses its existing window
- taskbar selection restores a minimized window
- modal dialogs remain scoped to their owning window where possible
- critical global dialogs are rare and must identify the affected application

### 5.3 Move and resize

- pointer move begins only from draggable title-bar regions
- interactive title-bar controls never begin a drag
- resize handles remain reachable with mouse and touch
- movement is constrained so the title bar cannot become permanently unreachable
- minimum size comes from the application definition
- remote displays receive a debounced resize after the user stops resizing

### 5.4 Snapping

Supported snap targets:

- left half
- right half
- four quarters
- maximize by top edge or title-bar action

Snap preview appears before release. The complete floating taskbar footprint, including its bottom offset and safe-area inset, is excluded from usable bounds.

A snapped window can be restored by dragging its title bar. Restore size and pointer-relative position must feel predictable.

## 6. Multi-window behavior

Applications declare:

- single instance per user
- single instance per target resource
- unrestricted multiple instances

Examples:

- Settings: single instance per user
- Proxmox VM console: single instance per VM unless parallel sessions are explicitly supported
- Browser: multiple instances
- Files: multiple instances with different providers or paths

Window titles must distinguish multiple instances.

## 7. Taskbar

Running applications are grouped by application definition. Multiple windows show a count and window picker.

Taskbar item states:

- running
- focused
- minimized only
- reconnecting
- attention required
- session active
- faulted

The status area contains only global items:

- notifications
- problems
- current user
- time
- settings or power menu

Host Connector administration belongs under Settings → Hosts → Host access. Connector failures appear through Problems/Notifications and inside the package that needs the host; a Connector is not a launcher application or permanent taskbar item.

## 8. Widgets

Widgets are lightweight summaries, not full applications.

### 8.1 Widget rules

- fixed supported size variants declared by the package
- grid placement, not unrestricted pixel positioning
- package-owned data and actions
- one clear primary value or status
- click opens the corresponding application or resource
- no complex editing inside a widget
- explicit loading, stale, offline, unauthorized and error states

### 8.2 Initial widget sizes

- Small: one value and label
- Medium: value, trend or secondary status
- Wide: compact list or grouped metrics
- Large: problem list or infrastructure summary

Widgets may define only size variants that have a real layout.

### 8.3 Refresh behavior

- live values use events where available
- periodic refresh shows last observation time
- hidden or minimized browser tabs reduce refresh frequency
- widget errors do not trigger global notification spam

## 9. Problem center

The problem center supports:

- severity filters
- source package filters
- resource filters
- active, acknowledged and resolved states
- grouped repeated observations
- deep links to the owning application
- suggested action
- audit-linked remediation result

Severity presentation:

- Information: neutral
- Warning: attention needed, service still usable
- Error: function unavailable or unhealthy
- Critical: data loss, security or broad availability risk

Color is not the only indicator. Every severity has text and icon semantics.

## 10. Notifications

Notifications are for changes that need user awareness. Persistent operational faults belong in Problem Center.

Examples:

- package installation completed
- browser session terminated by inactivity policy
- Host Connector enrolled
- backup completed or failed
- destructive action completed

Duplicate notifications from repeated observations are suppressed.

## 11. Store and package manager UX

The Store presents catalog applications and JulOS extension packages together while naming the selected delivery clearly: connect existing service, install image, install Compose or install native extension.

Package detail page shows:

- name, publisher and version
- signature status
- catalog source and stale state
- core compatibility
- applications and widgets contributed
- permissions requested
- capabilities provided and required
- storage and runtime requirements
- configuration state
- health and logs
- update notes

Unsigned or unknown-publisher content shows one concise warning with source, digest and runtime-right summary, followed by **Install anyway** for an authorized administrator. An invalid claimed signature is shown as corrupted and cannot run.

Docker/Compose preview additionally shows selected host, ports, networks, mounts, devices, privileged settings, data ownership and the exact resources JulOS will manage. Connection-only delivery states that no service will be deployed or deleted.

Lifecycle actions:

- install
- configure
- enable
- disable
- update
- repair
- remove

Remove distinguishes:

- remove runtime and retain package data
- remove runtime and delete package data

Destructive removal requires re-authentication or strong confirmation when secrets, profiles or operational state are deleted.

Managed application removal separately offers retain data, back up then remove owned data, or remove owned data. External, shared and adopted resources default to retain.

## 12. Browser application UX

### 12.1 Full browser mode

Contains:

- tab strip
- address field
- back, forward, reload and stop
- certificate and connection status
- downloads
- browser menu
- developer tools action when allowed

### 12.2 Application mode

Contains the website content and minimal session controls. The configured application name and icon replace generic Browser branding.

An action can reveal the current address and open the same session in full browser mode when policy allows.

### 12.3 Browser session states

- starting runtime
- connecting display
- active
- reconnecting
- suspended
- terminated
- failed

Startup progress must identify which stage is taking time.

## 13. Remote session UX

Remote windows contain an optional compact toolbar for:

- connection status
- display scaling
- send special keys
- clipboard
- file or drive redirection
- reconnect
- disconnect
- terminate
- full screen

Toolbar can auto-hide in full-screen mode but must remain keyboard-accessible.

Closing a window with an active session shows the selected lifecycle action when termination would lose session state.

## 14. Files UX

File Manager uses a familiar two-pane-capable layout without forcing two panes.

Required areas:

- provider and location navigation
- breadcrumb path
- file list with virtualized rows
- details or preview panel
- transfer queue
- conflict and permission dialogs

File operations show provider-specific errors without exposing credentials. Transfers continue when the file window is minimized and appear in a global transfer indicator.

## 15. Infrastructure applications

Docker and Proxmox applications share interaction patterns:

- resource tree or scoped switcher
- overview
- current status
- problems
- actions
- recent tasks or logs

Write actions are visually separated from read views. Dangerous actions use explicit names such as `Stop VM` rather than ambiguous icons.

## 16. PWA and responsive behavior

JulOS is installable as a PWA and remains usable as a normal browser tab. Offline mode is not simulated: disconnected state permits Retry and troubleshooting but no fake successful mutation.

### Desktop viewport

- full taskbar
- free window placement
- snapping
- desktop widgets
- context menus

### Tablet viewport

- taskbar remains
- windows default to maximized or split and at least two visible applications are supported
- free windows are available when screen area and pointer capabilities permit
- resize and drag handles are enlarged
- command palette remains available

### Mobile viewport

- one primary application by default
- explicit Split shows at most two foreground applications
- Portrait splits top/bottom; Landscape splits left/right
- task switcher replaces free overlapping windows
- widgets move to a scrollable overview page
- remote applications default to full screen
- Phone, Tablet, desktop-single and desktop-multi layouts remain separate
- shared layouts may be overridden per registered client device

### 16.1 Surface execution

Phone backgrounds suspend by default. A Shell action on the open application offers **Keep active in background** when the package supports it. Suspension stops frontend timers, polling, rendering and presentation connections without implicitly terminating Browser/Remote sessions or durable Operations. Mobile operating systems may still freeze the complete PWA; the UI never promises guaranteed client-side background execution.

## 17. Accessibility

- complete keyboard navigation for shell and standard controls
- visible focus indicators
- semantic HTML and accessible names
- high-contrast compatibility
- reduced-motion support
- minimum touch target size of 44 CSS pixels for primary mobile actions
- zoom up to 200% without losing core actions
- screen-reader announcements for connection and problem-state changes

## 18. Keyboard shortcuts

Initial shell shortcuts:

```text
Ctrl+Space        Open command palette
Alt+Tab           Switch JulOS windows
Alt+Shift+Tab     Switch backward
Meta+ArrowLeft    Snap left
Meta+ArrowRight   Snap right
Meta+ArrowUp      Maximize
Meta+ArrowDown    Restore or minimize
Ctrl+Alt+N        Open notification center
Ctrl+Alt+P        Open problem center
Escape            Close active transient overlay
```

Shortcuts that conflict with a remote session are captured only when the JulOS session toolbar is active or the user invokes a documented escape chord.

## 18.1 Back behavior

System Back, browser history and supported mouse Back buttons enter the same Shell dispatcher:

1. close the top transient dialog/menu;
2. invoke the active application's registered Back handler;
3. collapse Phone split/detail history;
4. return from task switcher/application view to the workspace;
5. allow normal browser/PWA exit only at the JulOS Root.

Packages do not mutate top-level browser history. Proxied applications participate only through the versioned runtime bridge; streamed Browser handles its own page navigation.

## 19. Theme and motion

Themes:

- system
- light
- dark

Motion settings:

- enabled or reduced
- speed setting only when it affects all shell transitions consistently
- no animation for continuous infrastructure updates
- window movement follows input directly and is not animated

Animations communicate state changes; they are not decorative blockers.

## 20. Empty and failure states

Every application specifies:

- first-run state
- no-data state
- unauthorized state
- offline state
- configuration-required state
- faulted-package state

Empty states contain the next valid action. They must not instruct users to perform actions they lack permission to execute.
