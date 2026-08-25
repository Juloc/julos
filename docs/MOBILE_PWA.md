# Mobile and PWA contract

Status: Accepted target specification. The current responsive Desktop is the implementation starting point; it is not yet an installable PWA and does not yet implement this device, split-view or surface-lifecycle model.

## 1. Product role

JulOS Desktop is delivered as one installable Progressive Web Application. The PWA is the primary JulOS client on phones and tablets. Installation is optional; the same Shell remains usable in a normal browser tab.

JulOS does not maintain a native mobile client or a second mobile-only application model.

JulOS is not an offline control plane. Without Server connectivity it shows a truthful disconnected state and allows no mutation that pretends to have succeeded.

## 2. Three separate concepts

JulOS separates:

1. **Application viewport class** — compatibility declared by an application: `desktop`, `tablet` or `mobile`.
2. **Workspace class** — persisted Shell presentation: `phone`, `tablet`, `desktop-single` or `desktop-multi`.
3. **Client device** — one explicitly registered browser/PWA installation used only for layout preferences, never authentication.

Mapping:

| Workspace class | Application viewport class |
|---|---|
| `phone` | `mobile` |
| `tablet` | `tablet` |
| `desktop-single` | `desktop` |
| `desktop-multi` | `desktop` |

Automatic workspace classification is deterministic and uses only current presentation capabilities:

1. if the primary pointer is coarse, no fine pointer exists and `min(screen.width, screen.height) < 600` CSS px, choose `phone`;
2. otherwise, if the primary pointer is coarse and no fine pointer exists, choose `tablet`;
3. otherwise, if the layout viewport width is below 600 CSS px, choose `phone`;
4. otherwise, if it is below 1024 CSS px, choose `tablet`;
5. otherwise choose `desktop-single`.

`desktop-multi` is entered only through the explicit Multi-Display controller when at least two display participants are active. Layout viewport means `document.documentElement.clientWidth`, not `VisualViewport`; a software keyboard therefore cannot change workspace identity. The coarse-device minimum screen dimension remains stable across orientation, so a Phone does not become a Tablet in landscape. A stored device override is authoritative. User-agent or hardware fingerprinting is forbidden.

## 3. Client device registration

On first authenticated use, Server creates a cryptographically random 256-bit client instance key, stores only its hash, sets the value in a Secure, HTTP-only, SameSite-Strict cookie and returns an opaque `ClientDeviceId`. Desktop JavaScript never receives the raw key. The key identifies layout preferences only and is not an authentication credential.

`ClientDevice`:

```text
ClientDeviceId
OwnerUserId
ClientInstanceKeyHash
DisplayName
LastDetectedWorkspaceClass
WorkspaceClassOverride          nullable phone | tablet | desktop-single
CreatedAtUtc
LastSeenAtUtc
Revision
```

Rules:

- device records are always scoped to the authenticated user;
- clearing site data creates a new device identity;
- losing the device cookie does not invalidate the JulOS user session;
- a user can rename and remove devices in Settings;
- removing a device removes only device-scoped preferences/layouts, never shared layouts or application data;
- device identity is not accepted as authorization for any other resource.

## 4. Layout resolution

A user has one shared layout per workspace class by default. Each client device may choose:

```text
LayoutScope    shared | device
RestoreMode   resume | fresh
```

Resolution:

1. Load the current Client Device by the owner-scoped cookie.
2. Calculate `DetectedWorkspaceClass` from the fixed matrix and update `LastDetectedWorkspaceClass` without changing layout revision.
3. Resolve the base class from `ClientDevice.WorkspaceClassOverride ?? DetectedWorkspaceClass`; override cannot be `desktop-multi`.
4. If the user explicitly entered Multi-Display and at least two participants are active, resolve `desktop-multi`; otherwise use the base class.
5. Load the current device's preference for that resolved workspace class.
6. If `RestoreMode = fresh`, start with no restored windows and do not persist window state.
7. If `LayoutScope = device`, load the user/device/workspace layout.
8. Otherwise load the user/shared/workspace layout.
9. If no layout exists, create an empty revisioned layout in the selected scope.

