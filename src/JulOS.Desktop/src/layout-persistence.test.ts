import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  DebouncedLayoutPersistence,
  LayoutRevisionConflictError,
  type DesktopLayoutApi,
  type SaveDesktopLayoutRequest,
  type StoredDesktopLayout,
} from './layout-persistence.js';
import type { DesktopWindowSnapshot } from './window-store.js';

const windowSnapshot: DesktopWindowSnapshot = {
  id: 'window-1',
  applicationId: 'app.files',
  launchTargetId: null,
  title: 'Files',
  state: 'normal',
  bounds: { x: 20, y: 30, width: 640, height: 420 },
  restoreBounds: { x: 20, y: 30, width: 640, height: 420 },
  zIndex: 0,
};

class FakeLayoutApi implements DesktopLayoutApi {
  public stored: StoredDesktopLayout | null = null;
  public readonly saves: SaveDesktopLayoutRequest[] = [];
  public conflictRevision: number | null = null;

  public async read(): Promise<StoredDesktopLayout | null> {
    return this.stored;
  }

  public async save(request: SaveDesktopLayoutRequest): Promise<StoredDesktopLayout> {
    this.saves.push(request);
    if (this.conflictRevision !== null) {
      throw new LayoutRevisionConflictError(this.conflictRevision);
    }

    this.stored = {
      viewportClass: request.viewportClass,
      windows: request.windows,
      revision: request.expectedRevision + 1,
    };
    return this.stored;
  }
}

test('restore returns the authoritative layout for the viewport class', async () => {
  const api = new FakeLayoutApi();
  api.stored = { viewportClass: 'desktop', windows: [windowSnapshot], revision: 3 };
  const persistence = new DebouncedLayoutPersistence(api);

  const restored = await persistence.restore('desktop');

  assert.deepEqual(restored, api.stored);
});

test('rapid changes collapse into the latest save', async () => {
  const api = new FakeLayoutApi();
  const persistence = new DebouncedLayoutPersistence(api, 60_000);

  persistence.schedule('desktop', [windowSnapshot], 1);
  persistence.schedule('desktop', [{ ...windowSnapshot, title: 'Latest' }], 1);
  const saved = await persistence.flush();

  assert.equal(api.saves.length, 1);
  assert.equal(api.saves[0]?.windows[0]?.title, 'Latest');
  assert.equal(saved?.revision, 2);
  persistence.dispose();
});

test('conflicting browser instances surface current revision without overwrite', async () => {
  const api = new FakeLayoutApi();
  api.conflictRevision = 8;
  const conflicts: number[] = [];
  const persistence = new DebouncedLayoutPersistence(api, 60_000, (conflict) => {
    conflicts.push(conflict.currentRevision);
  });

  persistence.schedule('desktop', [windowSnapshot], 7);
  const result = await persistence.flush();

  assert.equal(result, null);
  assert.deepEqual(conflicts, [8]);
  assert.equal(api.saves.length, 1);
  persistence.dispose();
});

test('viewport classes are saved independently', async () => {
  const api = new FakeLayoutApi();
  const persistence = new DebouncedLayoutPersistence(api, 60_000);

  persistence.schedule('mobile', [windowSnapshot], 0);
  await persistence.flush();
  persistence.schedule('desktop', [windowSnapshot], 0);
  await persistence.flush();

  assert.deepEqual(api.saves.map((save) => save.viewportClass), ['mobile', 'desktop']);
  persistence.dispose();
});
