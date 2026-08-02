import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ShellApiClient } from './shell-api.js';

test('shell API reads authentication, profile and running server version', async () => {
  const paths: string[] = [];
  const fakeFetch: typeof fetch = async (input) => {
    const path = String(input);
    paths.push(path);

    const body = path.endsWith('/api/v1/auth/status')
      ? { setupRequired: false, authenticated: true, user: { userId: 'user-1', userName: 'admin', displayName: 'Administrator' } }
      : path.endsWith('/api/v1/profile')
        ? {
            userId: 'user-1',
            userName: 'admin',
            displayName: 'Administrator',
            preferredLanguage: 'de',
            timeZone: 'Europe/Berlin',
            theme: 'dark',
            motion: 'reduced',
            revision: 4,
          }
        : { component: 'JulOS.Server', version: '0.1.0' };

    return new Response(JSON.stringify(body), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    });
  };

  const api = new ShellApiClient(fakeFetch);
  const status = await api.readAuthenticationStatus();
  const profile = await api.readProfile();
  const version = await api.readServerVersion();

  assert.equal(status.authenticated, true);
  assert.equal(profile.preferredLanguage, 'de');
  assert.equal(profile.theme, 'dark');
  assert.equal(version.version, '0.1.0');
  assert.deepEqual(paths, [
    '/api/v1/auth/status',
    '/api/v1/profile',
    '/api/v1/system/version',
  ]);
});

test('shell API rejects failed HTTP responses', async () => {
  const fakeFetch: typeof fetch = async () => new Response(null, { status: 401 });
  const api = new ShellApiClient(fakeFetch);

  await assert.rejects(() => api.readServerVersion(), /status 401/);
});
