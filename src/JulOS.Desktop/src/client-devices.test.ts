import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  ClientDeviceStore,
  defaultDevicePreference,
  detectWorkspaceClass,
  preferenceFor,
  preferenceWorkspaceClasses,
  readPresentationCapabilities,
  type CapabilitySource,
  type ClientDeviceView,
} from './client-devices.js';

test('classification reads only the four permitted capability inputs', () => {
  const capabilities = readPresentationCapabilities(capabilitySource({
    coarse: true,
    fine: false,
    screen: [390, 844],
    layoutWidth: 390,
  }));

  assert.deepEqual(Object.keys(capabilities).sort(), [
    'anyPointerFine',
    'layoutViewportWidthCssPx',
    'primaryPointerCoarse',
    'screenMinimumDimensionCssPx',
  ]);
  // The minimum is stable across orientation, so landscape does not promote a phone.
  assert.equal(capabilities.screenMinimumDimensionCssPx, 390);
});

test('a touch phone a tablet and a mouse desktop classify differently', () => {
  assert.equal(
    detectWorkspaceClass(capabilitySource({ coarse: true, fine: false, screen: [390, 844], layoutWidth: 390 })),
    'phone',
  );
  assert.equal(
    detectWorkspaceClass(capabilitySource({ coarse: true, fine: false, screen: [820, 1180], layoutWidth: 820 })),
    'tablet',
  );
  assert.equal(
    detectWorkspaceClass(capabilitySource({ coarse: false, fine: true, screen: [1920, 1080], layoutWidth: 1600 })),
    'desktop-single',
  );
});

test('an unset preference resolves to the documented shared and resume default', () => {
  const device = clientDevice({ preferences: [] });

  const preference = preferenceFor(device, 'phone');

  assert.equal(preference.layoutScope, defaultDevicePreference.layoutScope);
  assert.equal(preference.restoreMode, defaultDevicePreference.restoreMode);
  assert.equal(preference.workspaceClass, 'phone');
});

test('only workspace classes a device can present are offered', () => {
  const phone = clientDevice({ lastDetectedWorkspaceClass: 'phone' });
  const desktop = clientDevice({ lastDetectedWorkspaceClass: 'desktop-single' });
  const pinned = clientDevice({ lastDetectedWorkspaceClass: 'desktop-single', workspaceClassOverride: 'tablet' });

  assert.deepEqual(preferenceWorkspaceClasses(phone), ['phone']);
  assert.deepEqual(preferenceWorkspaceClasses(desktop), ['desktop-single', 'desktop-multi']);
  assert.deepEqual(preferenceWorkspaceClasses(pinned), ['tablet'], 'A pin is authoritative over detection.');
});

test('a preference stored for a class the pin hides stays reachable', () => {
  const device = clientDevice({
    lastDetectedWorkspaceClass: 'desktop-single',
    workspaceClassOverride: 'tablet',
    preferences: [{ workspaceClass: 'phone', layoutScope: 'device', restoreMode: 'fresh' }],
  });

  assert.deepEqual(preferenceWorkspaceClasses(device), ['phone', 'tablet']);
});

test('registration sends the detected class and never receives a key', async () => {
  const server = fakeServer();
  const store = new ClientDeviceStore(server.fetch);

  await store.register('Phone', 'phone');

  const registration = server.calls.find((call) => call.path === '/api/v1/client-devices/registration');
  assert.ok(registration);
  assert.equal(registration.method, 'POST');
  assert.deepEqual(registration.body, { displayName: 'Phone', detectedWorkspaceClass: 'phone' });
  // Headers lower-cases every name, so the recorded key is the normalised one.
  assert.equal(registration.headers['x-julos-antiforgery'], 'token-value');

  const current = store.currentDevice();
  assert.ok(current);
  assert.equal(current.displayName, 'Phone');
  assert.ok(
    !Object.keys(current).some((key) => key.toLowerCase().includes('key')),
    'No response field may carry the client instance key or its hash.',
  );
});

