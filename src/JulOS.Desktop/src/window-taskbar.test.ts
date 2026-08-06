import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  AltTabWindowSwitcher,
  TaskbarWindowModel,
  WindowLaunchCoordinator,
  type ApplicationInstancePolicy,
} from './window-taskbar.js';
import { WindowStore, type WindowBounds } from './window-store.js';

const usableArea: WindowBounds = { x: 0, y: 0, width: 1280, height: 744 };
const bounds: WindowBounds = { x: 80, y: 60, width: 640, height: 420 };

function createStore(): WindowStore {
  let identifier = 1;
  return new WindowStore(() => `window-${identifier++}`);
}

function launch(
  coordinator: WindowLaunchCoordinator,
  applicationId: string,
  policy: ApplicationInstancePolicy,
  launchTargetId: string | null = null,
) {
  return coordinator.launch({
    applicationId,
    launchTargetId,
    instancePolicy: policy,
    title: `${applicationId}:${launchTargetId ?? 'default'}`,
    bounds,
  }, usableArea);
}

test('single-instance-per-user focuses the existing window', () => {
  const store = createStore();
  const coordinator = new WindowLaunchCoordinator(store);

  const first = launch(coordinator, 'app.settings', 'single-instance-per-user');
  const second = launch(coordinator, 'app.settings', 'single-instance-per-user', 'other-target');

  assert.equal(first.outcome, 'opened');
  assert.equal(second.outcome, 'focused-existing');
  assert.equal(second.window.id, first.window.id);
  assert.equal(store.windows.length, 1);
  assert.equal(store.frontWindow?.id, first.window.id);
});

test('single-instance-per-target separates targets and reuses the same target', () => {
  const store = createStore();
  const coordinator = new WindowLaunchCoordinator(store);

  const serverA = launch(coordinator, 'app.remote', 'single-instance-per-target', 'server-a');
  const serverB = launch(coordinator, 'app.remote', 'single-instance-per-target', 'server-b');
  const serverAAgain = launch(coordinator, 'app.remote', 'single-instance-per-target', 'server-a');

  assert.equal(serverA.outcome, 'opened');
  assert.equal(serverB.outcome, 'opened');
  assert.equal(serverAAgain.outcome, 'focused-existing');
  assert.equal(serverAAgain.window.id, serverA.window.id);
  assert.equal(store.windows.length, 2);
  assert.equal(store.frontWindow?.id, serverA.window.id);
});

test('multiple-instance applications always open a new window', () => {
  const store = createStore();
  const coordinator = new WindowLaunchCoordinator(store);

  const first = launch(coordinator, 'app.browser', 'multiple-instances', 'example');
  const second = launch(coordinator, 'app.browser', 'multiple-instances', 'example');

  assert.equal(first.outcome, 'opened');
  assert.equal(second.outcome, 'opened');
  assert.notEqual(first.window.id, second.window.id);
  assert.equal(store.windows.length, 2);
});

test('taskbar groups applications with counts and front-to-back window order', () => {
  const store = createStore();
  const coordinator = new WindowLaunchCoordinator(store);
  const taskbar = new TaskbarWindowModel(store);

  const browserOne = launch(coordinator, 'app.browser', 'multiple-instances');
  const files = launch(coordinator, 'app.files', 'single-instance-per-user');
  const browserTwo = launch(coordinator, 'app.browser', 'multiple-instances');
  store.minimize(browserOne.window.id);

  assert.deepEqual(taskbar.groups, [
    {
      applicationId: 'app.browser',
      title: browserTwo.window.title,
      count: 2,
      minimizedCount: 1,
      windowIds: [browserTwo.window.id, browserOne.window.id],
      activeWindowId: browserTwo.window.id,
    },
    {
      applicationId: 'app.files',
      title: files.window.title,
      count: 1,
      minimizedCount: 0,
      windowIds: [files.window.id],
      activeWindowId: null,
    },
  ]);
});

test('taskbar activation restores a minimized window to its previous fixed state', () => {
  const store = createStore();
  const coordinator = new WindowLaunchCoordinator(store);
  const taskbar = new TaskbarWindowModel(store);
  const launched = launch(coordinator, 'app.files', 'single-instance-per-user');

  store.maximize(launched.window.id, usableArea);
  store.minimize(launched.window.id);
  const activated = taskbar.activateWindow(launched.window.id, usableArea);

  assert.equal(activated.state, 'maximized');
  assert.deepEqual(activated.bounds, usableArea);
  assert.equal(store.frontWindow?.id, launched.window.id);
});

test('Alt+Tab freezes MRU order and changes focus only when committed', () => {
  const store = createStore();
  const coordinator = new WindowLaunchCoordinator(store);
  const one = launch(coordinator, 'app.one', 'multiple-instances');
  const two = launch(coordinator, 'app.two', 'multiple-instances');
  const three = launch(coordinator, 'app.three', 'multiple-instances');
  const switcher = new AltTabWindowSwitcher(store);

  const firstSelection = switcher.begin();
  assert.deepEqual(firstSelection, {
    windowIds: [three.window.id, two.window.id, one.window.id],
    selectedWindowId: two.window.id,
    selectedIndex: 1,
  });
  assert.equal(store.frontWindow?.id, three.window.id);

  const secondSelection = switcher.next();
  assert.equal(secondSelection?.selectedWindowId, one.window.id);
  assert.equal(store.frontWindow?.id, three.window.id);

  const committed = switcher.commit(usableArea);
  assert.equal(committed?.id, one.window.id);
  assert.equal(store.frontWindow?.id, one.window.id);
  assert.equal(switcher.current, null);
});

test('Alt+Tab restores a minimized selected window and excludes later windows', () => {
  const store = createStore();
  const coordinator = new WindowLaunchCoordinator(store);
  const one = launch(coordinator, 'app.one', 'multiple-instances');
  const two = launch(coordinator, 'app.two', 'multiple-instances');
  const three = launch(coordinator, 'app.three', 'multiple-instances');
  store.minimize(two.window.id);
  const switcher = new AltTabWindowSwitcher(store);

  const started = switcher.begin();
  const later = launch(coordinator, 'app.later', 'multiple-instances');
  assert.deepEqual(started?.windowIds, [three.window.id, two.window.id, one.window.id]);
  assert.equal(switcher.current?.windowIds.includes(later.window.id), false);

  const committed = switcher.commit(usableArea);
  assert.equal(committed?.id, two.window.id);
  assert.equal(committed?.state, 'normal');
  assert.equal(store.frontWindow?.id, two.window.id);
});

test('Alt+Tab cancel preserves the existing focus', () => {
  const store = createStore();
  const coordinator = new WindowLaunchCoordinator(store);
  launch(coordinator, 'app.one', 'multiple-instances');
  const two = launch(coordinator, 'app.two', 'multiple-instances');
  const switcher = new AltTabWindowSwitcher(store);

  switcher.begin();
  switcher.previous();
  switcher.cancel();

  assert.equal(store.frontWindow?.id, two.window.id);
  assert.equal(switcher.current, null);
});
