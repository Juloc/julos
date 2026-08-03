import assert from 'node:assert/strict';
import test from 'node:test';

import { PackageCapabilityClient } from './package-capability-client.js';

test('capability calls bind the package identity and reuse antiforgery state', async () => {
  const calls: Array<{ readonly path: string; readonly init: RequestInit | undefined }> = [];
  const fetchImplementation: typeof fetch = async (input, init) => {
    const path = String(input);
    calls.push({ path, init });
    if (path === '/api/v1/auth/antiforgery') {
      return new Response(JSON.stringify({
        headerName: 'X-JulOS-Antiforgery',
        token: 'test-token',
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }

    return new Response(JSON.stringify({
      state: 'live',
      metrics: [],
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    });
  };
  const client = new PackageCapabilityClient(fetchImplementation);

  await client.invoke(
    'de.juloc.julos.hostmetrics',
    'host.metrics.read',
    'latest',
    {});
  await client.invoke(
    'de.juloc.julos.hostmetrics',
    'host.metrics.read',
    'latest',
    { maximumAgeSeconds: 120 });

  assert.equal(calls.length, 3);
  assert.equal(calls[0]?.path, '/api/v1/auth/antiforgery');
  assert.equal(
    calls[1]?.path,
    '/api/v1/packages/de.juloc.julos.hostmetrics/capabilities/host.metrics.read/latest');
  const headers = new Headers(calls[1]?.init?.headers);
  assert.equal(headers.get('X-JulOS-Antiforgery'), 'test-token');
  assert.deepEqual(
    JSON.parse(String(calls[1]?.init?.body)),
    { payload: {} });
  assert.deepEqual(
    JSON.parse(String(calls[2]?.init?.body)),
    { payload: { maximumAgeSeconds: 120 } });
});

test('capability calls reject malformed routing identities locally', async () => {
  const client = new PackageCapabilityClient(async () => {
    throw new Error('fetch must not run');
  });

  await assert.rejects(
    client.invoke('../other-package', 'host.metrics.read', 'latest', {}),
    TypeError);
});