test('a rename sends the revision the rendered record carried', async () => {
  const server = fakeServer();
  const store = new ClientDeviceStore(server.fetch);
  await store.register('Phone', 'phone');
  const device = store.currentDevice();
  assert.ok(device);

  await store.update(device, 'Kitchen tablet', 'tablet');

  const update = server.calls.find((call) => call.method === 'PUT');
  assert.ok(update);
  assert.deepEqual(update.body, {
    displayName: 'Kitchen tablet',
    workspaceClassOverride: 'tablet',
    expectedRevision: device.revision,
  });
});

test('a rejected write still reloads the authoritative list', async () => {
  const server = fakeServer();
  server.rejectNextWrite({ status: 409, code: 'request.concurrency_conflict', currentRevision: 7 });
  const store = new ClientDeviceStore(server.fetch);
  await store.register('Phone', 'phone');
  const device = store.currentDevice();
  assert.ok(device);
  const before = server.calls.filter((call) => call.method === 'GET' && call.path === '/api/v1/client-devices').length;

  await assert.rejects(() => store.update(device, 'Renamed', null));

  const after = server.calls.filter((call) => call.method === 'GET' && call.path === '/api/v1/client-devices').length;
  assert.equal(after, before + 1, 'A conflict must leave the surface showing server state, not the rejected edit.');
  assert.equal(store.snapshot().devices[0]?.displayName, 'Phone');
  assert.notEqual(store.snapshot().lastError, null);
});

test('the delete request carries the revision in the documented query value', async () => {
  const server = fakeServer();
  const store = new ClientDeviceStore(server.fetch);
  await store.register('Phone', 'phone');
  const device = store.currentDevice();
  assert.ok(device);

  await store.remove(device);

  const remove = server.calls.find((call) => call.method === 'DELETE');
  assert.ok(remove);
  assert.equal(remove.path, `/api/v1/client-devices/${device.clientDeviceId}?revision=${device.revision}`);
});

test('removing the device in use registers this browser again', async () => {
  const server = fakeServer();
  const store = new ClientDeviceStore(server.fetch);
  await store.register('Phone', 'phone');
  const device = store.currentDevice();
  assert.ok(device);

  await store.remove(device);

  const registrations = server.calls.filter((call) => call.path === '/api/v1/client-devices/registration');
  assert.equal(registrations.length, 2, 'The cleared cookie must be replaced by a visibly new device.');
  const replacement = store.currentDevice();
  assert.ok(replacement);
  assert.notEqual(replacement.clientDeviceId, device.clientDeviceId);
});

test('removing another device does not re-register this one', async () => {
  const server = fakeServer();
  const store = new ClientDeviceStore(server.fetch);
  await store.register('Phone', 'phone');
  const other = server.addForeignDevice('Old laptop');

  await store.remove(other);

  const registrations = server.calls.filter((call) => call.path === '/api/v1/client-devices/registration');
  assert.equal(registrations.length, 1);
  assert.deepEqual(store.snapshot().devices.map((device) => device.displayName), ['Phone']);
});

interface RecordedCall {
  readonly method: string;
  readonly path: string;
  readonly body: unknown;
  readonly headers: Readonly<Record<string, string>>;
}

interface WriteRejection {
  readonly status: number;
  readonly code: string;
  readonly currentRevision: number;
}

/**
 * A minimal stand-in for the client-device endpoints.
 *
 * It deliberately never returns a key field of any kind, so a test asserting that the
 * client cannot read the client instance key is testing the client and not the fake.
 */
