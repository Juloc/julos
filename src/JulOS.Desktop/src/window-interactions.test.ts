import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  WindowInteractionController,
  moveBounds,
  resizeBounds,
  type AnimationFrameScheduler,
  type PointerSample,
  type SettledResize,
} from './window-interactions.js';
import { WindowStore, WindowStoreError, type WindowBounds } from './window-store.js';

const initialBounds: WindowBounds = { x: 100, y: 80, width: 600, height: 400 };
const usableArea: WindowBounds = { x: 0, y: 0, width: 1200, height: 744 };

class ManualAnimationFrameScheduler implements AnimationFrameScheduler {
  readonly #callbacks = new Map<number, (timestamp: number) => void>();
  #nextHandle = 1;

  public requestCount = 0;
  public cancelCount = 0;

  public request(callback: (timestamp: number) => void): number {
    const handle = this.#nextHandle++;
    this.requestCount += 1;
    this.#callbacks.set(handle, callback);
    return handle;
  }

  public cancel(handle: number): void {
    if (this.#callbacks.delete(handle)) {
      this.cancelCount += 1;
    }
  }

  public flush(timestamp = 16): void {
    const callbacks = [...this.#callbacks.values()];
    this.#callbacks.clear();
    for (const callback of callbacks) {
      callback(timestamp);
    }
  }

  public get pendingCount(): number {
    return this.#callbacks.size;
  }
}

function createStore(): WindowStore {
  const store = new WindowStore(() => 'window-1');
  store.open({
    applicationId: 'app.files',
    title: 'Files',
    bounds: initialBounds,
  });
  return store;
}

function pointer(
  clientX: number,
  clientY: number,
  pointerType = 'mouse',
  pointerId = 1,
): PointerSample {
  return { pointerId, pointerType, clientX, clientY };
}

test('many pointer moves produce one store update per animation frame', () => {
  const store = createStore();
  const scheduler = new ManualAnimationFrameScheduler();
  const snapshots: WindowBounds[] = [];
  store.subscribe((windows) => {
    const window = windows[0];
    if (window !== undefined) {
      snapshots.push(window.bounds);
    }
  });
  const controller = new WindowInteractionController(store, { scheduler });

  controller.beginMove('window-1', pointer(120, 100), {
    usableArea,
    titleBarHeight: 40,
    minimumVisibleTitleBarWidth: 96,
    source: 'draggable',
  });
  const countAfterFocus = snapshots.length;

  for (let offset = 1; offset <= 100; offset += 1) {
    assert.equal(controller.updatePointer(pointer(120 + offset, 100 + offset)), true);
  }

  assert.equal(scheduler.requestCount, 1);
  assert.equal(scheduler.pendingCount, 1);
  assert.equal(snapshots.length, countAfterFocus);

  scheduler.flush();
  assert.equal(snapshots.length, countAfterFocus + 1);
  assert.deepEqual(store.frontWindow?.bounds, { x: 200, y: 180, width: 600, height: 400 });
});

test('interactive title-bar controls never begin a drag', () => {
  const store = createStore();
  const scheduler = new ManualAnimationFrameScheduler();
  const controller = new WindowInteractionController(store, { scheduler });

  const started = controller.beginMove('window-1', pointer(120, 100), {
    usableArea,
    titleBarHeight: 40,
    minimumVisibleTitleBarWidth: 96,
    source: 'interactive',
  });

  assert.equal(started, false);
  assert.equal(controller.activeWindowId, null);
  assert.equal(controller.updatePointer(pointer(300, 300)), false);
  assert.equal(scheduler.requestCount, 0);
  assert.deepEqual(store.frontWindow?.bounds, initialBounds);
});

test('mouse and touch use the same pointer interaction path', async () => {
  for (const pointerType of ['mouse', 'touch'] as const) {
    const store = createStore();
    const scheduler = new ManualAnimationFrameScheduler();
    const controller = new WindowInteractionController(store, { scheduler });

    controller.beginMove('window-1', pointer(100, 80, pointerType), {
      usableArea,
      titleBarHeight: 40,
      minimumVisibleTitleBarWidth: 96,
      source: 'draggable',
    });
    assert.equal(await controller.endPointer(pointer(160, 150, pointerType)), true);
    assert.deepEqual(store.frontWindow?.bounds, { x: 160, y: 150, width: 600, height: 400 });
  }
});

test('movement keeps a recoverable title-bar region inside the usable area', () => {
  assert.deepEqual(
    moveBounds(initialBounds, -5000, -5000, usableArea, 40, 96),
    { x: -504, y: 0, width: 600, height: 400 },
  );
  assert.deepEqual(
    moveBounds(initialBounds, 5000, 5000, usableArea, 40, 96),
    { x: 1104, y: 704, width: 600, height: 400 },
  );
});

test('resize honors application minimum size and reachable usable-area handles', () => {
  assert.deepEqual(
    resizeBounds(
      initialBounds,
      1000,
      1000,
      'top-left',
      { width: 320, height: 240 },
      usableArea,
    ),
    { x: 380, y: 240, width: 320, height: 240 },
  );

  assert.deepEqual(
    resizeBounds(
      initialBounds,
      5000,
      5000,
      'bottom-right',
      { width: 320, height: 240 },
      usableArea,
    ),
    { x: 100, y: 80, width: 1100, height: 664 },
  );
});

test('resize completion emits one debounced settled result after the last pointer sample', async () => {
  const store = createStore();
  const scheduler = new ManualAnimationFrameScheduler();
  const settled: SettledResize[] = [];
  const controller = new WindowInteractionController(store, {
    scheduler,
    onResizeSettled: (value) => {
      settled.push(value);
    },
  });

  controller.beginResize('window-1', pointer(700, 480, 'touch', 7), {
    usableArea,
    minimumSize: { width: 320, height: 240 },
    edge: 'bottom-right',
  });
  controller.updatePointer(pointer(740, 520, 'touch', 7));
  controller.updatePointer(pointer(780, 560, 'touch', 7));

  assert.deepEqual(settled, []);
  assert.equal(await controller.endPointer(pointer(800, 580, 'touch', 7)), true);
  assert.deepEqual(settled, [{
    windowId: 'window-1',
    bounds: { x: 100, y: 80, width: 700, height: 500 },
    pointerType: 'touch',
  }]);
  assert.equal(scheduler.cancelCount, 1);
});

test('unrelated pointers and cancelled interactions cannot mutate the window', () => {
  const store = createStore();
  const scheduler = new ManualAnimationFrameScheduler();
  const controller = new WindowInteractionController(store, { scheduler });

  controller.beginMove('window-1', pointer(100, 80, 'pen', 9), {
    usableArea,
    titleBarHeight: 40,
    minimumVisibleTitleBarWidth: 96,
    source: 'draggable',
  });
  assert.equal(controller.updatePointer(pointer(500, 500, 'touch', 10)), false);
  assert.equal(controller.updatePointer(pointer(500, 500, 'pen', 9)), true);
  assert.equal(controller.cancelPointer(10), false);
  assert.equal(controller.cancelPointer(9), true);
  scheduler.flush();

  assert.deepEqual(store.frontWindow?.bounds, initialBounds);
});

test('fixed windows reject move and resize interaction starts', () => {
  const store = createStore();
  store.maximize('window-1', usableArea);
  const controller = new WindowInteractionController(store, {
    scheduler: new ManualAnimationFrameScheduler(),
  });

  assert.throws(
    () => controller.beginMove('window-1', pointer(100, 80), {
      usableArea,
      titleBarHeight: 40,
      minimumVisibleTitleBarWidth: 96,
      source: 'draggable',
    }),
    (error: unknown) =>
      error instanceof WindowStoreError && error.code === 'window.interaction_state_invalid',
  );
});