Switching workspace class is atomic: flush the old writable layout, stop its presentation scheduler, load the new authoritative layout, then render. A resize or orientation event must never write geometry into a different workspace class.

`DeviceWorkspacePreference`:

```text
ClientDeviceId
WorkspaceClass
LayoutScope
RestoreMode
Revision
```

## 5. Layout persistence model

`DesktopLayout` target fields:

```text
DesktopLayoutId
UserId
WorkspaceClass
ClientDeviceId               null for shared layout
Name
PresentationMode             freeform | tiled | phone-empty | phone-single | phone-split
PrimaryWindowId              nullable
SecondaryWindowId            nullable
SplitRatioPermille           nullable; 250..750 only for phone-split
DisplayCount
Revision
UpdatedAtUtc
```

`DesktopWindow` adds:

```text
WorkspaceClass               persisted copy of parent layout class
DisplaySlot                  stable zero-based logical display position
```

State matrix:

| Workspace/mode | Primary | Secondary | Split ratio |
|---|---|---|---|
| Phone `phone-empty` | null | null | null |
| Phone `phone-single` | required | null | null |
| Phone `phone-split` | required | required and different | 250–750 |
| Tablet/Desktop `freeform` or `tiled` | null | null | null |

Enforcement matrix:

| Rule | PostgreSQL and SQLite | Domain/Application |
|---|---|---|
| one shared layout | Partial unique index `(user_id, workspace_class) WHERE client_device_id IS NULL` | owner-scoped resolver |
| one device layout | Partial unique index `(user_id, client_device_id, workspace_class) WHERE client_device_id IS NOT NULL` | owner-scoped resolver |
| one shared/device execution preference | Separate partial unique indexes on `(user_id, application_definition_id, workspace_class)` for null device and the same tuple plus non-null device | owner-scoped preference resolver |
| device belongs to user | Composite foreign key `(user_id, client_device_id)` to `(owner_user_id, client_device_id)` | authenticated-user check |
| Primary/Secondary belong to layout | Deferred composite foreign keys `(desktop_layout_id, primary_window_id/secondary_window_id)` to `(desktop_layout_id, window_id)` | aggregate validation before save |
| Phone/mode nullability and ratio | Check constraint implementing the state matrix | transition methods reject invalid states |
| stable display slot | Composite FK `(desktop_layout_id, workspace_class)` to the immutable parent class, then check `display_slot >= 0 AND (workspace_class = 'desktop-multi' OR display_slot = 0)` | topology resolver assigns one active display owner per Window |
| `fresh` cannot persist | no row is selected for write | API rejects writes with `desktop.layout_persistence_disabled` |

`DesktopLayout.WorkspaceClass` is immutable because each class is a different layout identity. Migration backfills each Window's persisted class from its parent before adding the composite parent key and Window foreign key; application writes never accept a separate client-supplied Window class. No database upper bound ties `DisplaySlot` to current `DisplayCount`: a non-negative slot for a temporarily absent display is retained, presented on slot zero for the current session, and restored to its persisted slot when that participant returns. Provider-specific migration SQL is allowed only for equivalent partial indexes/deferred constraints and is covered by real PostgreSQL and SQLite fixtures. Neither provider may weaken the logical rule.

The current viewport-only layouts migrate as follows:

```text
desktop → shared desktop-single
tablet  → shared tablet
mobile  → shared phone
```

No physical multi-monitor topology is invented during migration. The first `desktop-multi` layout is explicitly initialized by copying `desktop-single` or starting empty.

For each migrated Mobile layout, retain every Window, bounds, z-order and revision. Select Primary deterministically as the non-minimized Window with highest z-order, breaking ties by `WindowId`; if none is eligible use `phone-empty`, otherwise use `phone-single`. Secondary remains null because Split is always an explicit user action. All other retained Windows start in the task switcher/background policy and are not deleted. The migration never fabricates Split from an old layout containing several Windows.

## 6. Phone presentation

A phone has at most two foreground windows.

### Single mode

- one Primary window occupies the available stage;
- taskbar/navigation remains reachable through touch-safe Shell controls;
- opening an app replaces the visible stage unless the user selects Split.

### Split mode

