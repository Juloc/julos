import assert from 'node:assert/strict';
import { test } from 'node:test';

import { DesktopWindowCoordinator, buildTaskbarGroups } from './taskbar.js';
import { WindowStore, type WindowBounds } from './window-store.js';

const usableArea: WindowBounds = { x: 0, y: 0, width: 1280, height: 664 };
const bounds: WindowBounds = { x: 40, y: 40, width: 640, height: 420 };

function createStore(): WindowStore {
  let next = 1;
  return new WindowStore(() => `window-${next++}`);
}

test('taskbar groups windows by application and exposes count and state', () => {
  const store = createStore();
  const first = store.open({ applicationId: 'app.files', title: 'Files A', bounds });
  const second = store.open({ applicationId: 'app.files', title: 'Files B', bounds });
  store.open({ applicationId: 'app.settings', title: 'Settings', bounds });
  store.minimize(first.id);
  store.minimize(second.id);

  const groups = buildTaskbarGroups(store.windows, store.frontWindow?.id ?? null);
  const files = groups.find((group) => group.applicationId === 'app.files');
  const settings = groups.find((group) => group.applicationId === 'app.settings');

  assert.equal(files?.count, 2);
  assert.equal(files?.minimizedOnly, true);
  assert.equal(files?.focused, false);
  assert.equal(settings?.count, 1);
  assert.equal(settings?.focused, true);
});

test('single-user application focuses its existing window', () => {
  const store = createStore();
  const coordinator = new DesktopWindowCoordinator(store);
  const request = { applicationId: 'app.settings', title: 'Settings', bounds } as const;

  const opened = coordinator.openOrFocus(request, 'single-user', usableArea);
  store.minimize(opened.id);
  const activated = coordinator.openOrFocus(request, 'single-user', usableArea);

  assert.equal(store.windows.length, 1);
  assert.equal(activated.id, opened.id);
  assert.equal(activated.state, 'normal');
});

test('single-target application permits different targets but not duplicates', () => {
  const store = createStore();
  const coordinator = new DesktopWindowCoordinator(store);

  coordinator.openOrFocus(
    { applicationId: 'app.remote', launchTargetId: 'vm-1', title: 'VM 1', bounds },
    'single-target',
    usableArea,
  );
  coordinator.openOrFocus(
    { applicationId: 'app.remote', launchTargetId: 'vm-2', title: 'VM 2', bounds },
    'single-target',
    usableArea,
  );
  coordinator.openOrFocus(
    { applicationId: 'app.remote', launchTargetId: 'vm-1', title: 'VM 1 duplicate', bounds },
    'single-target',
    usableArea,
  );

  assert.equal(store.windows.length, 2);
  assert.equal(store.frontWindow?.launchTargetId, 'vm-1');
});

test('multiple policy always opens another window', () => {
  const store = createStore();
  const coordinator = new DesktopWindowCoordinator(store);
  const request = { applicationId: 'app.browser', title: 'Browser', bounds } as const;

  coordinator.openOrFocus(request, 'multiple', usableArea);
  coordinator.openOrFocus(request, 'multiple', usableArea);

  assert.equal(store.windows.length, 2);
});

test('window switcher restores minimized targets and keeps focus predictable', () => {
  const store = createStore();
  const coordinator = new DesktopWindowCoordinator(store);
  const first = store.open({ applicationId: 'app.one', title: 'One', bounds });
  store.open({ applicationId: 'app.two', title: 'Two', bounds });
  store.minimize(first.id);

  const switched = coordinator.switchByOffset(1, usableArea);

  assert.equal(switched?.id, first.id);
  assert.equal(switched?.state, 'normal');
  assert.equal(store.frontWindow?.id, first.id);
});
