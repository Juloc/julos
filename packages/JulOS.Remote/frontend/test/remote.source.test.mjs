import assert from 'node:assert/strict';
import test from 'node:test';

import {
  createKeyboardPipeline,
  createPointerPipeline,
  createResizeScheduler,
  isKeyboardReleaseShortcut,
  splitDisplayEndpoint,
  validateDisplayDescriptor,
} from '../remote.source.js';

const origin = 'https://os.example.test';

test('display descriptor stays same-origin and token-free', () => {
  const descriptor = validateDisplayDescriptor({
    kind: 'graphical',
    contractVersion: '1.0.0',
    endpoint: '/api/v1/remote/sessions/11111111-1111-4111-8111-111111111111/display?package=de.juloc.julos.remote&revision=7&expires=1785873660',
    expiresAtUtc: '2026-08-04T21:01:00+00:00',
  });

  const endpoint = splitDisplayEndpoint(descriptor.endpoint, origin);
  assert.equal(
    endpoint.tunnelUrl,
    '/api/v1/remote/sessions/11111111-1111-4111-8111-111111111111/display',
  );
  assert.equal(
    endpoint.connectData,
    'package=de.juloc.julos.remote&revision=7&expires=1785873660',
  );
  assert.throws(() => validateDisplayDescriptor({
    ...descriptor,
    endpoint: '/display?access_token=secret',
  }));
});

test('mobile text input uses one keyboard pipeline and an explicit local release shortcut', () => {
  let keyboardCount = 0;
  let sinkCount = 0;
  const keyEvents = [];
  const appended = [];
  const removed = [];
  const listeners = new Map();
  let blurred = 0;
  let released = 0;

  class Keyboard {
    constructor(target) {
      keyboardCount += 1;
      this.target = target;
      this.onkeydown = null;
      this.onkeyup = null;
      this.resetCount = 0;
    }

    reset() {
      this.resetCount += 1;
    }
  }

  class InputSink {
    constructor() {
      sinkCount += 1;
      this.element = { remove: () => removed.push(this.element) };
    }

    getElement() {
      return this.element;
    }
  }

  const target = {
    append: (element) => appended.push(element),
    addEventListener: (name, handler, capture) => listeners.set(`${name}:${capture}`, handler),
    removeEventListener: (name, handler, capture) => {
      if (listeners.get(`${name}:${capture}`) === handler) {
        listeners.delete(`${name}:${capture}`);
      }
    },
    blur: () => { blurred += 1; },
  };
  const client = { sendKeyEvent: (...args) => keyEvents.push(args) };
  const pipeline = createKeyboardPipeline(
    { Keyboard, InputSink },
    target,
    client,
    true,
    () => { released += 1; },
  );

  assert.equal(keyboardCount, 1);
  assert.equal(sinkCount, 1);
  assert.equal(appended.length, 1);
  pipeline.keyboard.onkeydown(65);
  pipeline.keyboard.onkeyup(65);
  assert.deepEqual(keyEvents, [[1, 65], [0, 65]]);

  const releaseHandler = listeners.get('keydown:true');
  assert.equal(typeof releaseHandler, 'function');
  const event = {
    key: 'Escape',
    ctrlKey: true,
    altKey: true,
    shiftKey: true,
    prevented: 0,
    stopped: 0,
    preventDefault() { this.prevented += 1; },
    stopImmediatePropagation() { this.stopped += 1; },
  };
  assert.equal(isKeyboardReleaseShortcut(event), true);
  releaseHandler(event);
  assert.equal(event.prevented, 1);
  assert.equal(event.stopped, 1);
  assert.equal(blurred, 1);
  assert.equal(released, 1);
  assert.equal(pipeline.keyboard.resetCount, 1);

  pipeline.dispose();
  assert.equal(pipeline.keyboard.resetCount, 2);
  assert.equal(removed.length, 1);
  assert.equal(listeners.size, 0);
});

test('resize delivery collapses repeated observations and disposal cancels pending work', () => {
  let nextId = 0;
  const pending = new Map();
  const delays = [];
  const timers = {
    setTimeout(callback, delay) {
      nextId += 1;
      pending.set(nextId, callback);
      delays.push(delay);
      return nextId;
    },
    clearTimeout(id) {
      pending.delete(id);
    },
  };
  let runs = 0;
  const scheduler = createResizeScheduler(() => { runs += 1; }, 150, timers);

  scheduler.schedule();
  scheduler.schedule();
  scheduler.schedule();
  assert.equal(pending.size, 1);
  assert.deepEqual(delays, [150, 150, 150]);
  assert.equal(runs, 0);

  const [firedId, callback] = [...pending.entries()][0];
  pending.delete(firedId);
  callback();
  assert.equal(runs, 1);

  scheduler.schedule();
  scheduler.dispose();
  assert.equal(pending.size, 0);
  assert.equal(runs, 1);
});

test('pointer input selects one desktop or touch adapter', () => {
  const sent = [];
  let mouseCount = 0;
  let touchCount = 0;

  class Mouse {
    constructor() {
      mouseCount += 1;
      this.onmousedown = null;
      this.onmouseup = null;
      this.onmousemove = null;
    }
  }
  Mouse.Touchscreen = class Touchscreen extends Mouse {
    constructor() {
      super();
      mouseCount -= 1;
      touchCount += 1;
    }
  };

  const client = { sendMouseState: (state) => sent.push(state) };
  const desktop = createPointerPipeline({ Mouse }, {}, client, false);
  assert.equal(mouseCount, 1);
  assert.equal(touchCount, 0);
  desktop.pointer.onmousemove({ x: 1, y: 2 });
  desktop.dispose();

  const touch = createPointerPipeline({ Mouse }, {}, client, true);
  assert.equal(mouseCount, 1);
  assert.equal(touchCount, 1);
  touch.pointer.onmousedown({ x: 3, y: 4 });
  touch.dispose();

  assert.deepEqual(sent, [{ x: 1, y: 2 }, { x: 3, y: 4 }]);
});
