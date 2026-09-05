import type { ShellApiClient } from './shell-api.js';

/** Stable Core identity for the unified proxy-first JulOS Browser window. */
export const WebAppBrowserApplicationId = 'core.webapp-browser';

const Base32Alphabet = 'abcdefghijklmnopqrstuvwxyz234567';
const MaximumLabelLength = 63;
const BrowserReadyMessage = 'Browser traffic is routed through JulOS and rendered locally on this device.';

export interface ParsedAddress {
  readonly origin: string;
  readonly pathQuery: string;
}

/**
 * Parses an address-bar value into an origin plus path. A bare host gains an `https://` scheme.
 * @throws if the value is empty, unparseable, or not an http(s) URL.
 */
export function parseAddressInput(raw: string): ParsedAddress {
  const trimmed = raw.trim();
  if (trimmed.length === 0) {
    throw new Error('Enter a web address.');
  }

  const candidate = /^[a-z][a-z0-9+.-]*:\/\//iu.test(trimmed) ? trimmed : `https://${trimmed}`;
  let url: URL;
  try {
    url = new URL(candidate);
  } catch {
    throw new Error(`"${trimmed}" is not a valid web address.`);
  }

  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new Error('Only http and https addresses can be opened.');
  }

  return { origin: `${url.protocol}//${url.host}`, pathQuery: `${url.pathname}${url.search}` };
}

/**
 * Encodes a target origin into the proxy host `wa<base32>.<zone>`. Must match the server-side
 * WebAppOriginCodec exactly (a shared golden vector guards against drift).
 * @throws if the resulting DNS label would exceed 63 characters.
 */
export function encodeProxyHost(origin: string, zone: string): string {
  const url = new URL(origin);
  const schemeByte = url.protocol === 'https:' ? 1 : 0;
  const port = url.port !== '' ? url.port : schemeByte === 1 ? '443' : '80';
  const authority = `${url.hostname.toLowerCase()}:${port}`;
  const payload = new Uint8Array([schemeByte, ...new TextEncoder().encode(authority)]);
  const label = `wa${base32Encode(payload)}`;
  if (label.length > MaximumLabelLength) {
    throw new Error('This address is too long for the JulOS browser proxy.');
  }

  return `${label}.${zone}`;
}

/** Builds the same-origin proxy URL the iframe loads for a parsed address. */
export function buildIframeSrc(encodedHost: string, pathQuery: string): string {
  return `${location.protocol}//${encodedHost}${pathQuery.length === 0 ? '/' : pathQuery}`;
}

export interface NavigationState {
  readonly entries: readonly string[];
  readonly index: number;
}

export type NavigationAction =
  | { readonly type: 'open'; readonly url: string }
  | { readonly type: 'back' }
  | { readonly type: 'forward' }
  | { readonly type: 'reload' }
  | { readonly type: 'sync'; readonly url: string };

/** Pure parent-side history reducer over the user-submitted absolute URLs. */
export function navigate(state: NavigationState, action: NavigationAction): NavigationState {
  switch (action.type) {
    case 'open': {
      const entries = [...state.entries.slice(0, state.index + 1), action.url];
      return { entries, index: entries.length - 1 };
    }
    case 'back':
      return { entries: state.entries, index: Math.max(0, state.index - 1) };
    case 'forward':
      return { entries: state.entries, index: Math.min(state.entries.length - 1, state.index + 1) };
    case 'reload':
      return state;
    case 'sync': {
      if (state.index < 0) {
        return state;
      }
      const entries = [...state.entries];
      entries[state.index] = action.url;
      return { entries, index: state.index };
    }
  }
}

export interface CoreSurfaceHandle {
  readonly element: HTMLElement;
  readonly dispose: () => void;
}

/**
 * Builds the unified JulOS Browser in proxy mode: JulOS fetches and proxies the target while the
 * user's own browser renders it locally. A full remote Chromium mode may be added later as an
 * explicit compatibility mode; it is not a separate user-facing browser application.
 */
