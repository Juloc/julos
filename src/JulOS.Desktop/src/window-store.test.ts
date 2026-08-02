import assert from 'node:assert/strict';
import { test } from 'node:test';

import { WindowStore, WindowStoreError, type WindowBounds } from './window-store.js';

const initialBounds: WindowBounds = { x: 40, y: 50, width: 640, height: 420 };
const usableArea: WindowBounds = { x: 0, y: 0, width: 1440, height: 844 };

function createStore(): WindowStore {
  let nextIdentifier = 1;
  return new WindowStore(() => `window-${nextIdentifier++}`);
}

function openWindow(store: WindowStore, title = 'Application'): string {
  return store.open({
    applicationId: `app.${title.toLowerCase().replaceAll(' ', '-')}`,
    title,
    bounds: initialBounds,
  }).id;
}

test('five simultaneous windows keep deterministic gap-free stacking', () => {
  const store = createStore();
  const identifiers = [
    openWindow(store, 'Files'),
    openWindow(store, 'Docker'),
    openWindow(store, 'Browser'),
    openWindow(store, 'Remote'),
    openWindow(store, 'Caddy'),
  ];

  assert.deepEqual(store.windows.map((window) => window.id), identifiers);
  assert.deepEqual(store.windows.map((window) => window.zIndex), [0, 1, 2, 3, 4]);
  assert.equal(store.frontWindow?.id, 'window-5');

  store.focus('window-2');
  assert.deepEqual(store.windows.map((window) => window.id), [
    'window-1',
    'window-3',
    'window-4',
    'window-5',
    'window-2',
  ]);
  assert.deepEqual(store.windows.map((window) => window.zIndex), [0, 1, 2, 3, 4]);
  assert.equal(store.frontWindow?.id, 'window-2');
});

test('move and resize update normal and restore bounds together', () => {
  const store = createStore();
  const windowId = openWindow(store);

  store.move(windowId, -20, 72);
  const resized = store.resize(windowId, 900, 560);

  assert.deepEqual(resized.bounds, { x: -20, y: 72, width: 900, height: 560 });
  assert.deepEqual(resized.restoreBounds, resized.bounds);
  assert.equal(resized.state, 'normal');
});

test('maximize remembers normal geometry and restore returns to it', () => {
  const store = createStore();
  const windowId = openWindow(store);

  const maximized = store.maximize(windowId, usableArea);
  assert.equal(maximized.state, 'maximized');
  assert.deepEqual(maximized.bounds, usableArea);
  assert.deepEqual(maximized.restoreBounds, initialBounds);

  const restored = store.restore(windowId);
  assert.equal(restored.state, 'normal');
  assert.deepEqual(restored.bounds, initialBounds);
});

test('minimize and restore preserve the previous presentation state', () => {
  const store = createStore();
  const normalId = openWindow(store, 'Normal');
  const maximizedId = openWindow(store, 'Maximized');

  store.minimize(normalId);
  const restoredNormal = store.restore(normalId);
  assert.equal(restoredNormal.state, 'normal');
  assert.deepEqual(restoredNormal.bounds, initialBounds);

  store.maximize(maximizedId, usableArea);
  store.minimize(maximizedId);
  const restoredMaximized = store.restore(maximizedId, usableArea);
  assert.equal(restoredMaximized.state, 'maximized');
  assert.deepEqual(restoredMaximized.bounds, usableArea);
  assert.equal(store.frontWindow?.id, maximizedId);
});

test('close removes the target and renormalizes the remaining z-order', () => {
  const store = createStore();
  openWindow(store, 'One');
  openWindow(store, 'Two');
  openWindow(store, 'Three');

  store.close('window-2');

  assert.deepEqual(store.windows.map((window) => window.id), ['window-1', 'window-3']);
  assert.deepEqual(store.windows.map((window) => window.zIndex), [0, 1]);
  assert.equal(store.frontWindow?.id, 'window-3');
});

test('invalid transitions and identities fail with stable error codes', () => {
  const store = createStore();
  const windowId = openWindow(store);

  assert.throws(
    () => store.open({
      id: windowId,
      applicationId: 'app.duplicate',
      title: 'Duplicate',
      bounds: initialBounds,
    }),
    (error: unknown) => error instanceof WindowStoreError && error.code === 'window.already_open',
  );

  store.maximize(windowId, usableArea);
  assert.throws(
    () => store.move(windowId, 10, 10),
    (error: unknown) => error instanceof WindowStoreError && error.code === 'window.bounds_not_owned',
  );

  store.restore(windowId);
  store.minimize(windowId);
  assert.throws(
    () => store.maximize(windowId, usableArea),
    (error: unknown) => error instanceof WindowStoreError && error.code === 'window.not_visible',
  );

  assert.throws(
    () => store.close('missing-window'),
    (error: unknown) => error instanceof WindowStoreError && error.code === 'window.not_open',
  );
  assert.throws(
    () => store.open({
      applicationId: 'app.invalid',
      title: 'Invalid bounds',
      bounds: { x: 0, y: 0, width: 0, height: 10 },
    }),
    (error: unknown) => error instanceof WindowStoreError && error.code === 'window.bounds_invalid',
  );
});

test('subscriptions receive snapshots and cannot mutate stored geometry', () => {
  const store = createStore();
  const snapshots: Array<ReadonlyArray<{ readonly x: number }>> = [];
  const unsubscribe = store.subscribe((windows) => {
    snapshots.push(windows.map((window) => ({ x: window.bounds.x })));
  });

  const windowId = openWindow(store);
  const external = store.windows[0];
  assert.ok(external);
  (external.bounds as { x: number }).x = 9999;

  assert.equal(store.windows[0]?.bounds.x, initialBounds.x);
  store.move(windowId, 75, 80);
  unsubscribe();
  store.close(windowId);

  assert.deepEqual(snapshots, [[], [{ x: 40 }], [{ x: 75 }]]);
});

test('clear removes all windows with one resulting empty snapshot', () => {
  const store = createStore();
  openWindow(store, 'One');
  openWindow(store, 'Two');
  store.clear();

  assert.deepEqual(store.windows, []);
  assert.equal(store.frontWindow, null);
});
