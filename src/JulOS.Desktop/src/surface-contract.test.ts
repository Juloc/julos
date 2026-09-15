import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  backgroundStateFor,
  resolveBackgroundMode,
  surfaceDeadlines,
  SurfaceContractError,
  transitionCalls,
  validateSurfaceDeclaration,
  type SurfaceManifestDeclaration,
} from './surface-contract.js';

function declaration(
  overrides: Partial<SurfaceManifestDeclaration> = {},
): SurfaceManifestDeclaration {
  return {
    ContractVersion: '1.0.0',
    SupportedBackgroundModes: ['suspend', 'keep-surface-active'],
    HandlesBack: true,
    ...overrides,
  };
}

test('background mode maps exactly onto a surface state', () => {
  assert.equal(backgroundStateFor('suspend'), 'suspended');
  assert.equal(backgroundStateFor('keep-surface-active'), 'background-active');
});

test('a background mode the manifest does not support is refused, not downgraded', () => {
  assert.throws(
    () => resolveBackgroundMode(
      declaration({ SupportedBackgroundModes: ['suspend'] }),
      'keep-surface-active',
    ),
    (error: unknown) => error instanceof SurfaceContractError
      && error.code === 'application.background_mode_unsupported',
  );
});

test('an unsupported major contract version fails clearly', () => {
  const errors = validateSurfaceDeclaration(declaration({ ContractVersion: '2.0.0' }));

  assert.equal(errors.length, 1);
  assert.match(errors[0]!, /major 2 is unsupported/);
});

test('a declaration must support the default phone behaviour', () => {
  const errors = validateSurfaceDeclaration(
    declaration({ SupportedBackgroundModes: ['keep-surface-active'] }),
  );

  assert.ok(errors.some((error) => error.includes('must include "suspend"')));
});

test('a valid declaration produces no errors', () => {
  assert.deepEqual(validateSurfaceDeclaration(declaration()), []);
});

test('entering the foreground from suspended re-reads data before activating', () => {
  assert.deepEqual(transitionCalls('suspended', 'foreground-focused'), ['resume', 'activate']);
});

test('entering the foreground from background-active only activates', () => {
  assert.deepEqual(transitionCalls('background-active', 'foreground-focused'), ['activate']);
});

test('leaving the visible foreground deactivates before it suspends', () => {
  assert.deepEqual(transitionCalls('foreground-focused', 'suspended'), ['deactivate', 'suspend']);
});

test('keep-surface-active backgrounding deactivates without suspending', () => {
  assert.deepEqual(transitionCalls('foreground-visible', 'background-active'), ['deactivate']);
});

test('a visible but unfocused pane is not deactivated', () => {
  // Phone Split and Tablet panes move between focused and visible inside the foreground.
  assert.deepEqual(transitionCalls('foreground-focused', 'foreground-visible'), ['activate']);
  assert.deepEqual(transitionCalls('foreground-visible', 'foreground-focused'), ['activate']);
});

test('repeating a completed transition is idempotent', () => {
  assert.deepEqual(transitionCalls('suspended', 'suspended'), []);
  assert.deepEqual(transitionCalls('foreground-focused', 'foreground-focused'), []);
});

test('dispose is terminal', () => {
  assert.deepEqual(transitionCalls('foreground-focused', 'terminated'), ['dispose']);
  assert.throws(
    () => transitionCalls('terminated', 'foreground-focused'),
    (error: unknown) => error instanceof SurfaceContractError
      && error.code === 'package.surface_terminated',
  );
});

test('back has a tighter deadline than the other lifecycle calls', () => {
  assert.equal(surfaceDeadlines.lifecycleMs, 2000);
  assert.equal(surfaceDeadlines.backMs, 500);
  assert.ok(surfaceDeadlines.backMs < surfaceDeadlines.lifecycleMs);
});