- the user explicitly selects **Open in split** or drags an app from the task switcher into the secondary slot;
- Primary and Secondary are both visible;
- portrait uses top/bottom; landscape uses left/right;
- the divider persists a ratio from 25% to 75%;
- focus belongs to exactly one pane;
- opening a third app replaces the focused pane and moves the replaced window to its configured background execution state;
- all open windows remain in the task switcher.

Orientation changes presentation geometry only. They do not create, select or overwrite another layout.

## 7. Tablet presentation

Tablet uses the desktop window and application model with touch-first defaults:

- windows default to maximized or split/tiled presentation;
- at least two visible applications are supported;
- snapping, taskbar switching and keyboard shortcuts remain available;
- title bars, resize handles and drop zones are touch-safe;
- keyboard, trackpad, pen and mouse use the same Pointer Events path;
- free window placement is enabled when sufficient area and precise pointer input are available or the user opts in.

There is no iPad-specific JulOS implementation. iPadOS receives the tablet workspace with capability-aware input behavior.

## 8. Multi-display presentation

`desktop-multi` persists stable logical `DisplaySlot` values. Runtime `BroadcastChannel` participant IDs remain ephemeral and never enter persisted window identity.

On workspace start:

1. active displays negotiate an ordered runtime topology;
2. logical slots are assigned or recovered;
3. each durable window is rendered by one active display owner;
4. a missing slot is recovered onto the earliest surviving display without rewriting its stored slot until the user persists a new placement.

The same user can keep distinct `desktop-single` and `desktop-multi` layouts. A multi-monitor session never overwrites the single-display layout.

## 9. Three lifecycles

Window presentation, frontend-surface execution and runtime-session state are independent.

### Window presentation

Examples: normal, minimized, snapped, phone Primary and phone Secondary.

### Surface execution

```text
foreground-focused
foreground-visible
background-active
suspended
faulted
terminated
```

### Runtime session

Examples: Browser, RDP, VNC, SSH or container-terminal session states owned by their package.

Changing one lifecycle never implies a transition in another unless an explicit documented policy requests it.

## 10. Surface contract

Every mobile-capable entry in package manifest `Applications[]` declares the case-sensitive `Surface` object:

```json
{
  "Surface": {
    "ContractVersion": "1.0.0",
    "SupportedBackgroundModes": ["suspend", "keep-surface-active"],
    "HandlesBack": true
  }
}
```

Host-side interface semantics:

```text
activate(SurfaceContext, AbortSignal)       -> Promise<void>
deactivate(SurfaceReason, AbortSignal)      -> Promise<void>
suspend(SurfaceReason, AbortSignal)         -> Promise<void>
resume(SurfaceContext, AbortSignal)         -> Promise<void>
handleBack(BackContext, AbortSignal)        -> Promise<handled | not-handled>
dispose(SurfaceReason, AbortSignal)         -> Promise<void>
```

`SurfaceContext` contains Window ID, Workspace Class, `focused | visible` presentation, bounds and revision; it contains no secret or runtime credential. `SurfaceReason` is one of `window-opened`, `presentation-changed`, `window-backgrounded`, `workspace-changed`, `user-requested`, `page-hidden`, `window-closed` or `shell-dispose`. `BackContext` contains only input source and a monotonically increasing Shell navigation sequence.

Rules:

- transitions for one Surface are serialized; repeating a completed transition to the same state is idempotent;
- a newly created Surface completes `activate` before it receives input;
- a visible but unfocused Phone Split/Tablet pane is `foreground-visible`, receives no keyboard focus and is not deactivated;
- leaving the visible foreground runs `deactivate`, followed by `suspend` only when the resolved Background Mode is `suspend`;
- entering foreground from `suspended` runs `resume` then `activate`; from `background-active` it runs `activate` only;
- `deactivate` means loss of visible presentation; it is not disposal;
- `suspend` stops timers, polling, rendering, input listeners and display connections owned by the surface;
- `resume` re-reads authoritative data and reconnects presentation as needed;
- `dispose` runs only when the window/surface is actually destroyed;
- lifecycle calls have a 2-second deadline; Back has 500 ms. Shell aborts on deadline;
- activate/resume rejection enters `faulted` and shows a retryable error Surface; suspend/dispose timeout forcibly tears down only the frontend realm and records `package.surface_timeout`, never a runtime Session;
- rejected/timed-out Back is `not-handled`, records a bounded package failure and continues Shell navigation;
- dispose is terminal and later calls fail with `package.surface_terminated`;
- internal app state belongs to the package and is not stored as arbitrary package JSON in Core layouts;
- unsupported major versions fail clearly; there is no silent mobile lifecycle fallback.