function fakeServer(): {
  readonly fetch: typeof fetch;
  readonly calls: readonly RecordedCall[];
  rejectNextWrite: (rejection: WriteRejection) => void;
  addForeignDevice: (displayName: string) => ClientDeviceView;
} {
  const calls: RecordedCall[] = [];
  let devices: ClientDeviceView[] = [];
  let rejection: WriteRejection | null = null;
  let issued = 0;

  const fetchImplementation = (async (path: string, init: RequestInit = {}) => {
    const method = init.method ?? 'GET';
    const headers = Object.fromEntries(new Headers(init.headers).entries());
    const body = typeof init.body === 'string' ? JSON.parse(init.body) : null;
    calls.push({ method, path, body, headers });

    if (path === '/api/v1/auth/antiforgery') {
      return json({ headerName: 'X-JulOS-Antiforgery', token: 'token-value' });
    }
    if (path === '/api/v1/client-devices' && method === 'GET') {
      return json(devices);
    }
    if (path === '/api/v1/client-devices/registration') {
      issued += 1;
      const registered = clientDevice({
        clientDeviceId: `0198f5c1-a0f0-7000-8000-0000000001${String(issued).padStart(2, '0')}`,
        displayName: String(body?.displayName ?? ''),
        lastDetectedWorkspaceClass: body?.detectedWorkspaceClass ?? 'phone',
        isCurrentDevice: true,
      });
      devices = [...devices.map((device) => ({ ...device, isCurrentDevice: false })), registered];
      return json(registered, 201);
    }

    if (rejection !== null) {
      const failure = rejection;
      rejection = null;
      return json(
        {
          status: failure.status,
          title: 'The record changed.',
          detail: 'The record changed.',
          code: failure.code,
          currentRevision: failure.currentRevision,
        },
        failure.status,
      );
    }

    if (method === 'DELETE') {
      const id = path.slice('/api/v1/client-devices/'.length).split('?')[0];
      devices = devices.filter((device) => device.clientDeviceId !== id);
      return new Response(null, { status: 204 });
    }

    const id = path.slice('/api/v1/client-devices/'.length).split('/')[0] ?? '';
    const existing = devices.find((device) => device.clientDeviceId === id);
    assert.ok(existing, `The fake server has no device '${id}'.`);
    const updated: ClientDeviceView = {
      ...existing,
      displayName: typeof body?.displayName === 'string' ? body.displayName : existing.displayName,
      workspaceClassOverride: body?.workspaceClassOverride ?? existing.workspaceClassOverride,
      revision: existing.revision + 1,
    };
    devices = devices.map((device) => (device.clientDeviceId === id ? updated : device));
    return json(updated);
  }) as unknown as typeof fetch;

  return {
    fetch: fetchImplementation,
    calls,
    rejectNextWrite: (next) => { rejection = next; },
    addForeignDevice: (displayName) => {
      const foreign = clientDevice({
        clientDeviceId: '0198f5c1-a0f0-7000-8000-0000000009ff',
        displayName,
        isCurrentDevice: false,
      });
      devices = [...devices, foreign];
      return foreign;
    },
  };
}

function json(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function clientDevice(overrides: Partial<ClientDeviceView> = {}): ClientDeviceView {
  return {
    clientDeviceId: '0198f5c1-a0f0-7000-8000-000000000101',
    displayName: 'Phone',
    lastDetectedWorkspaceClass: 'phone',
    workspaceClassOverride: null,
    createdAtUtc: '2026-09-15T08:00:00Z',
    lastSeenAtUtc: '2026-09-15T09:00:00Z',
    revision: 1,
    preferences: [],
    isCurrentDevice: false,
    ...overrides,
  };
}

function capabilitySource(input: {
  readonly coarse: boolean;
  readonly fine: boolean;
  readonly screen: readonly [number, number];
  readonly layoutWidth: number;
}): CapabilitySource {
  return {
    matchMedia: (query: string) => ({
      matches: query === '(pointer: coarse)' ? input.coarse : input.fine,
    }),
    screenWidthCssPx: input.screen[0],
    screenHeightCssPx: input.screen[1],
    layoutViewportWidthCssPx: input.layoutWidth,
  };
}
