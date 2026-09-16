import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  SurfaceActivityMonitor,
  type MutationObserverLike,
  type SurfaceActivityViolation,
} from './surface-activity.js';
import { surfaceErrorCodes } from './surface-scheduler.js';

test('a suspended Surface that renders nothing is quiet', () => {
  const observers = new FakeObservers();
  const monitor = new SurfaceActivityMonitor({ createObserver: observers.create });

  monitor.watch('w1', {} as Node);

  assert.equal(monitor.isQuiet('w1'), true);
  assert.equal(monitor.mutationsSinceSuspend('w1'), 0);
});

test('a suspended Surface that keeps rendering produces one bounded failure', () => {
  const observers = new FakeObservers();
  const violations: SurfaceActivityViolation[] = [];
  const monitor = new SurfaceActivityMonitor({
    createObserver: observers.create,
    onViolation: (violation) => violations.push(violation),
  });
  monitor.watch('w1', {} as Node);

  observers.emit(3);
  observers.emit(5);

  assert.equal(violations.length, 1, 'A package rendering in a loop must not become a loop of failures.');
  assert.equal(violations[0]?.windowId, 'w1');
  assert.equal(violations[0]?.code, surfaceErrorCodes.timeout);
  assert.equal(monitor.isQuiet('w1'), false);
  assert.equal(monitor.mutationsSinceSuspend('w1'), 8, 'Activity keeps being counted after the first report.');
});

test('releasing a Surface stops observing it', () => {
  const observers = new FakeObservers();
  const violations: SurfaceActivityViolation[] = [];
  const monitor = new SurfaceActivityMonitor({
    createObserver: observers.create,
    onViolation: (violation) => violations.push(violation),
  });
  monitor.watch('w1', {} as Node);

  monitor.release('w1');
  observers.emit(4);

  assert.deepEqual(violations, [], 'A Surface that is running again is expected to render.');
  assert.equal(observers.disconnected, 1);
  assert.equal(monitor.mutationsSinceSuspend('w1'), 0);
});

test('watching the same window again replaces the previous observation', () => {
  const observers = new FakeObservers();
  const monitor = new SurfaceActivityMonitor({ createObserver: observers.create });
  monitor.watch('w1', {} as Node);
  observers.emit(2);

  monitor.watch('w1', {} as Node);

  assert.equal(monitor.mutationsSinceSuspend('w1'), 0, 'Each suspension is measured on its own.');
  assert.equal(observers.disconnected, 1);
});

test('releasing everything disconnects every observer', () => {
  const observers = new FakeObservers();
  const monitor = new SurfaceActivityMonitor({ createObserver: observers.create });
  monitor.watch('w1', {} as Node);
  monitor.watch('w2', {} as Node);

  monitor.releaseAll();

  assert.equal(observers.disconnected, 2);
});

/** Collects the observers the monitor creates so a test can drive them. */
class FakeObservers {
  readonly callbacks: ((mutations: readonly MutationRecord[]) => void)[] = [];
  disconnected = 0;

  readonly create = (callback: (mutations: readonly MutationRecord[]) => void): MutationObserverLike => {
    this.callbacks.push(callback);
    return {
      observe: () => undefined,
      disconnect: () => { this.disconnected += 1; },
    };
  };

  emit(count: number): void {
    const mutations = Array.from({ length: count }, () => ({} as MutationRecord));
    for (const callback of this.callbacks) {
      callback(mutations);
    }
  }
}