Browser and Remote must implement this contract before Phone enables suspension by default. Their current element-disconnect cleanup must be separated from session termination.

## 11. Background execution preference

Default Phone behavior is `suspend`. A user can choose **Keep active in background** from the open app's Shell menu. An application cannot enable this for itself.

`ApplicationExecutionPreference`:

```text
UserId
ApplicationDefinitionId
WorkspaceClass
ClientDeviceId               null for shared preference
BackgroundMode               suspend | keep-surface-active
Revision
```

Manifest support, stored preference and resolved Surface state map exactly: `suspend -> suspended`; `keep-surface-active -> background-active`. An application cannot request or persist a mode. `keep-surface-active` is best effort while the JulOS page is alive. Browsers and mobile operating systems may freeze or terminate the entire PWA. Reliable work must run as a durable server-side Operation in Core, a package worker or Host Connector. After resume, Desktop reads the Operation and application state again.

## 12. Operation Center

Desktop exposes queued and running Operations independently from their originating window. `operation.changed` is a small SignalR event containing identity and revision; Desktop then fetches authoritative state.

Closing, suspending or reloading a surface does not cancel an Operation. Cancellation is a separate permission-checked action through the existing Operation API.

## 13. Shell-owned back navigation

All supported back input enters one `ShellNavigationController`:

1. dismiss the top dialog, menu or transient overlay;
2. invoke the active application's registered `handleBack`;
3. collapse app detail or Phone split focus/history;
4. leave the task switcher or active app view for the workspace;
5. at the JulOS root, allow that browser/operating-system Back action to leave.

Inputs:

- Android/system Back gesture;
- browser `popstate`/Navigation API events;
- supported mouse Back button;
- Shell keyboard shortcut where applicable.

History state machine:

1. On Shell boot, `replaceState` marks the current entry `{ julos: 1, epoch, sequence: 0, kind: "root" }`; it does not push a guard entry.
2. Each Shell-owned back-consumable layer pushes exactly one entry with the same random page `epoch`, next `sequence`, `kind` and opaque non-secret layer token.
3. A package that creates internal navigation calls the Surface bridge `pushBackEntry(token)`; it never calls top-level `history.pushState` itself.
4. Shell Back controls call `history.back()` when sequence is above zero. Android/system and mouse Back reach the same logic through `popstate`.
5. `popstate` is treated as an already-completed history move, not a cancellable event. The controller serially reconciles the old stack down to the target sequence using the dispatcher order. A browser jump across several entries unwinds each departed JulOS layer once, newest first.
6. `handleBack` receives the departed sequence and 500 ms AbortSignal. `not-handled`, rejection or timeout lets Shell close/collapse that layer; no handler can restore a sentinel or trap history.
7. A stale epoch (reload/BFCache) causes Shell to re-resolve authoritative workspace state and replace only the current entry as sequence zero. Reentrant `popstate` is queued until the current reconciliation finishes.
8. At sequence zero Shell has no guard entry. Back reaches the previous external history entry or lets the standalone PWA/operating system leave immediately.

Programmatic forward navigation truncates any abandoned in-memory layer stack exactly as browser history truncates forward entries. History state contains no URL credential, secret, package state or runtime descriptor.

A proxied web application participates only through the versioned JulOS runtime message bridge. Streamed Browser sends Back to its own browser session. Remote Android sends the Android Back key through its protocol. SSH and container terminals return `not-handled`, allowing Shell navigation.

## 14. PWA assets and caching

Required installability:

- standards-compliant web manifest;
- product icons and maskable icons;
- `display: standalone` with normal browser-tab compatibility;
- theme/background colors;
- stable start URL within the authenticated Shell;
- service worker registered by the Shell.

