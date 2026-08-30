# JulOS Adaptive Browser

Adaptive Browser is an additional JulOS browser package. It does not replace `JulOS.Browser` or the Core Local Web surface.

## Execution modes

- `Automatic` selects a direct device surface only when JulOS can do so without changing the page origin; arbitrary external sites use the server runtime for reliable rendering.
- `This device` embeds the target directly in the user's browser so JavaScript, WebGL and WebGPU execute on that device. Normal browser framing restrictions still apply.
- `JulOS server` starts an isolated headless Chromium runtime through `interactive.session/1.0.0`.

The execution preference is non-security-sensitive and is stored in the client under the authenticated JulOS user identity. Network and runtime allowlists remain server-side and are never derived from that preference.

## Server presentation

Server mode deliberately does not use VNC, noVNC or Guacamole.

The package runtime starts Chromium with its DevTools endpoint bound to loopback. A package-owned bridge exposes only the bounded `julos-browser-stream.v1` WebSocket protocol. It converts browser controls into a small allowlist of Chrome DevTools Protocol commands and forwards `Page.startScreencast` JPEG frames to the JulOS canvas.

A separate package-owned Remote provider proxies that authenticated WebSocket between the private Chromium runtime and the existing same-origin JulOS Remote display gateway. The browser never receives the runtime credential or a raw CDP endpoint.

Current stream controls are navigation, back/forward, reload/stop, viewport resizing, pointer/wheel input and keyboard input. Browser profiles, downloads, audio, WebRTC frame transport and multi-tab target management can extend this protocol without changing the legacy Browser package.

## Rendering and WebGL

Device execution uses the user's own browser and GPU capabilities. Server execution enables Chromium WebGL with SwiftShader as the portable baseline. Hardware GPU passthrough remains a Runtime Manager policy feature and is not granted implicitly by this package.

## Runtime configuration

Official installation provides:

- `runtimeImage`: immutable Adaptive Browser Chromium runtime image;
- `allowedNetworks`: comma-separated Runtime Manager networks;
- `defaultNetwork`: one value from `allowedNetworks`;
- `idleTimeoutMinutes`: session idle timeout.

The presentation protocol is `browser-stream` on runtime port `8080`. The provider runtime is configured by the JulOS deployment and is digest-pinned independently from the browser runtime image.
