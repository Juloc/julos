import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  edgeForPointer,
  placeTransferredWindow,
  resolveDisplayTarget,
  type WorkspaceDisplay,
} from './multi-display-workspace.js';

const area = { x: 0, y: 0, width: 1200, height: 744 };

test('display edge detection only activates inside the transfer threshold', () => {
  assert.equal(edgeForPointer(0, area), 'left');
  assert.equal(edgeForPointer(12, area), 'left');
  assert.equal(edgeForPointer(13, area), null);
  assert.equal(edgeForPointer(1187, area), null);
  assert.equal(edgeForPointer(1188, area), 'right');
  assert.equal(edgeForPointer(1200, area), 'right');
});

test('display order follows connection order and does not wrap at outer edges', () => {
  const first: WorkspaceDisplay = { displayId: 'display-a', startedAt: 100 };
  const second: WorkspaceDisplay = { displayId: 'display-b', startedAt: 200 };
  const third: WorkspaceDisplay = { displayId: 'display-c', startedAt: 300 };

  assert.deepEqual(resolveDisplayTarget(first.displayId, [second, third], 'right', first), second);
  assert.equal(resolveDisplayTarget(first.displayId, [second, third], 'left', first), null);
  assert.deepEqual(resolveDisplayTarget(second.displayId, [first, third], 'left', second), first);
  assert.deepEqual(resolveDisplayTarget(second.displayId, [first, third], 'right', second), third);
  assert.equal(resolveDisplayTarget(third.displayId, [first, second], 'right', third), null);
});

test('transferred windows enter from the matching edge and retain vertical position', () => {
  const source = { x: 900, y: 172, width: 500, height: 400 };

  assert.deepEqual(
    placeTransferredWindow(source, area, 'right', 0.5),
    { x: 24, y: 172, width: 500, height: 400 },
  );
  assert.deepEqual(
    placeTransferredWindow(source, area, 'left', 0.5),
    { x: 676, y: 172, width: 500, height: 400 },
  );
});

test('transferred windows are clamped when the target display is smaller', () => {
  const smallArea = { x: 0, y: 0, width: 800, height: 500 };
  const source = { x: 0, y: 0, width: 1200, height: 700 };

  assert.deepEqual(
    placeTransferredWindow(source, smallArea, 'right', 1),
    { x: 0, y: 0, width: 800, height: 500 },
  );
});
