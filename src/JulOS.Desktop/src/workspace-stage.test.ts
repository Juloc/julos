import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  clampSplitRatioPermille,
  deriveWorkspaceStage,
  maximumSplitRatioPermille,
  minimumSplitRatioPermille,
  phoneDividerThicknessCssPx,
  resolveDisplaySlots,
  WorkspaceStageError,
  type WorkspaceStageInput,
} from './workspace-stage.js';
import type { DesktopWindowSnapshot } from './window-store.js';

const portrait = { x: 0, y: 0, width: 390, height: 780 };
const landscape = { x: 0, y: 0, width: 780, height: 390 };
const desktopArea = { x: 0, y: 0, width: 1600, height: 900 };

test('a phone with no foreground window shows an empty stage', () => {
  const stage = deriveWorkspaceStage(phoneInput([window('a', 0)], {
    primaryWindowId: null,
    secondaryWindowId: null,
    splitRatioPermille: null,
  }));

  assert.equal(stage.presentation, 'phone-empty');
  assert.deepEqual(stage.placements, []);
  assert.equal(stage.taskWindows.length, 1, 'An open window stays reachable in the task switcher.');
});

test('a phone single stage fills the usable area with one window', () => {
  const stage = deriveWorkspaceStage(phoneInput([window('a', 0)], {
    primaryWindowId: 'a',
    secondaryWindowId: null,
    splitRatioPermille: null,
  }));

  assert.equal(stage.presentation, 'phone-single');
  assert.equal(stage.placements.length, 1);
  assert.deepEqual(stage.placements[0]?.bounds, portrait);
  assert.equal(stage.divider, null);
});

test('portrait splits top and bottom, landscape splits left and right', () => {
  const foreground = {
    primaryWindowId: 'a',
    secondaryWindowId: 'b',
    splitRatioPermille: 500,
  };
  const windows = [window('a', 1), window('b', 0)];

  const tall = deriveWorkspaceStage(phoneInput(windows, foreground));
  const wide = deriveWorkspaceStage({ ...phoneInput(windows, foreground), area: landscape });

  assert.equal(tall.orientation, 'portrait');
  assert.equal(tall.presentation, 'phone-split');
  assert.equal(tall.placements[0]?.bounds.width, portrait.width, 'Portrait panes span the full width.');
  assert.equal(tall.placements[1]?.bounds.width, portrait.width);
  assert.ok((tall.placements[1]?.bounds.y ?? 0) > (tall.placements[0]?.bounds.y ?? 0));

  assert.equal(wide.orientation, 'landscape');
  assert.equal(wide.placements[0]?.bounds.height, landscape.height, 'Landscape panes span the full height.');
  assert.ok((wide.placements[1]?.bounds.x ?? 0) > (wide.placements[0]?.bounds.x ?? 0));
});

test('the two panes and the divider exactly fill the usable area', () => {
  const stage = deriveWorkspaceStage(phoneInput([window('a', 1), window('b', 0)], {
    primaryWindowId: 'a',
    secondaryWindowId: 'b',
    splitRatioPermille: 350,
  }));

  const primary = stage.placements[0]?.bounds;
  const secondary = stage.placements[1]?.bounds;
  const divider = stage.divider;
  assert.ok(primary && secondary && divider);
  assert.equal(divider.height, phoneDividerThicknessCssPx);
  assert.equal(primary.height + divider.height + secondary.height, portrait.height);
  assert.equal(divider.y, primary.y + primary.height);
  assert.equal(secondary.y, divider.y + divider.height);
});

test('a phone never shows more than two foreground windows', () => {
  const stage = deriveWorkspaceStage(phoneInput(
    [window('a', 2), window('b', 1), window('c', 0)],
    { primaryWindowId: 'a', secondaryWindowId: 'b', splitRatioPermille: 500 },
  ));

  assert.equal(stage.placements.length, 2);
  assert.equal(stage.taskWindows.length, 3, 'The third window stays open in the task switcher.');
});

