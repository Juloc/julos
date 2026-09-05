# JulOS Browser

JulOS exposes one user-facing Browser. Its default execution mode is the JulOS transparent proxy,
rendered by the user's own browser for low latency, local GPU/video decoding and normal device input.

The historical `de.juloc.julos.browser` package contains the isolated Chromium runtime and profile
work that can be reused by a later explicit **Remote mode**. It is no longer intended to appear as a
second Browser application in the JulOS launcher.

## Product contract

- One Browser icon and one browser workspace per JulOS user.
- Proxy mode is the default and routes HTTP, HTTPS and WebSocket traffic through JulOS.
- JulOS removes or narrows framing restrictions it owns at the proxy boundary and rewrites
  proxy-owned cookie/redirect metadata where required.
- Tabs, active tab, navigation state and browser workspace metadata are server-owned so another
  device can resume the same workspace.
- Remote Chromium is an optional compatibility mode inside the same Browser, not a separate app.

## Remote runtime assets

The existing worker/runtime code remains useful for the future Remote mode:

- `worker/` contains isolated Chromium profile/session policy;
- `runtime/` contains the unprivileged Chromium image and Runtime Manager definition;
- `frontend/` is legacy UI and is not the primary Browser surface.

Remote profiles remain user-scoped. Chromium profile bytes stay in package-owned runtime volumes,
while JulOS Browser workspace metadata belongs to server-side Browser persistence.

See `docs/WEB-APP-RENDERING.md`, `docs/BROWSER-RUNTIME.md` and decision D042.
