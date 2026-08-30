import type { ShellApiClient } from './shell-api.js';

/** Package identity for the dynamic "type a URL" local-rendering browser window. */
export const WebAppBrowserApplicationId = 'core.webapp-browser';

const Base32Alphabet = 'abcdefghijklmnopqrstuvwxyz234567';
const MaximumLabelLength = 63;
const LocalWebReadyMessage = 'Local Web is for compatible internal web apps. Use the Browser package for general websites or apps that stay blank.';

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
    throw new Error('This address is too long for Local Web; use the Browser package instead.');
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
  | { readonly type: 'reload' };

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
  }
}

export interface CoreSurfaceHandle {
  readonly element: HTMLElement;
  readonly dispose: () => void;
}

/**
 * Builds Local Web: an address bar plus a sandboxed iframe that renders a proxied internal target
 * in the user's browser. This is deliberately separate from the installable Browser package,
 * which uses an isolated Chromium runtime and the interactive display transport.
 */
export function createWebAppBrowserSurface(api: ShellApiClient): CoreSurfaceHandle {
  const root = document.createElement('section');
  root.className = 'core-app webapp-browser';

  const heading = document.createElement('h2');
  heading.textContent = 'Local Web';
  const explanation = document.createElement('p');
  explanation.className = 'webapp-description';
  explanation.textContent = LocalWebReadyMessage;

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
  toolbar.append(back, forward, reload, address, open);

  const status = document.createElement('div');
  status.className = 'webapp-status';
  status.setAttribute('role', 'status');

  const frame = document.createElement('iframe');
  frame.className = 'window-webapp';
  frame.title = 'Local Web application';
  frame.setAttribute('sandbox', 'allow-forms allow-scripts allow-same-origin allow-popups allow-downloads');
  frame.referrerPolicy = 'no-referrer';

  root.append(heading, explanation, toolbar, status, frame);

  let zone: string | null = null;
  let nav: NavigationState = { entries: [], index: -1 };
  let loadingTarget = false;

  const applyButtons = (): void => {
    back.disabled = nav.index <= 0;
    forward.disabled = nav.index < 0 || nav.index >= nav.entries.length - 1;
    reload.disabled = nav.index < 0;
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
      status.textContent = 'Opening through the Local Web proxy…';
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

  frame.addEventListener('load', () => {
    if (!loadingTarget || frame.src === 'about:blank') {
      return;
    }
    loadingTarget = false;
    status.textContent = 'Loaded through Local Web. If the app is blank or broken, open it with the Browser package.';
  });
  frame.addEventListener('error', () => {
    if (!loadingTarget) {
      return;
    }
    loadingTarget = false;
    status.textContent = 'Local Web could not render this target. Use the Browser package for the streamed Chromium session.';
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

  toolbar.hidden = true;
  status.textContent = 'Loading Local Web configuration…';
  void api.readWebProxyConfig().then(
    (config) => {
      if (!config.enabled || config.proxyZone.length === 0) {
        status.textContent = 'Local Web is not enabled on this deployment. The Browser package is separate and does not require Local Web mode.';
        return;
      }
      zone = config.proxyZone;
      toolbar.hidden = false;
      status.textContent = 'Enter a compatible internal address.';
      applyButtons();
      address.focus();
    },
    () => {
      status.textContent = 'Could not load the Local Web proxy configuration.';
    },
  );

  return {
    element: root,
    dispose: () => {
      loadingTarget = false;
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