export function createWebAppBrowserSurface(api: ShellApiClient): CoreSurfaceHandle {
  const root = document.createElement('section');
  root.className = 'core-app webapp-browser';

  const heading = document.createElement('h2');
  heading.textContent = 'Browser';
  const explanation = document.createElement('p');
  explanation.className = 'webapp-description';
  explanation.textContent = BrowserReadyMessage;

  const toolbar = document.createElement('form');
  toolbar.className = 'webapp-toolbar';
  const back = navButton('‹', 'Back');
  const forward = navButton('›', 'Forward');
  const reload = navButton('⟳', 'Reload');
  const address = document.createElement('input');
  address.type = 'text';
  address.className = 'webapp-address';
  address.placeholder = 'https://unifi.local …';
  address.spellcheck = false;
  address.autocapitalize = 'off';
  const open = document.createElement('button');
  open.type = 'submit';
  open.className = 'webapp-open';
  open.textContent = 'Open';
  const external = document.createElement('button');
  external.type = 'button';
  external.className = 'webapp-external';
  external.textContent = '↗';
  external.title = 'Open in a new tab';
  external.setAttribute('aria-label', 'Open in a new tab');
  external.disabled = true;
  toolbar.append(back, forward, reload, address, open, external);

  const status = document.createElement('div');
  status.className = 'webapp-status';
  status.setAttribute('role', 'status');

  const frame = document.createElement('iframe');
  frame.className = 'window-webapp';
  frame.title = 'JulOS Browser';
  frame.referrerPolicy = 'no-referrer';

  root.append(heading, explanation, toolbar, status, frame);

  let zone: string | null = null;
  let nav: NavigationState = { entries: [], index: -1 };
  let loadingTarget = false;

  const applyButtons = (): void => {
    back.disabled = nav.index <= 0;
    forward.disabled = nav.index < 0 || nav.index >= nav.entries.length - 1;
    reload.disabled = nav.index < 0;
    external.disabled = zone === null || nav.index < 0;
  };

  const loadCurrent = (): void => {
    if (zone === null || nav.index < 0) {
      return;
    }
    const url = nav.entries[nav.index]!;
    address.value = url;
    try {
      const parsed = parseAddressInput(url);
      loadingTarget = true;
      status.textContent = 'Opening through JulOS…';
      frame.src = buildIframeSrc(encodeProxyHost(parsed.origin, zone), parsed.pathQuery);
    } catch (error) {
      loadingTarget = false;
      status.textContent = error instanceof Error ? error.message : String(error);
    }
    applyButtons();
  };

  const dispatch = (action: NavigationAction): void => {
    nav = navigate(nav, action);
    loadCurrent();
  };

  const syncFrameLocation = (event: MessageEvent): void => {
    if (event.source !== frame.contentWindow) {
      return;
    }

    const payload = event.data as { type?: unknown; url?: unknown } | null;
    if (payload?.type !== 'julos-browser-location' || typeof payload.url !== 'string') {
      return;
    }

    try {
      const parsed = parseAddressInput(payload.url);
      const normalized = `${parsed.origin}${parsed.pathQuery}`;
      nav = navigate(nav, { type: 'sync', url: normalized });
      address.value = normalized;
      applyButtons();
    } catch {
      // Ignore malformed messages from proxied content.
    }
  };
  window.addEventListener('message', syncFrameLocation);

  frame.addEventListener('load', () => {
    if (!loadingTarget || frame.src === 'about:blank') {
      return;
    }
    loadingTarget = false;
    status.textContent = 'Loaded through the JulOS proxy.';
  });
  frame.addEventListener('error', () => {
    if (!loadingTarget) {
      return;
    }
    loadingTarget = false;
    status.textContent = 'This page could not be rendered in proxy mode.';
  });

  toolbar.addEventListener('submit', (event) => {
    event.preventDefault();
    if (zone === null) {
      return;
    }
    try {
      const parsed = parseAddressInput(address.value);
      dispatch({ type: 'open', url: `${parsed.origin}${parsed.pathQuery}` });
    } catch (error) {
      status.textContent = error instanceof Error ? error.message : String(error);
    }
  });
  back.addEventListener('click', () => dispatch({ type: 'back' }));
  forward.addEventListener('click', () => dispatch({ type: 'forward' }));
  reload.addEventListener('click', () => loadCurrent());
  external.addEventListener('click', () => {
    if (zone === null || nav.index < 0) {
      return;
    }
    try {
      const parsed = parseAddressInput(nav.entries[nav.index]!);
      const proxied = buildIframeSrc(encodeProxyHost(parsed.origin, zone), parsed.pathQuery);
      window.open(proxied, '_blank', 'noopener,noreferrer');
    } catch (error) {
      status.textContent = error instanceof Error ? error.message : String(error);
    }
  });

  toolbar.hidden = true;
  status.textContent = 'Loading Browser proxy configuration…';
  void api.readWebProxyConfig().then(
    (config) => {
      if (!config.enabled || config.proxyZone.length === 0) {
        status.textContent = 'The JulOS Browser proxy is not enabled on this deployment.';
        return;
      }
      zone = config.proxyZone;
      toolbar.hidden = false;
      status.textContent = 'Enter a web address.';
      applyButtons();
      address.focus();
    },
    () => {
      status.textContent = 'Could not load the JulOS Browser proxy configuration.';
    },
  );

  return {
    element: root,
    dispose: () => {
      loadingTarget = false;
      window.removeEventListener('message', syncFrameLocation);
      frame.src = 'about:blank';
    },
  };
}

function navButton(glyph: string, label: string): HTMLButtonElement {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'webapp-nav';
  button.textContent = glyph;
  button.title = label;
  button.setAttribute('aria-label', label);
  button.disabled = true;
  return button;
}

function base32Encode(data: Uint8Array): string {
  let output = '';
  let buffer = 0;
  let bits = 0;
  for (const value of data) {
    buffer = (buffer << 8) | value;
    bits += 8;
    while (bits >= 5) {
      bits -= 5;
      output += Base32Alphabet[(buffer >> bits) & 0x1f];
    }
  }
  if (bits > 0) {
    output += Base32Alphabet[(buffer << (5 - bits)) & 0x1f];
  }

  return output;
}