The service worker may cache only versioned immutable Shell assets and a non-sensitive disconnected document. It must not persistently cache:

- authenticated API responses;
- HTML containing user state;
- antiforgery or authentication responses;
- Secret Reference material;
- operation, session, runtime or display traffic;
- package frontend modules without their version/integrity identity;
- proxied application responses.

An offline navigation displays **JulOS is not reachable** with Retry and local troubleshooting guidance. Mutations remain disabled.

Update handshake:

1. A waiting worker sends `JULOS_UPDATE_READY { buildId }` to every controlled Window Client; cached assets are already digest-verified.
2. A page answers `JULOS_UPDATE_STATUS { buildId, clientId, layoutState }`, where layout state is `clean`, `dirty`, `conflict` or `fresh`.
3. User acceptance sends `JULOS_ACTIVATE_UPDATE { buildId }`. Activation may change the controller but never calls `location.reload()` from the worker.
4. Each page independently flushes a dirty writable layout with expected revision before reloading. Success sends `JULOS_CLIENT_READY_TO_RELOAD`; `fresh`/`clean` may reload immediately.
5. Offline or `409` conflict leaves that page running and displays Retry/Resolve; it does not block other clients and no loop repeatedly reloads it.
6. **Reload without saving** is an explicit confirmation scoped to the current page only. It discards only that page's pending presentation changes; it cannot approve another client.
7. A closed or frozen client needs no acknowledgement. On its next navigation it receives the new immutable Shell and resolves the authoritative layout normally.

Thus service-worker activation never causes data loss or a multi-client deadlock; page reload is gated by a successful layout flush or explicit local discard.

## 15. Public API

Devices:

```text
POST   /api/v1/client-devices/registration
GET    /api/v1/client-devices/current
GET    /api/v1/client-devices
PUT    /api/v1/client-devices/{clientDeviceId}
DELETE /api/v1/client-devices/{clientDeviceId}?revision={revision}
```

Preferences:

```text
GET /api/v1/client-devices/current/workspace-preferences
PUT /api/v1/client-devices/current/workspace-preferences/{workspaceClass}
GET /api/v1/application-execution-preferences/{applicationDefinitionId}/current?workspaceClass={workspaceClass}
PUT /api/v1/application-execution-preferences/{applicationDefinitionId}/current?workspaceClass={workspaceClass}
```

Layouts:

```text
GET /api/v1/workspace-layouts/{workspaceClass}/current
PUT /api/v1/workspace-layouts/{workspaceClass}/current
```

DTOs and status behavior:

```text
RegisterClientDeviceRequest       DisplayName?, DetectedWorkspaceClass
UpdateClientDeviceRequest         DisplayName, WorkspaceClassOverride?, ExpectedRevision
WorkspacePreferenceRequest        LayoutScope, RestoreMode, ExpectedRevision?
ExecutionPreferenceRequest        BackgroundMode, ExpectedRevision?
WorkspaceLayoutWriteRequest       Layout, ExpectedRevision
```

`POST registration` requires authentication/antiforgery. With a valid cookie owned by the current user it returns `200` and the existing device after coalescing Last Seen/detected-class updates. With a missing, unknown, deleted or differently owned cookie it generates a new key/cookie and returns `201`; it never reveals the other owner. `GET current` returns `404 client_device.not_registered` when registration is required. Removing the current device clears its cookie; deleting another owned device does not.

Device responses include the device record, Detected Workspace Class and resolved Workspace Class. Device DELETE has no body and requires the positive decimal `revision` query value shown in the route; missing or malformed revision returns `400 request.invalid`. A stale update/delete returns `409 request.concurrency_conflict` with `currentRevision`; successful delete returns `204`. Workspace/Execution Preference PUT creates with null expected revision (`201`) or updates with exact revision (`200`). Invalid enum/combination returns `400` with a stable field error.

`current` resolves shared/device/fresh scope server-side from authenticated user, owner-scoped cookie and path Workspace Class. Layout GET returns `{ workspaceClass, layoutScope, restoreMode, persistenceEnabled, layout, revision }`. In fresh mode it returns a transient empty layout with null ID, revision zero and `persistenceEnabled=false`; Layout PUT returns `409 desktop.layout_persistence_disabled`. Normal PUT returns `200`; create-on-first-save is atomic and returns `201`. A client cannot select a Client Device ID or another user's resource through request data.

