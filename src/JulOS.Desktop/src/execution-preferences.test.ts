import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ExecutionPreferenceClient, type ExecutionPreference } from './execution-preferences.js';

test('an application that was never configured reads as suspend', async () => {
  const server = fakeServer();
  const client = new ExecutionPreferenceClient(server.fetch);

  const resolved = await client.read('app-1', 'phone');

  assert.equal(resolved.backgroundMode, 'suspend');
  assert.equal(resolved.revision, 0);
  assert.equal(
    server.calls[0]?.path,
    '/api/v1/application-execution-preferences/app-1/current?workspaceClass=phone',
  );
});

test('a read is cached so opening a window does not re-ask', async () => {
  const server = fakeServer();
  const client = new ExecutionPreferenceClient(server.fetch);

  assert.equal(client.cached('app-1', 'phone'), null);
  await client.read('app-1', 'phone');

  assert.equal(client.cached('app-1', 'phone')?.backgroundMode, 'suspend');
  assert.equal(server.calls.filter((call) => call.method === 'GET').length, 1);
});

test('a first write sends no expected revision', async () => {
  const server = fakeServer();
  const client = new ExecutionPreferenceClient(server.fetch);
  await client.read('app-1', 'phone');

  await client.write('app-1', 'phone', 'keep-surface-active', 0);

  const write = server.calls.find((call) => call.method === 'PUT');
  assert.deepEqual(write?.body, { backgroundMode: 'keep-surface-active', expectedRevision: null });
});

test('a later write sends the revision it is based on', async () => {
  const server = fakeServer();
  const client = new ExecutionPreferenceClient(server.fetch);

  await client.write('app-1', 'phone', 'keep-surface-active', 3);

  const write = server.calls.find((call) => call.method === 'PUT');
  assert.deepEqual(write?.body, { backgroundMode: 'keep-surface-active', expectedRevision: 3 });
});

test('the cache holds what the server answered, not what was sent', async () => {
  const server = fakeServer();
  server.answerWith = { backgroundMode: 'suspend', revision: 9 };
  const client = new ExecutionPreferenceClient(server.fetch);

  const stored = await client.write('app-1', 'phone', 'keep-surface-active', null);

  assert.equal(stored.backgroundMode, 'suspend', 'A write never assumes it landed as sent.');
  assert.equal(client.cached('app-1', 'phone')?.revision, 9);
});

test('clearing forgets every cached preference', async () => {
  const server = fakeServer();
  const client = new ExecutionPreferenceClient(server.fetch);
  await client.read('app-1', 'phone');

  client.clear();

  assert.equal(client.cached('app-1', 'phone'), null);
});

interface RecordedCall {
  readonly method: string;
  readonly path: string;
  readonly body: unknown;
}

function fakeServer(): {
  readonly fetch: typeof fetch;
  readonly calls: RecordedCall[];
  answerWith: { backgroundMode: 'suspend' | 'keep-surface-active'; revision: number } | null;
} {
  const calls: RecordedCall[] = [];
  const server = {
    calls,
    answerWith: null as { backgroundMode: 'suspend' | 'keep-surface-active'; revision: number } | null,
    fetch: (async (path: string, init: RequestInit = {}) => {
      const method = init.method ?? 'GET';
      const body = typeof init.body === 'string' ? JSON.parse(init.body) : null;
      calls.push({ method, path, body });

      if (path === '/api/v1/auth/antiforgery') {
        return json({ headerName: 'X-JulOS-Antiforgery', token: 'token-value' });
      }

      const preference: ExecutionPreference = {
        applicationDefinitionId: 'app-1',
        workspaceClass: 'phone',
        layoutScope: 'shared',
        backgroundMode: server.answerWith?.backgroundMode
          ?? (method === 'PUT' ? body.backgroundMode : 'suspend'),
        supportsKeepSurfaceActive: true,
        revision: server.answerWith?.revision ?? (method === 'PUT' ? 1 : 0),
      };
      return json(preference);
    }) as unknown as typeof fetch,
  };
  return server;
}

function json(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}
