import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  classifyViewport,
  deriveResponsiveDesktop,
  viewportLayoutKey,
} from './responsive-desktop.js';
import type { DesktopWindowSnapshot } from './window-store.js';

function window(id: string, state: DesktopWindowSnapshot['state'] = 'normal'): DesktopWindowSnapshot {
  return {
    id,
    applicationId: `app.${id}`,
    launchTargetId: null,
    title: id,
    state,
    bounds: { x: 0, y: 0, width: 500, height: 400 },
    restoreBounds: { x: 0, y: 0, width: 500, height: 400 },
    zIndex: Number(id.replace('window-', '')),
  };
}

test('viewport thresholds are deterministic', () => {
  assert.equal(classifyViewport(719), 'mobile');
  assert.equal(classifyViewport(720), 'tablet');
  assert.equal(classifyViewport(1099), 'tablet');
  assert.equal(classifyViewport(1100), 'desktop');
});

test('mobile shows one active window and uses task switching', () => {
  const state = deriveResponsiveDesktop(
    390,
    [window('window-1'), window('window-2'), window('window-3', 'minimized')],
    'window-1',
  );

  assert.equal(state.viewportClass, 'mobile');
  assert.equal(state.usesTaskSwitching, true);
  assert.deepEqual(state.visibleWindows.map((item) => item.id), ['window-1']);
});

test('desktop retains all non-minimized windows', () => {
  const state = deriveResponsiveDesktop(
    1440,
    [window('window-1'), window('window-2'), window('window-3', 'minimized')],
    'window-1',
  );

  assert.equal(state.usesTaskSwitching, false);
  assert.deepEqual(state.visibleWindows.map((item) => item.id), ['window-1', 'window-2']);
});

test('layout keys cannot collide across viewport classes', () => {
  assert.notEqual(viewportLayoutKey('user-1', 'mobile'), viewportLayoutKey('user-1', 'desktop'));
});