All writes require antiforgery. Registration, preferences, layouts and execution preferences require only an authenticated owner; Client Device identity grants no permission. Device removal and Workspace Class override changes are audited without cookie/hash values. High-frequency Last Seen/layout autosaves and ordinary visual/background preferences are revisioned but not audit events. MOB-003 publishes `client_device.changed`; MOB-004 publishes `workspace_layout.changed`. Events contain resource identity/revision only and clients refetch authoritative state.

Operation Center additionally uses the owner-scoped paged `GET /api/v1/operations` contract in `DATA_AND_API_CONTRACTS.md`; it never enumerates another user's work.

## 16. Stable errors

```text
client_device.not_found
client_device.not_registered
client_device.not_owned
client_device.workspace_preference_invalid
desktop.workspace_class_invalid
desktop.phone_foreground_limit_exceeded
desktop.layout_persistence_disabled
application.background_mode_unsupported
package.surface_contract_unsupported
package.surface_timeout
package.surface_terminated
pwa.update_requires_reload
pwa.layout_flush_conflict
pwa.server_unreachable
```

## 17. Work items and dependency order

1. `MOB-001` — commit PWA, workspace, lifecycle and navigation decisions.
2. `MOB-002` — add manifest, icons, service worker and disconnected/update UX.
3. `MOB-003` — implement client-device registration and Settings UI.
4. `MOB-004` — replace viewport-only layout identity and migrate existing layouts.
5. `MOB-005` — implement Phone Single/Split, Tablet windows and stable display slots.
6. `MOB-006` — version the package surface lifecycle and background preference.
7. `MOB-007` — migrate Browser and Remote surfaces without terminating sessions.
8. `MOB-008` — add Operation Center and `operation.changed` refresh.
9. `MOB-009` — implement Shell navigation and all back inputs.
10. `MOB-010` — complete cross-device release acceptance.

`MOB-004` depends on the supported SQLite migration foundation from `DB-001`. Suspension does not become the Phone default until `MOB-006` and `MOB-007` are complete.

## 18. Required tests

Domain and persistence:

- shared and device layout identities cannot collide;
- Phone rejects a third foreground slot;
- `fresh` cannot write a layout;
- real PostgreSQL and SQLite fixtures migrate from viewport layouts;
- concurrent devices cannot overwrite one another;
- deleting a device preserves shared layouts.

API and security:

- device cookie is not authentication;
- cross-user device access is denied;
- every mutation requires antiforgery and revision;
- service worker does not cache forbidden response classes.

Desktop and package contract:

- Phone Single/Split, focused-pane replacement, ratio and orientation;
- Tablet keeps multiple windows visible;
- workspace switch flush/load is atomic;
- suspended surfaces produce no timer, polling, rendering, input or display activity;
- Browser/Remote suspend preserves runtime-session policy;
- `keep-surface-active` requires the user's stored choice;
- unsupported surface-contract major fails explicitly;
- overlay, app, split, task-switcher and Root Back order;
- `popstate` and mouse Back use the same dispatcher.

End to end:

- Android Chrome installed PWA;
- iPhone and iPad Home-Screen plus ordinary Safari tab;
- iPad touch, keyboard and trackpad;
- two Phones sharing a layout, then one device override;
- `fresh` starts empty and `resume` restores;
- durable Operation survives app switch and PWA restart;
- system Back leaves JulOS only from Shell Root;
- desktop multi-monitor restores logical display slots.

## 19. Acceptance

- Phone never renders more than two foreground apps.
- Tablet supports at least two visible apps and desktop-style keyboard/pointer use.
- Shared layouts and device overrides behave exactly as resolved above.
- Orientation and soft-keyboard changes do not overwrite another layout.
- Suspended apps consume no active frontend work; `keep-surface-active` remains best effort.
- Browser/Remote sessions survive surface suspension according to their own policy.
- long-running work remains visible after PWA restart through Operations.
- Back input is consumed inside JulOS before outer navigation and never traps the user at Root.
- offline state is honest and performs no queued fake mutation.
