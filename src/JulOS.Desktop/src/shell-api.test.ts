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

test('shell API creates the initial administrator and signs in with same-origin JSON requests', async () => {
  const requests: Array<{ path: string; method: string; body: unknown }> = [];
  const fakeFetch: typeof fetch = async (input, init) => {
    const path = String(input);
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) as unknown : null;
    requests.push({ path, method: init?.method ?? 'GET', body });

    return new Response(JSON.stringify({
      userId: 'user-1',
      userName: 'admin',
      displayName: 'Administrator',
    }), {
      status: path.endsWith('/setup') ? 201 : 200,
      headers: { 'Content-Type': 'application/json' },
    });
  };

  const api = new ShellApiClient(fakeFetch);
  await api.createInitialAdministrator({
    userName: 'admin',
    displayName: 'Administrator',
    password: 'Strong-password-1',
  });
  await api.login({ userName: 'admin', password: 'Strong-password-1' });

  assert.deepEqual(requests, [
    {
      path: '/api/v1/auth/setup',
      method: 'POST',
      body: {
        userName: 'admin',
        displayName: 'Administrator',
        password: 'Strong-password-1',
      },
    },
    {
      path: '/api/v1/auth/login',
      method: 'POST',
      body: {
        userName: 'admin',
        password: 'Strong-password-1',
      },
    },
  ]);
});

test('shell API reads the launchable application catalog for the selected viewport', async () => {
  let requestedPath = '';
  const fakeFetch: typeof fetch = async (input) => {
    requestedPath = String(input);
    return new Response(JSON.stringify([{
      applicationDefinitionId: '11111111-1111-1111-1111-111111111111',
      packageId: 'de.juloc.julos.reference',
      packageVersion: '1.0.0',
      stableKey: 'reference',
      displayNameKey: 'app.reference.name',
      instancePolicy: 'single-instance-per-user',
      defaultWidth: 720,
      defaultHeight: 520,
      minimumWidth: 360,
      minimumHeight: 280,
      viewports: ['desktop'],
      elementName: 'julos-reference-app',
      frontend: {
        moduleUrl: '/api/v1/packages/de.juloc.julos.reference/frontend/1.0.0',
        sha256: '0'.repeat(64),
        exportedElements: ['julos-reference-app'],
      },
    }]), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    });
  };

  const api = new ShellApiClient(fakeFetch);
  const applications = await api.readApplications('desktop');

  assert.equal(requestedPath, '/api/v1/applications?viewport=desktop');
  assert.equal(applications.length, 1);
  assert.equal(applications[0]?.elementName, 'julos-reference-app');
});

test('shell API rejects failed HTTP responses', async () => {
  const fakeFetch: typeof fetch = async () => new Response(null, { status: 401 });
  const api = new ShellApiClient(fakeFetch);

  await assert.rejects(() => api.readServerVersion(), /status 401/);
});
