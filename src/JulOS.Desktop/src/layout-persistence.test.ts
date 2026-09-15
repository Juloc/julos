import assert from 'node:assert/strict';
import { test } from 'node:test';

import { JulOsApiError } from './api-client.js';
import {
  DesktopLayoutPersistence,
  type LayoutConflict,
  type PersistedDesktopWindow,
  type WorkspaceLayoutResponse,
} from './layout-persistence.js';
import type { WorkspaceClass } from './workspace-contract.js';

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
  displaySlot: 0,
};

class FakeLayoutServer {
  readonly documents = new Map<WorkspaceClass, WorkspaceLayoutResponse>();
  readonly saves: Array<{
    readonly workspaceClass: WorkspaceClass;
    readonly body: Record<string, unknown>;
  }> = [];

  conflictRevision: number | null = null;

  public constructor() {
    this.documents.set('desktop-single', resolved('desktop-single', 3));
    this.documents.set('tablet', resolved('tablet', 1));
    this.documents.set('phone', resolved('phone', 2));
  }

  /** Makes one workspace class resolve to the fresh mode that stores nothing. */
  public makeFresh(workspaceClass: WorkspaceClass): void {
    const current = this.documents.get(workspaceClass);
    assert.ok(current);
    this.documents.set(workspaceClass, {
      ...current,
      restoreMode: 'fresh',
      persistenceEnabled: false,
      revision: 0,
      layout: { ...current.layout, layoutId: null, windows: [] },
    });
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

    const match = /^\/api\/v1\/workspace-layouts\/([a-z-]+)\/current$/u.exec(path);
    if (match === null) {
      return new Response(null, { status: 404 });
    }

    const workspaceClass = match[1] as WorkspaceClass;
    if ((init?.method ?? 'GET') === 'GET') {
      return json(this.documents.get(workspaceClass));
    }

    const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
    this.saves.push({ workspaceClass, body });
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

    const previous = this.documents.get(workspaceClass);
    const layout = body['layout'] as Record<string, unknown>;
    const saved: WorkspaceLayoutResponse = {
      workspaceClass,
      layoutScope: previous?.layoutScope ?? 'shared',
      restoreMode: 'resume',
      persistenceEnabled: true,
      revision: Number(body['expectedRevision']) + 1,
      layout: {
        layoutId: previous?.layout.layoutId ?? `layout-${workspaceClass}`,
        name: 'Default',
        presentationMode: previous?.layout.presentationMode ?? 'freeform',
        primaryWindowId: null,
        secondaryWindowId: null,
        splitRatioPermille: null,
        displayCount: 1,
        updatedAtUtc: '2026-08-02T22:00:00Z',
        windows: layout['windows'] as readonly PersistedDesktopWindow[],
        widgets: [],
      },
    };
    this.documents.set(workspaceClass, saved);
    return json(saved);
  };
}

test('load returns the authoritative layout and how it resolved', async () => {
  const server = new FakeLayoutServer();
  const persistence = new DesktopLayoutPersistence(server.fetch);

  const restored = await persistence.load('desktop-single');

  assert.equal(restored.workspaceClass, 'desktop-single');
  assert.equal(restored.layoutScope, 'shared');
  assert.equal(restored.restoreMode, 'resume');
  assert.equal(restored.revision, 3);
  assert.deepEqual(restored.layout.windows, [windowSnapshot]);
  persistence.dispose();
});

test('rapid layout changes collapse into the latest revisioned save', async () => {
  const server = new FakeLayoutServer();
  const persistence = new DesktopLayoutPersistence(server.fetch, { debounceMilliseconds: 60_000 });
  await persistence.load('desktop-single');

  persistence.schedule('desktop-single', [{ ...windowSnapshot, x: 40 }], []);
  persistence.schedule('desktop-single', [{ ...windowSnapshot, x: 75 }], []);
  await persistence.flush('desktop-single');

  assert.equal(server.saves.length, 1);
  assert.equal(server.saves[0]?.body['expectedRevision'], 3);
  const layout = server.saves[0]?.body['layout'] as Record<string, unknown>;
  const windows = layout['windows'] as readonly PersistedDesktopWindow[];
  assert.equal(windows[0]?.x, 75);
  assert.equal(persistence.snapshot('desktop-single').revision, 4);
  persistence.dispose();
});

