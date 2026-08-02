import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  applyAppearance,
  isMotionMode,
  isThemeMode,
  resolveTheme,
  type AppearanceTarget,
} from './appearance.js';

test('system theme resolves from the operating-system preference', () => {
  assert.equal(resolveTheme('system', false), 'light');
  assert.equal(resolveTheme('system', true), 'dark');
  assert.equal(resolveTheme('light', true), 'light');
  assert.equal(resolveTheme('dark', false), 'dark');
});

test('only supported appearance values are accepted', () => {
  assert.equal(isThemeMode('system'), true);
  assert.equal(isThemeMode('light'), true);
  assert.equal(isThemeMode('dark'), true);
  assert.equal(isThemeMode('contrast'), false);
  assert.equal(isMotionMode('enabled'), true);
  assert.equal(isMotionMode('reduced'), true);
  assert.equal(isMotionMode('fast'), false);
});

test('appearance is exposed through root data attributes', () => {
  const dataset: Record<string, string> = {};
  applyAppearance({ dataset: dataset as DOMStringMap } satisfies AppearanceTarget, 'dark', 'reduced');

  assert.deepEqual(dataset, { theme: 'dark', motion: 'reduced' });
});
