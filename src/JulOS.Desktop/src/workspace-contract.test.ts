import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';

import {
  applicationViewportFor,
  classifyWorkspace,
  resolveWorkspace,
  selectLayout,
  workspaceApplicationViewport,
  WorkspaceContractError,
  type PresentationCapabilities,
  type WorkspaceClass,
  type WorkspaceClassOverride,
} from './workspace-contract.js';

interface ResolutionCase {
  readonly name: string;
  readonly capabilities: PresentationCapabilities;
  readonly override: WorkspaceClassOverride | null;
  readonly multiDisplayRequested: boolean;
  readonly activeDisplayParticipants: number;
  readonly expectedDetected: WorkspaceClassOverride;
  readonly expectedResolved: WorkspaceClass;
}

/** Walks up from the compiled test output until the repository fixtures are visible. */
function repositoryRoot(): string {
  let directory = dirname(fileURLToPath(import.meta.url));
  while (!existsSync(join(directory, 'tests', 'fixtures'))) {
    const parent = dirname(directory);
    if (parent === directory) {
      throw new Error('The repository root could not be located from the test output directory.');
    }
    directory = parent;
  }
  return directory;
}

function capabilities(
  overrides: Partial<PresentationCapabilities> = {},
): PresentationCapabilities {
  return {
    primaryPointerCoarse: false,
    anyPointerFine: true,
    screenMinimumDimensionCssPx: 1080,
    layoutViewportWidthCssPx: 1920,
    ...overrides,
  };
}

test('a touch-only device below 600 CSS px on its shorter edge is a phone', () => {
  assert.equal(
    classifyWorkspace(capabilities({
      primaryPointerCoarse: true,
      anyPointerFine: false,
      screenMinimumDimensionCssPx: 393,
      layoutViewportWidthCssPx: 393,
    })),
    'phone',
  );
});

test('a phone in landscape stays a phone because the minimum dimension is stable', () => {
  const portrait = capabilities({
    primaryPointerCoarse: true,
    anyPointerFine: false,
    screenMinimumDimensionCssPx: 393,
    layoutViewportWidthCssPx: 393,
  });
  const landscape = { ...portrait, layoutViewportWidthCssPx: 852 };

  assert.equal(classifyWorkspace(portrait), 'phone');
  assert.equal(classifyWorkspace(landscape), 'phone');
});

test('a touch-only device with a larger short edge is a tablet', () => {
  assert.equal(
    classifyWorkspace(capabilities({
      primaryPointerCoarse: true,
      anyPointerFine: false,
      screenMinimumDimensionCssPx: 834,
      layoutViewportWidthCssPx: 1194,
    })),
    'tablet',
  );
});

test('a pointer device falls back to layout viewport thresholds', () => {
  assert.equal(classifyWorkspace(capabilities({ layoutViewportWidthCssPx: 599 })), 'phone');
  assert.equal(classifyWorkspace(capabilities({ layoutViewportWidthCssPx: 600 })), 'tablet');
  assert.equal(classifyWorkspace(capabilities({ layoutViewportWidthCssPx: 1023 })), 'tablet');
  assert.equal(classifyWorkspace(capabilities({ layoutViewportWidthCssPx: 1024 })), 'desktop-single');
});

test('a touch device that also has a fine pointer uses the width rules', () => {
  // A tablet with a trackpad attached behaves like a pointer device.
  assert.equal(
    classifyWorkspace(capabilities({
      primaryPointerCoarse: true,
      anyPointerFine: true,
      screenMinimumDimensionCssPx: 400,
      layoutViewportWidthCssPx: 1400,
    })),
    'desktop-single',
  );
});

test('classification never produces desktop-multi', () => {
  const widths = [320, 599, 600, 1023, 1024, 3840];
  for (const width of widths) {
    assert.notEqual(
      classifyWorkspace(capabilities({ layoutViewportWidthCssPx: width })),
      'desktop-multi',
    );
  }
});

test('a stored device override is authoritative over detection', () => {
  const resolved = resolveWorkspace({
    capabilities: capabilities({ layoutViewportWidthCssPx: 1920 }),
    override: 'tablet',
    multiDisplayRequested: false,
    activeDisplayParticipants: 1,
  });

  assert.equal(resolved.detected, 'desktop-single');
  assert.equal(resolved.resolved, 'tablet');
  assert.equal(resolved.overrideApplied, true);
});

