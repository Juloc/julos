import assert from 'node:assert/strict';
import test from 'node:test';

import {
  createKeyboardPipeline,
  createPointerPipeline,
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

test('mobile text input uses exactly one keyboard pipeline', () => {
  let keyboardCount = 0;
  let sinkCount = 0;
  const keyEvents = [];
  const appended = [];
  const removed = [];

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

  const target = { append: (element) => appended.push(element) };
  const client = { sendKeyEvent: (...args) => keyEvents.push(args) };
  const pipeline = createKeyboardPipeline({ Keyboard, InputSink }, target, client, true);

  assert.equal(keyboardCount, 1);
  assert.equal(sinkCount, 1);
  assert.equal(appended.length, 1);
  pipeline.keyboard.onkeydown(65);
  pipeline.keyboard.onkeyup(65);
  assert.deepEqual(keyEvents, [[1, 65], [0, 65]]);

  pipeline.dispose();
  assert.equal(pipeline.keyboard.resetCount, 1);
  assert.equal(removed.length, 1);
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