test('conflicting browser instances surface current revision and correlation', async () => {
  const server = new FakeLayoutServer();
  server.conflictRevision = 8;
  const conflicts: LayoutConflict[] = [];
  const persistence = new DesktopLayoutPersistence(server.fetch, {
    debounceMilliseconds: 60_000,
    onConflict: (conflict) => {
      conflicts.push(conflict);
    },
  });
  await persistence.load('desktop-single');
  persistence.schedule('desktop-single', [windowSnapshot], []);

  await assert.rejects(
    persistence.flush('desktop-single'),
    (error: unknown) => error instanceof JulOsApiError && error.status === 409,
  );

  assert.deepEqual(conflicts, [{
    workspaceClass: 'desktop-single',
    localRevision: 3,
    currentRevision: 8,
    correlationId: 'layout-conflict-test',
  }]);
  persistence.dispose();
});

test('workspace classes retain independent revisions and documents', async () => {
  const server = new FakeLayoutServer();
  const persistence = new DesktopLayoutPersistence(server.fetch, { debounceMilliseconds: 60_000 });
  await persistence.load('phone');
  await persistence.load('desktop-single');

  persistence.schedule('phone', [{ ...windowSnapshot, width: 390 }], []);
  persistence.schedule('desktop-single', [{ ...windowSnapshot, width: 1200 }], []);
  await persistence.flush();

  assert.deepEqual(
    server.saves.map((save) => save.workspaceClass).sort(),
    ['desktop-single', 'phone'],
  );
  assert.equal(persistence.snapshot('phone').revision, 3);
  assert.equal(persistence.snapshot('desktop-single').revision, 4);
  persistence.dispose();
});

test('a fresh workspace never sends a write', async () => {
  const server = new FakeLayoutServer();
  server.makeFresh('phone');
  const persistence = new DesktopLayoutPersistence(server.fetch, { debounceMilliseconds: 0 });

  const restored = await persistence.load('phone');
  assert.equal(restored.persistenceEnabled, false);
  assert.equal(restored.layout.layoutId, null);
  assert.equal(persistence.persistenceEnabled('phone'), false);

  persistence.schedule('phone', [windowSnapshot], []);
  await persistence.flush('phone');

  assert.deepEqual(
    server.saves,
    [],
    'Fresh mode stores nothing, so an autosave must not reach the server at all.',
  );
  persistence.dispose();
});

test('the presentation the layout was loaded with is carried into a save', async () => {
  const server = new FakeLayoutServer();
  const persistence = new DesktopLayoutPersistence(server.fetch, { debounceMilliseconds: 0 });
  await persistence.load('phone');

  persistence.schedule('phone', [windowSnapshot], []);
  await persistence.flush('phone');

  const layout = server.saves[0]?.body['layout'] as Record<string, unknown>;
  assert.equal(
    layout['presentationMode'],
    'phone-empty',
    'An autosave stores where the windows are; it does not decide how the workspace is arranged.',
  );
  persistence.dispose();
});

function resolved(workspaceClass: WorkspaceClass, revision: number): WorkspaceLayoutResponse {
  return {
    workspaceClass,
    layoutScope: 'shared',
    restoreMode: 'resume',
    persistenceEnabled: true,
    revision,
    layout: {
      layoutId: `0198f5c1-a0f0-7000-8000-00000000010${revision}`,
      name: 'Default',
      presentationMode: workspaceClass === 'phone' ? 'phone-empty' : 'freeform',
      primaryWindowId: null,
      secondaryWindowId: null,
      splitRatioPermille: null,
      displayCount: 1,
      updatedAtUtc: '2026-08-02T21:00:00Z',
      windows: [windowSnapshot],
      widgets: [],
    },
  };
}

function json(value: unknown, status = 200, contentType = 'application/json'): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': contentType },
  });
}
