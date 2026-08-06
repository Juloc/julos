import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  WindowSnapController,
  detectSnapTarget,
  targetState,
  type SnapPreview,
  type SnapTarget,
} from './window-snapping.js';
import {
  WindowStore,
  boundsForWindowState,
  type FixedWindowState,
  type WindowBounds,
} from './window-store.js';

const normalBounds: WindowBounds = { x: 120, y: 90, width: 640, height: 420 };
const usableArea: WindowBounds = { x: 0, y: 0, width: 1201, height: 744 };

function createStore(): WindowStore {
  const store = new WindowStore(() => 'window-1');
  store.open({
    applicationId: 'app.browser',
    title: 'Browser',
    bounds: normalBounds,
  });
  return store;
}

test('snap geometry tiles odd usable areas without seams or overlaps', () => {
  const left = boundsForWindowState('snapped-left', usableArea);
  const right = boundsForWindowState('snapped-right', usableArea);
  assert.equal(left.width + right.width, usableArea.width);
  assert.equal(left.x + left.width, right.x);

  const quarters = [
    boundsForWindowState('snapped-top-left', usableArea),
    boundsForWindowState('snapped-top-right', usableArea),
    boundsForWindowState('snapped-bottom-left', usableArea),
    boundsForWindowState('snapped-bottom-right', usableArea),
  ];
  const [topLeft, topRight, bottomLeft, bottomRight] = quarters;
  assert.ok(topLeft && topRight && bottomLeft && bottomRight);
  assert.equal(topLeft.width + topRight.width, usableArea.width);
  assert.equal(topLeft.height + bottomLeft.height, usableArea.height);
  assert.equal(topLeft.x + topLeft.width, topRight.x);
  assert.equal(topLeft.y + topLeft.height, bottomLeft.y);
  assert.equal(bottomLeft.x + bottomLeft.width, bottomRight.x);
  assert.equal(topRight.y + topRight.height, bottomRight.y);
});

test('pointer edge detection covers halves quarters and maximize', () => {
  const samples: ReadonlyArray<readonly [number, number, SnapTarget | null]> = [
    [1, 300, 'left'],
    [1200, 300, 'right'],
    [1, 1, 'top-left'],
    [1200, 1, 'top-right'],
    [1, 743, 'bottom-left'],
    [1200, 743, 'bottom-right'],
    [600, 1, 'maximize'],
    [600, 400, null],
  ];

  for (const [x, y, expected] of samples) {
    assert.equal(detectSnapTarget({ x, y }, usableArea), expected);
  }
});

test('preview is visible before pointer release and clears after commit', () => {
  const store = createStore();
  const controller = new WindowSnapController(store);
  const observed: Array<SnapPreview | null> = [];
  controller.subscribe((preview) => observed.push(preview));

  const preview = controller.updatePreview({ x: 1, y: 300 }, usableArea);
  assert.equal(preview?.target, 'left');
  assert.deepEqual(preview?.bounds, boundsForWindowState('snapped-left', usableArea));
  assert.equal(store.frontWindow?.state, 'normal');

  const committed = controller.commitPointer('window-1', { x: 1, y: 300 }, usableArea);
  assert.equal(committed?.state, 'snapped-left');
  assert.deepEqual(committed?.bounds, preview?.bounds);
  assert.equal(controller.preview, null);
  assert.deepEqual(observed.map((value) => value?.target ?? null), [null, 'left', null]);
});

test('pointer and keyboard commands produce identical state and bounds', () => {
  const targets: readonly SnapTarget[] = [
    'left',
    'right',
    'top-left',
    'top-right',
    'bottom-left',
    'bottom-right',
    'maximize',
  ];

  for (const target of targets) {
    const pointerStore = createStore();
    const keyboardStore = createStore();
    const pointerController = new WindowSnapController(pointerStore);
    const keyboardController = new WindowSnapController(keyboardStore);
    const point = pointerPointFor(target);

    const pointerWindow = pointerController.commitPointer('window-1', point, usableArea);
    const keyboardWindow = keyboardController.applyKeyboard('window-1', target, usableArea);

    assert.equal(pointerWindow?.state, keyboardWindow.state);
    assert.deepEqual(pointerWindow?.bounds, keyboardWindow.bounds);
    assert.equal(keyboardWindow.state, targetState(target));
  }
});

test('usable snap bounds exclude the taskbar area supplied by the shell', () => {
  const fullViewportHeight = 800;
  const taskbarHeight = fullViewportHeight - usableArea.height;
  assert.equal(taskbarHeight, 56);

  for (const state of fixedStates()) {
    const bounds = boundsForWindowState(state, usableArea);
    assert.ok(bounds.y + bounds.height <= fullViewportHeight - taskbarHeight);
  }
});

test('dragging a snapped window restores it under the same pointer ratio', () => {
  const store = createStore();
  const controller = new WindowSnapController(store);
  controller.applyKeyboard('window-1', 'right', usableArea);

  const restored = controller.restoreForDrag(
    'window-1',
    { x: 1000, y: 18 },
    usableArea,
    40,
    96,
  );

  assert.equal(restored.state, 'normal');
  assert.equal(Math.round(restored.bounds.x), 574);
  assert.equal(restored.bounds.y, 0);
  assert.equal(restored.bounds.width, 640);
  assert.equal(restored.bounds.height, 420);
  assert.deepEqual(restored.restoreBounds, restored.bounds);
});

test('keyboard restore returns every snapped state to original normal bounds', () => {
  const store = createStore();
  const controller = new WindowSnapController(store);

  controller.applyKeyboard('window-1', 'bottom-right', usableArea);
  const restored = controller.applyKeyboard('window-1', 'restore', usableArea);

  assert.equal(restored.state, 'normal');
  assert.deepEqual(restored.bounds, normalBounds);
});

function pointerPointFor(target: SnapTarget): { readonly x: number; readonly y: number } {
  switch (target) {
    case 'left':
      return { x: 1, y: 300 };
    case 'right':
      return { x: 1200, y: 300 };
    case 'top-left':
      return { x: 1, y: 1 };
    case 'top-right':
      return { x: 1200, y: 1 };
    case 'bottom-left':
      return { x: 1, y: 743 };
    case 'bottom-right':
      return { x: 1200, y: 743 };
    case 'maximize':
      return { x: 600, y: 1 };
  }
}

function fixedStates(): readonly FixedWindowState[] {
  return [
    'maximized',
    'snapped-left',
    'snapped-right',
    'snapped-top-left',
    'snapped-top-right',
    'snapped-bottom-left',
    'snapped-bottom-right',
  ];
}
