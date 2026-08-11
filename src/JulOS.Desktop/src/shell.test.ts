import assert from 'node:assert/strict';
import { test } from 'node:test';

import { JSDOM } from 'jsdom';

import { translate, type SupportedLanguage } from './localization.js';
import type {
  AuthenticationStatus,
  ServerVersion,
  ShellApiClient,
  UserProfile,
} from './shell-api.js';
import type { DesktopRuntime, DesktopRuntimeOptions } from './desktop-runtime.js';

// The JulOsShell custom element needs a DOM. Populate only the globals Node does
// not already provide, so importing the shell module (which extends HTMLElement
// at definition time) and defining the element both succeed.
const dom = new JSDOM('<!DOCTYPE html><html><head></head><body></body></html>', {
  url: 'http://localhost/',
});

function ensureGlobal(name: string, value: unknown): void {
  if ((globalThis as Record<string, unknown>)[name] === undefined) {
    Object.defineProperty(globalThis, name, { value, configurable: true, writable: true });
  }
}

for (const name of ['window', 'document', 'customElements', 'HTMLElement']) {
  ensureGlobal(name, (dom.window as unknown as Record<string, unknown>)[name]);
}

const { JulOsShell, defineJulOsShell } = await import('./shell.js');
defineJulOsShell();

interface FakeProfileState {
  theme: string;
  motion: string;
  language: SupportedLanguage;
  displayName: string;
}

function createApi(state: {
  authenticated?: boolean;
  profile: FakeProfileState;
}): ShellApiClient {
  const authenticated = state.authenticated ?? true;
  const user = {
    userId: '00000000-0000-4000-8000-000000000001',
    userName: 'admin',
    displayName: state.profile.displayName,
  };
  const api = {
    readAuthenticationStatus: (): Promise<AuthenticationStatus> => Promise.resolve({
      setupRequired: false,
      authenticated,
      user: authenticated ? user : null,
    }),
    readProfile: (): Promise<UserProfile> => Promise.resolve({
      userId: user.userId,
      userName: user.userName,
      displayName: state.profile.displayName,
      preferredLanguage: state.profile.language,
      timeZone: 'UTC',
      theme: state.profile.theme as UserProfile['theme'],
      motion: state.profile.motion as UserProfile['motion'],
      revision: 1,
    }),
    readServerVersion: (): Promise<ServerVersion> =>
      Promise.resolve({ component: 'JulOS.Server', version: '0.4.0-test' }),
  };
  return api as unknown as ShellApiClient;
}

function stubRuntime(): DesktopRuntime {
  return { start: (): Promise<void> => Promise.resolve(), stop: (): void => undefined } as unknown as DesktopRuntime;
}

async function settle(predicate: () => boolean, timeoutMs = 2000): Promise<void> {
  const started = Date.now();
  while (!predicate()) {
    if (Date.now() - started > timeoutMs) {
      throw new Error('The shell did not reach the expected state in time.');
    }
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
}

function element(shell: HTMLElement, id: string): HTMLElement {
  const found = shell.shadowRoot?.getElementById(id) ?? null;
  assert.ok(found !== null, `The shell is missing #${id}.`);
  return found;
}

test('the authenticated user display name survives the language sweep', async () => {
  const api = createApi({ profile: { theme: 'system', motion: 'enabled', language: 'en', displayName: 'Alice Admin' } });
  const shell = new JulOsShell(api, () => stubRuntime());
  document.body.appendChild(shell);
  try {
    await settle(() => element(shell, 'current-user').textContent === 'Alice Admin');
    const user = element(shell, 'current-user');
    assert.equal(user.textContent, 'Alice Admin');
    assert.equal(user.dataset['message'], undefined);
    assert.notEqual(user.textContent, translate('en', 'loading'));
  } finally {
    shell.remove();
  }
});

test('the profile theme and motion are applied on load', async () => {
  document.documentElement.removeAttribute('data-theme');
  document.documentElement.removeAttribute('data-motion');
  const api = createApi({ profile: { theme: 'dark', motion: 'reduced', language: 'en', displayName: 'Alice Admin' } });
  const shell = new JulOsShell(api, () => stubRuntime());
  document.body.appendChild(shell);
  try {
    await settle(() => document.documentElement.dataset['theme'] === 'dark');
    assert.equal(document.documentElement.dataset['theme'], 'dark');
    assert.equal(document.documentElement.dataset['motion'], 'reduced');
  } finally {
    shell.remove();
  }
});

test('onProfileChanged reapplies the theme after a settings save', async () => {
  document.documentElement.removeAttribute('data-theme');
  const profile: FakeProfileState = { theme: 'dark', motion: 'reduced', language: 'en', displayName: 'Alice Admin' };
  const api = createApi({ profile });
  let captured: (() => void | Promise<void>) | undefined;
  const shell = new JulOsShell(api, (options: DesktopRuntimeOptions) => {
    captured = options.onProfileChanged;
    return stubRuntime();
  });
  document.body.appendChild(shell);
  try {
    await settle(() => captured !== undefined && document.documentElement.dataset['theme'] === 'dark');
    profile.theme = 'light';
    await captured?.();
    assert.equal(document.documentElement.dataset['theme'], 'light');
  } finally {
    shell.remove();
  }
});

test('static labels are translated into the profile language', async () => {
  const api = createApi({ profile: { theme: 'system', motion: 'enabled', language: 'de', displayName: 'Alice Admin' } });
  const shell = new JulOsShell(api, () => stubRuntime());
  document.body.appendChild(shell);
  try {
    await settle(() => element(shell, 'current-user').textContent === 'Alice Admin');
    const launcherLabel = shell.shadowRoot?.querySelector('[data-message="launcher"]') ?? null;
    assert.ok(launcherLabel !== null, 'The shell is missing its launcher label.');
    assert.equal(launcherLabel.textContent, translate('de', 'launcher'));
  } finally {
    shell.remove();
  }
});