test('a foreground window that is gone or minimized is not rendered as an empty pane', () => {
  const closed = deriveWorkspaceStage(phoneInput([window('b', 0)], {
    primaryWindowId: 'a',
    secondaryWindowId: 'b',
    splitRatioPermille: 500,
  }));
  assert.equal(closed.presentation, 'phone-empty', 'A missing primary leaves no stage to render.');

  const hidden = deriveWorkspaceStage(phoneInput(
    [window('a', 1), { ...window('b', 0), state: 'minimized' }],
    { primaryWindowId: 'a', secondaryWindowId: 'b', splitRatioPermille: 500 },
  ));
  assert.equal(hidden.presentation, 'phone-single');
});

test('a screen too small for two operable panes presents the primary window alone', () => {
  const stage = deriveWorkspaceStage({
    ...phoneInput([window('a', 1), window('b', 0)], {
      primaryWindowId: 'a',
      secondaryWindowId: 'b',
      splitRatioPermille: 500,
    }),
    area: { x: 0, y: 0, width: 320, height: 300 },
  });

  assert.equal(stage.presentation, 'phone-single');
  assert.equal(stage.divider, null);
});

test('a split position is only ever stored inside the documented range', () => {
  assert.equal(clampSplitRatioPermille(10), minimumSplitRatioPermille);
  assert.equal(clampSplitRatioPermille(990), maximumSplitRatioPermille);
  assert.equal(clampSplitRatioPermille(412.6), 413);
  assert.throws(() => clampSplitRatioPermille(Number.NaN), WorkspaceStageError);
});

test('changing orientation changes geometry only, never which windows are shown', () => {
  const foreground = { primaryWindowId: 'a', secondaryWindowId: 'b', splitRatioPermille: 500 };
  const windows = [window('a', 1), window('b', 0)];

  const tall = deriveWorkspaceStage(phoneInput(windows, foreground));
  const wide = deriveWorkspaceStage({ ...phoneInput(windows, foreground), area: landscape });

  assert.deepEqual(
    tall.placements.map((placement) => placement.windowId),
    wide.placements.map((placement) => placement.windowId),
  );
  assert.deepEqual(
    tall.placements.map((placement) => placement.pane),
    wide.placements.map((placement) => placement.pane),
  );
});

test('the arrangement follows the workspace class, not the size of the area', () => {
  const windows = [window('a', 1), window('b', 0)];
  const foreground = { primaryWindowId: 'a', secondaryWindowId: 'b', splitRatioPermille: 500 };

  // A phone pinned by its owner keeps the phone arrangement on a wide screen, and a
  // tablet keeps tiling on a narrow one. Resizing, rotating or opening a software
  // keyboard therefore cannot change which layout a session is looking at.
  const wide = deriveWorkspaceStage({ ...phoneInput(windows, foreground), area: desktopArea });
  const narrow = deriveWorkspaceStage({
    workspaceClass: 'tablet',
    windows,
    area: portrait,
    activeWindowId: null,
  });

  assert.equal(wide.presentation, 'phone-split');
  assert.equal(narrow.presentation, 'tiled');
});

test('a tablet tiles its visible windows without overlap', () => {
  const stage = deriveWorkspaceStage({
    workspaceClass: 'tablet',
    windows: [window('a', 2), window('b', 1), window('c', 0)],
    area: desktopArea,
    activeWindowId: 'a',
  });

  assert.equal(stage.presentation, 'tiled');
  assert.equal(stage.placements.length, 3, 'A tablet holds more than two visible applications.');
  assertNoOverlap(stage.placements.map((placement) => placement.bounds));
});

test('a tablet tiles two windows side by side on a wide area', () => {
  const stage = deriveWorkspaceStage({
    workspaceClass: 'tablet',
    windows: [window('a', 1), window('b', 0)],
    area: desktopArea,
    activeWindowId: null,
  });

  const [first, second] = stage.placements;
  assert.ok(first && second);
  assert.equal(first.bounds.height, desktopArea.height);
  assert.equal(second.bounds.height, desktopArea.height);
  assert.equal(first.bounds.width + second.bounds.width, desktopArea.width);
});

