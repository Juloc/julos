import assert from 'node:assert/strict';
import { test } from 'node:test';

import { JulOsApiError } from './api-client.js';
import {
  DesktopLayoutPersistence,
  type DesktopLayoutDocument,
  type DesktopViewport,
  type LayoutConflict,
  type PersistedDesktopWindow,
} from './layout-persistence.js';

const windowSnapshot: PersistedDesktopWindow = {
  windowId: '0198f5c1-a0f0-7000-8000-000000000101',
  applicationDefinitionId: '0198f5c1-a0f0-7000-8000-000000000102',
  launchTargetId: null,
  state: 'normal',
  x: 20,
  y: 30,
  width: 640,
  height: 420,
  restoreX: 20,
  restoreY: 30,
  restoreWidth: 640,
  restoreHeight: 420,
  zIndex: 0,
  sessionReferenceId: null,
};

class FakeLayoutServer {
  readonly documents = new Map<DesktopViewport, DesktopLayoutDocument>();
  readonly saves: Array<{ readonly viewport: DesktopViewport; readonly body: Record<string, unknown> }> = [];
  conflictRevision: number | null = null;

  public constructor() {
    this.documents.set('desktop', document('desktop', 3));
    this.documents.set('tablet', document('tablet', 1));
    this.documents.set('mobile', document('mobile', 2));
  }

  public readonly fetch: typeof fetch = async (input, init) => {
    const path = input instanceof Request
      ? new URL(input.url).pathname
      : input instanceof URL
        ? input.pathname
        : input;
    if (path === '/api/v1/auth/antiforgery') {
      return json({ headerName: 'X-JulOS-Antiforgery', token: 'test-token' });
    }

    const match = /^\/api\/v1\/desktop\/layouts\/(desktop|tablet|mobile)$/u.exec(path);
    if (match === null) {
      return new Response(null, { status: 404 });
    }
    const viewport = match[1] as DesktopViewport;
    if ((init?.method ?? 'GET') === 'GET') {
      return json(this.documents.get(viewport));
    }

    const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
    this.saves.push({ viewport, body });
    if (this.conflictRevision !== null) {
      return json(
        {
          type: 'https://os.juloc.de/problems/request-concurrency-conflict',
          title: 'The request conflicts with the current state.',
          status: 409,
          code: 'request.concurrency_conflict',
          correlationId: 'layout-conflict-test',
          retryable: false,
          currentRevision: this.conflictRevision,
        },
        409,
        'application/problem+json',
      );
    }

    const saved: DesktopLayoutDocument = {
      layoutId: this.documents.get(viewport)?.layoutId ?? `layout-${viewport}`,
      viewport,
      name: 'Default',
      revision: Number(body['revision']) + 1,
      updatedAtUtc: '2026-08-02T22:00:00Z',
      windows: body['windows'] as readonly PersistedDesktopWindow[],
      widgets: [],
    };
    this.documents.set(viewport, saved);
    return json(saved);
  };
}

test('load returns the authoritative document for one viewport', async () => {
  const server = new FakeLayoutServer();
  const persistence = new DesktopLayoutPersistence(server.fetch);

  const restored = await persistence.load('desktop');

  assert.equal(restored.viewport, 'desktop');
  assert.equal(restored.revision, 3);
  assert.deepEqual(restored.windows, [windowSnapshot]);
  persistence.dispose();
});

test('rapid layout changes collapse into the latest revisioned save', async () => {
  const server = new FakeLayoutServer();
  const persistence = new DesktopLayoutPersistence(server.fetch, { debounceMilliseconds: 60_000 });
  await persistence.load('desktop');

  persistence.schedule('desktop', [{ ...windowSnapshot, x: 40 }], []);
  persistence.schedule('desktop', [{ ...windowSnapshot, x: 75 }], []);
  await persistence.flush('desktop');

  assert.equal(server.saves.length, 1);
  assert.equal(server.saves[0]?.body['revision'], 3);
  const windows = server.saves[0]?.body['windows'] as readonly PersistedDesktopWindow[];
  assert.equal(windows[0]?.x, 75);
  assert.equal(persistence.snapshot('desktop').revision, 4);
  persistence.dispose();
});

test('conflicting browser instances surface current revision and correlation', async () => {
  const server = new FakeLayoutServer();
  server.conflictRevision = 8;
  const conflicts: LayoutConflict[] = [];
  const persistence = new DesktopLayoutPersistence(server.fetch, {
    debounceMilliseconds: 60_000,
    onConflict: (conflict) => conflicts.push(conflict),
  });
  await persistence.load('desktop');
  persistence.schedule('desktop', [windowSnapshot], []);

  await assert.rejects(
    persistence.flush('desktop'),
    (error: unknown) => error instanceof JulOsApiError && error.status === 409,
  );

  assert.deepEqual(conflicts, [{
    viewport: 'desktop',
    localRevision: 3,
    currentRevision: 8,
    correlationId: 'layout-conflict-test',
  }]);
  persistence.dispose();
});

test('viewport classes retain independent revisions and documents', async () => {
  const server = new FakeLayoutServer();
  const persistence = new DesktopLayoutPersistence(server.fetch, { debounceMilliseconds: 60_000 });
  await persistence.load('mobile');
  await persistence.load('desktop');

  persistence.schedule('mobile', [{ ...windowSnapshot, width: 390 }], []);
  persistence.schedule('desktop', [{ ...windowSnapshot, width: 1200 }], []);
  await persistence.flush();

  assert.deepEqual(server.saves.map((save) => save.viewport).sort(), ['desktop', 'mobile']);
  assert.equal(persistence.snapshot('mobile').revision, 3);
  assert.equal(persistence.snapshot('desktop').revision, 4);
  persistence.dispose();
});

function document(viewport: DesktopViewport, revision: number): DesktopLayoutDocument {
  return {
    layoutId: `0198f5c1-a0f0-7000-8000-00000000010${revision}`,
    viewport,
    name: 'Default',
    revision,
    updatedAtUtc: '2026-08-02T21:00:00Z',
    windows: [windowSnapshot],
    widgets: [],
  };
}

function json(value: unknown, status = 200, contentType = 'application/json'): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': contentType },
  });
}
