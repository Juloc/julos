import assert from 'node:assert/strict';
import { test } from 'node:test';

import type { UpdateLayoutState } from './pwa-cache-policy.js';
import {
  PwaUpdateController,
  type ServiceWorkerMessaging,
  type UpdateAvailability,
} from './pwa-update.js';

interface Harness {
  readonly controller: PwaUpdateController;
  readonly posted: unknown[];
  readonly reported: UpdateAvailability[];
  readonly reloads: () => number;
  readonly announce: (buildId: string) => void;
  readonly flushes: () => number;
}

function harness(
  state: UpdateLayoutState,
  flushResult = true,
): Harness {
  const posted: unknown[] = [];
  const reported: UpdateAvailability[] = [];
  let reloadCount = 0;
  let flushCount = 0;
  let listener: ((event: MessageEvent) => void) | null = null;

  const messaging: ServiceWorkerMessaging = {
    addEventListener: (_type, handler) => {
      listener = handler;
    },
    controller: { postMessage: (message) => posted.push(message) },
  };

  const controller = new PwaUpdateController({
    messaging,
    layout: {
      state: () => state,
      flush: async () => {
        flushCount += 1;
        return flushResult;
      },
    },
    onAvailable: (availability) => reported.push(availability),
    reload: () => {
      reloadCount += 1;
    },
    clientId: 'client-1',
  });

  return {
    controller,
    posted,
    reported,
    reloads: () => reloadCount,
    flushes: () => flushCount,
    announce: (buildId) => listener?.({ data: { type: 'JULOS_UPDATE_READY', buildId } } as MessageEvent),
  };
}

test('an announced update never reloads the page by itself', () => {
  const test1 = harness('clean');

  test1.announce('0.4.0-beta.43');

  assert.equal(test1.reloads(), 0, 'activation must never force a reload');
  assert.equal(test1.controller.pendingBuildId, '0.4.0-beta.43');
});

test('an announced update reports the page layout state back to the worker', () => {
  const test1 = harness('dirty');

  test1.announce('0.4.0-beta.43');

  assert.deepEqual(test1.posted, [{
    type: 'JULOS_UPDATE_STATUS',
    buildId: '0.4.0-beta.43',
    clientId: 'client-1',
    layoutState: 'dirty',
  }]);
});

test('a clean page may reload immediately once accepted', async () => {
  const test1 = harness('clean');
  test1.announce('0.4.0-beta.43');

  // clean and fresh may reload immediately once the user accepts; dirty and conflict
  // must wait for a successful flush or an explicit discard.
  assert.equal(test1.reported.at(-1)?.decision, 'reload');

  const decision = await test1.controller.accept();

  assert.equal(decision, 'reload');
  assert.equal(test1.flushes(), 0, 'a clean layout needs no flush');
  assert.equal(test1.reloads(), 1);
});

test('a dirty page flushes its layout before it reloads', async () => {
  const test1 = harness('dirty');
  test1.announce('0.4.0-beta.43');

  const decision = await test1.controller.accept();

  assert.equal(decision, 'reload');
  assert.equal(test1.flushes(), 1);
  assert.equal(test1.reloads(), 1);
});

test('a failed flush leaves the page running instead of reloading', async () => {
  const test1 = harness('dirty', false);
  test1.announce('0.4.0-beta.43');

  const decision = await test1.controller.accept();

  assert.equal(decision, 'blocked');
  assert.equal(test1.reloads(), 0, 'an offline or conflicting flush must not reload');
  assert.equal(test1.reported.at(-1)?.decision, 'blocked');
});

test('a conflict can be resolved by explicitly discarding this page changes', async () => {
  const test1 = harness('conflict', false);
  test1.announce('0.4.0-beta.43');

  const decision = await test1.controller.accept(true);

  assert.equal(decision, 'reload');
  assert.equal(test1.flushes(), 0, 'an explicit discard skips the flush');
  assert.equal(test1.reloads(), 1);
});

test('activation is only requested for the announced build', async () => {
  const test1 = harness('fresh');
  test1.announce('0.4.0-beta.43');

  await test1.controller.accept();

  assert.deepEqual(test1.posted.at(-2), {
    type: 'JULOS_ACTIVATE_UPDATE',
    buildId: '0.4.0-beta.43',
  });
  assert.deepEqual(test1.posted.at(-1), {
    type: 'JULOS_CLIENT_READY_TO_RELOAD',
    buildId: '0.4.0-beta.43',
    clientId: 'client-1',
  });
});

test('accepting without an announced update does nothing', async () => {
  const test1 = harness('clean');

  assert.equal(await test1.controller.accept(), 'blocked');
  assert.equal(test1.reloads(), 0);
});

test('a malformed worker message is ignored', () => {
  const test1 = harness('clean');

  test1.announce('');

  assert.equal(test1.controller.pendingBuildId, null);
  assert.equal(test1.posted.length, 0);
});
