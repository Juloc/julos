import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  KeyboardCommandRouter,
  clampZoomPercent,
  nextFocusable,
  reducedMotionEnabled,
} from './accessibility.js';

test('shell commands are operable without a pointer', async () => {
  const executed: string[] = [];
  const router = new KeyboardCommandRouter([
    {
      id: 'window.next',
      key: 'Tab',
      alt: true,
      execute: () => {
        executed.push('window.next');
      },
    },
    {
      id: 'launcher.open',
      key: 'Meta',
      execute: () => {
        executed.push('launcher.open');
      },
    },
  ]);

  const command = await router.handle({
    key: 'Tab',
    altKey: true,
    ctrlKey: false,
    shiftKey: false,
    metaKey: false,
    targetIsEditable: false,
  });

  assert.equal(command, 'window.next');
  assert.deepEqual(executed, ['window.next']);
});

test('plain shortcuts do not steal typing from editable controls', async () => {
  let executed = false;
  const router = new KeyboardCommandRouter([
    {
      id: 'plain.command',
      key: 'k',
      execute: () => {
        executed = true;
      },
    },
  ]);

  const command = await router.handle({
    key: 'k',
    altKey: false,
    ctrlKey: false,
    shiftKey: false,
    metaKey: false,
    targetIsEditable: true,
  });

  assert.equal(command, null);
  assert.equal(executed, false);
});

test('focus navigation skips hidden and disabled elements and wraps', () => {
  const items = [
    { id: 'one', disabled: false, hidden: false },
    { id: 'two', disabled: true, hidden: false },
    { id: 'three', disabled: false, hidden: true },
    { id: 'four', disabled: false, hidden: false },
  ];

  assert.equal(nextFocusable(items, 'one', 1), 'four');
  assert.equal(nextFocusable(items, 'four', 1), 'one');
  assert.equal(nextFocusable(items, 'one', -1), 'four');
});

test('zoom supports the accessibility range without breaking bounds', () => {
  assert.equal(clampZoomPercent(20), 50);
  assert.equal(clampZoomPercent(175.4), 175);
  assert.equal(clampZoomPercent(500), 400);
});

test('stored motion preference overrides the system only when explicit', () => {
  assert.equal(reducedMotionEnabled(null, true), true);
  assert.equal(reducedMotionEnabled(null, false), false);
  assert.equal(reducedMotionEnabled('reduced', false), true);
  assert.equal(reducedMotionEnabled('full', true), false);
});