test('desktop-multi needs both an explicit request and two participants', () => {
  const base = {
    capabilities: capabilities(),
    override: null,
    activeDisplayParticipants: 2,
  };

  assert.equal(resolveWorkspace({ ...base, multiDisplayRequested: false }).resolved, 'desktop-single');
  assert.equal(
    resolveWorkspace({ ...base, multiDisplayRequested: true, activeDisplayParticipants: 1 }).resolved,
    'desktop-single',
  );
  assert.equal(resolveWorkspace({ ...base, multiDisplayRequested: true }).resolved, 'desktop-multi');
});

test('a device cannot be pinned to desktop-multi', () => {
  assert.throws(
    () => resolveWorkspace({
      capabilities: capabilities(),
      override: 'desktop-multi' as never,
      multiDisplayRequested: false,
      activeDisplayParticipants: 1,
    }),
    (error: unknown) => error instanceof WorkspaceContractError
      && error.code === 'client_device.workspace_preference_invalid',
  );
});

test('every workspace class maps to exactly one application viewport class', () => {
  assert.deepEqual(workspaceApplicationViewport, {
    phone: 'mobile',
    tablet: 'tablet',
    'desktop-single': 'desktop',
    'desktop-multi': 'desktop',
  });
  assert.equal(applicationViewportFor('phone'), 'mobile');
  assert.equal(applicationViewportFor('desktop-multi'), 'desktop');
});

test('a fresh restore mode neither restores nor persists window state', () => {
  const selection = selectLayout('phone', {
    workspaceClass: 'phone',
    layoutScope: 'device',
    restoreMode: 'fresh',
  });

  assert.equal(selection.scope, 'device');
  assert.equal(selection.restoresWindows, false);
  assert.equal(selection.persistsWindowState, false);
});

test('a workspace without a device preference uses the shared layout', () => {
  const selection = selectLayout('desktop-single', null);

  assert.equal(selection.scope, 'shared');
  assert.equal(selection.restoresWindows, true);
  assert.equal(selection.persistsWindowState, true);
});

test('a preference for another workspace class is rejected', () => {
  assert.throws(
    () => selectLayout('phone', {
      workspaceClass: 'tablet',
      layoutScope: 'shared',
      restoreMode: 'resume',
    }),
    (error: unknown) => error instanceof WorkspaceContractError
      && error.code === 'client_device.workspace_preference_invalid',
  );
});

test('classification rejects non-positive dimensions instead of guessing', () => {
  assert.throws(
    () => classifyWorkspace(capabilities({ layoutViewportWidthCssPx: 0 })),
    (error: unknown) => error instanceof WorkspaceContractError,
  );
});

test('the committed workspace-resolution fixtures all resolve as documented', () => {
  const path = join(repositoryRoot(), 'tests', 'fixtures', 'mobile-pwa', 'workspace-resolution.json');
  // Repository JSON carries a byte order mark under the D012 encoding policy, which
  // JSON.parse rejects; tools/lib/package-manifest.mjs strips it the same way.
  const text = readFileSync(path, 'utf8').replace(/^﻿/, '');
  const fixture = JSON.parse(text) as { cases: readonly ResolutionCase[] };

  assert.ok(fixture.cases.length >= 10, 'the fixture set must cover every documented rule');

  for (const entry of fixture.cases) {
    const resolved = resolveWorkspace({
      capabilities: entry.capabilities,
      override: entry.override,
      multiDisplayRequested: entry.multiDisplayRequested,
      activeDisplayParticipants: entry.activeDisplayParticipants,
    });

    assert.equal(resolved.detected, entry.expectedDetected, `detected: ${entry.name}`);
    assert.equal(resolved.resolved, entry.expectedResolved, `resolved: ${entry.name}`);
  }
});

test('presentation capabilities expose no hardware fingerprint', () => {
  // docs/MOBILE_PWA.md section 2 forbids user-agent and hardware fingerprinting, so the
  // classifier cannot be handed such a value in the first place.
  const fields = Object.keys({
    primaryPointerCoarse: false,
    anyPointerFine: true,
    screenMinimumDimensionCssPx: 1,
    layoutViewportWidthCssPx: 1,
  } satisfies PresentationCapabilities).sort();

  assert.deepEqual(fields, [
    'anyPointerFine',
    'layoutViewportWidthCssPx',
    'primaryPointerCoarse',
    'screenMinimumDimensionCssPx',
  ]);
});