test('a tablet with free placement enabled uses the stored window bounds', () => {
  const stage = deriveWorkspaceStage({
    workspaceClass: 'tablet',
    windows: [window('a', 0)],
    area: desktopArea,
    activeWindowId: null,
    freeWindowPlacement: true,
  });

  assert.equal(stage.presentation, 'windowed');
  assert.deepEqual(stage.placements[0]?.bounds, { x: 40, y: 50, width: 800, height: 600 });
});

test('a minimized window is not placed but stays in the task switcher', () => {
  const stage = deriveWorkspaceStage({
    workspaceClass: 'desktop-single',
    windows: [window('a', 1), { ...window('b', 0), state: 'minimized' }],
    area: desktopArea,
    activeWindowId: null,
  });

  assert.deepEqual(stage.placements.map((placement) => placement.windowId), ['a']);
  assert.equal(stage.taskWindows.length, 2);
});

test('a window whose display is absent is recovered onto the earliest surviving one', () => {
  const resolved = resolveDisplaySlots(
    new Map([['a', 0], ['b', 2], ['c', 1]]),
    [1, 0],
  );

  assert.equal(resolved.get('a'), 0);
  assert.equal(resolved.get('c'), 1);
  assert.equal(resolved.get('b'), 0, 'Slot 2 is gone, so its window falls back to the earliest active slot.');
});

test('recovering a window onto another display does not rewrite its stored slot', () => {
  const stored = new Map([['a', 2]]);

  const resolved = resolveDisplaySlots(stored, [0]);

  assert.equal(resolved.get('a'), 0);
  assert.equal(stored.get('a'), 2, 'A temporarily absent display must not move the window for good.');
});

test('each display renders only the windows it owns', () => {
  const input: WorkspaceStageInput = {
    workspaceClass: 'desktop-multi',
    windows: [window('a', 2), window('b', 1), window('c', 0)],
    area: desktopArea,
    activeWindowId: null,
    displaySlot: 1,
    activeDisplaySlots: [0, 1],
    windowDisplaySlots: new Map([['a', 0], ['b', 1], ['c', 5]]),
  };

  const second = deriveWorkspaceStage(input);
  const first = deriveWorkspaceStage({ ...input, displaySlot: 0 });

  assert.deepEqual(second.placements.map((placement) => placement.windowId), ['b']);
  assert.deepEqual(
    first.placements.map((placement) => placement.windowId),
    ['a', 'c'],
    'The window whose display is absent is rendered by the earliest surviving display.');
  assert.equal(second.taskWindows.length, 3, 'Every window stays listed on every display.');
});

test('an area with no extent is rejected rather than producing invisible windows', () => {
  assert.throws(
    () => deriveWorkspaceStage({
      workspaceClass: 'desktop-single',
      windows: [],
      area: { x: 0, y: 0, width: 0, height: 900 },
      activeWindowId: null,
    }),
    WorkspaceStageError,
  );
});

function phoneInput(
  windows: readonly DesktopWindowSnapshot[],
  phone: { primaryWindowId: string | null; secondaryWindowId: string | null; splitRatioPermille: number | null },
): WorkspaceStageInput {
  return {
    workspaceClass: 'phone',
    windows,
    area: portrait,
    activeWindowId: null,
    phone,
  };
}

function window(id: string, zIndex: number): DesktopWindowSnapshot {
  return {
    id,
    applicationId: `app-${id}`,
    launchTargetId: null,
    title: id,
    state: 'normal',
    bounds: { x: 40, y: 50, width: 800, height: 600 },
    restoreBounds: { x: 40, y: 50, width: 800, height: 600 },
    zIndex,
  };
}

function assertNoOverlap(bounds: readonly { x: number; y: number; width: number; height: number }[]): void {
  for (let left = 0; left < bounds.length; left++) {
    for (let right = left + 1; right < bounds.length; right++) {
      const a = bounds[left] as { x: number; y: number; width: number; height: number };
      const b = bounds[right] as { x: number; y: number; width: number; height: number };
      const separated = a.x + a.width <= b.x
        || b.x + b.width <= a.x
        || a.y + a.height <= b.y
        || b.y + b.height <= a.y;
      assert.ok(separated, `Tiles ${left} and ${right} overlap.`);
    }
  }
}
