import assert from 'node:assert/strict';
import { test } from 'node:test';

import { SurfaceContractError, type BackOutcome, type SurfaceContext, type SurfaceHost, type SurfaceReason } from './surface-contract.js';
import { SurfaceScheduler, surfaceErrorCodes, type SurfaceFailure } from './surface-scheduler.js';

test('a newly registered Surface has not run and is activated before anything else', async () => {
  const surface = new RecordingSurface();
  const scheduler = new SurfaceScheduler();
  scheduler.register('w1', surface, 'suspend', true);

  assert.equal(scheduler.state('w1'), 'suspended');

  await scheduler.show(context('w1', 'focused'), 'window-opened');

  assert.deepEqual(surface.calls, ['resume', 'activate']);
  assert.equal(scheduler.state('w1'), 'foreground-focused');
});

test('repeating a completed transition calls nothing', async () => {
  const surface = new RecordingSurface();
  const scheduler = new SurfaceScheduler();
  scheduler.register('w1', surface, 'suspend', true);
  await scheduler.show(context('w1', 'focused'), 'window-opened');
  surface.calls.length = 0;

  await scheduler.show(context('w1', 'focused'), 'presentation-changed');

  assert.deepEqual(surface.calls, [], 'An idempotent transition produces no package calls.');
});

test('a visible but unfocused pane is foreground-visible and is not deactivated', async () => {
  const surface = new RecordingSurface();
  const scheduler = new SurfaceScheduler();
  scheduler.register('w1', surface, 'suspend', true);
  await scheduler.show(context('w1', 'focused'), 'window-opened');
  surface.calls.length = 0;

  await scheduler.show(context('w1', 'visible'), 'presentation-changed');

  assert.equal(scheduler.state('w1'), 'foreground-visible');
  assert.deepEqual(surface.calls, ['activate'], 'Losing focus is not losing visible presentation.');
});

test('leaving the foreground with suspend mode deactivates and then suspends', async () => {
  const surface = new RecordingSurface();
  const scheduler = new SurfaceScheduler();
  scheduler.register('w1', surface, 'suspend', true);
  await scheduler.show(context('w1', 'focused'), 'window-opened');
  surface.calls.length = 0;

  await scheduler.background('w1', 'window-backgrounded');

  assert.deepEqual(surface.calls, ['deactivate', 'suspend']);
  assert.equal(scheduler.state('w1'), 'suspended');
});

test('keep-surface-active deactivates but never suspends', async () => {
  const surface = new RecordingSurface();
  const scheduler = new SurfaceScheduler();
  scheduler.register('w1', surface, 'keep-surface-active', true);
  await scheduler.show(context('w1', 'focused'), 'window-opened');
  surface.calls.length = 0;

  await scheduler.background('w1', 'window-backgrounded');

  assert.deepEqual(surface.calls, ['deactivate']);
  assert.equal(scheduler.state('w1'), 'background-active');
});

test('returning from background-active activates without resuming', async () => {
  const surface = new RecordingSurface();
  const scheduler = new SurfaceScheduler();
  scheduler.register('w1', surface, 'keep-surface-active', true);
  await scheduler.show(context('w1', 'focused'), 'window-opened');
  await scheduler.background('w1', 'window-backgrounded');
  surface.calls.length = 0;

  await scheduler.show(context('w1', 'focused'), 'user-requested');

  assert.deepEqual(surface.calls, ['activate'], 'A Surface that kept its state has nothing to re-read.');
});

test('transitions for one Surface are serialized', async () => {
  const surface = new RecordingSurface();
  surface.hold = true;
  const scheduler = new SurfaceScheduler();
  scheduler.register('w1', surface, 'suspend', true);

  const first = scheduler.show(context('w1', 'focused'), 'window-opened');
  const second = scheduler.background('w1', 'window-backgrounded');
  surface.releaseAll();
  await Promise.all([first, second]);

  assert.deepEqual(
    surface.calls,
    ['resume', 'activate', 'deactivate', 'suspend'],
    'Two Shell events in the same tick must not interleave calls into one package.',
  );
});

test('dispose is terminal and later calls report it rather than throwing', async () => {
  const surface = new RecordingSurface();
  const failures: SurfaceFailure[] = [];
  const scheduler = new SurfaceScheduler({ onFailure: (failure) => failures.push(failure) });
  scheduler.register('w1', surface, 'suspend', true);
  await scheduler.show(context('w1', 'focused'), 'window-opened');

  await scheduler.dispose('w1', 'window-closed');
  assert.equal(scheduler.state('w1'), 'terminated');
  assert.equal(surface.calls.at(-1), 'dispose');

  await scheduler.show(context('w1', 'focused'), 'user-requested');

  assert.equal(scheduler.state('w1'), 'terminated');
  assert.equal(failures.at(-1)?.code, surfaceErrorCodes.terminated);
});

test('dispose and suspend are observably different calls', async () => {
  const surface = new RecordingSurface();
  const scheduler = new SurfaceScheduler();
  scheduler.register('w1', surface, 'suspend', true);
  await scheduler.show(context('w1', 'focused'), 'window-opened');
  await scheduler.background('w1', 'window-backgrounded');
  const afterSuspend = [...surface.calls];

  await scheduler.dispose('w1', 'window-closed');

  assert.ok(afterSuspend.includes('suspend'));
  assert.ok(!afterSuspend.includes('dispose'), 'Suspending must never dispose.');
  assert.ok(surface.calls.includes('dispose'));
});

test('an activate that rejects faults the Surface and reports it', async () => {
  const surface = new RecordingSurface();
  surface.rejectOn.add('activate');
  const failures: SurfaceFailure[] = [];
  const scheduler = new SurfaceScheduler({ onFailure: (failure) => failures.push(failure) });
  scheduler.register('w1', surface, 'suspend', true);

  await scheduler.show(context('w1', 'focused'), 'window-opened');

  assert.equal(scheduler.state('w1'), 'faulted');
  assert.equal(failures.length, 1);
  assert.equal(failures[0]?.call, 'activate');
});

test('a suspend that never settles is aborted on the deadline and torn down', async () => {
  const surface = new RecordingSurface();
  surface.hold = true;
  const failures: SurfaceFailure[] = [];
  const scheduler = new SurfaceScheduler({
    onFailure: (failure) => failures.push(failure),
    lifecycleDeadlineMs: 20,
  });
  scheduler.register('w1', surface, 'suspend', true);
  surface.holdExcept = new Set(['resume', 'activate']);

  await scheduler.show(context('w1', 'focused'), 'window-opened');
  await scheduler.background('w1', 'window-backgrounded');

  assert.equal(failures.at(-1)?.code, surfaceErrorCodes.timeout);
  assert.equal(
    scheduler.state('w1'),
    'terminated',
    'A Surface that cannot stop has its frontend realm torn down.',
  );
  assert.ok(surface.aborted.length > 0, 'The Shell aborts the call it gave up on.');
});

test('a package that ignores its abort signal still cannot hold the Shell', async () => {
  const surface = new RecordingSurface();
  surface.hold = true;
  surface.ignoreAbort = true;
  const scheduler = new SurfaceScheduler({ lifecycleDeadlineMs: 20 });
  scheduler.register('w1', surface, 'suspend', true);

  await scheduler.show(context('w1', 'focused'), 'window-opened');

  assert.equal(scheduler.state('w1'), 'faulted');
  surface.releaseAll();
});

test('Back reaches only a focused Surface that declares it handles Back', async () => {
  const surface = new RecordingSurface();
  const scheduler = new SurfaceScheduler();
  scheduler.register('w1', surface, 'suspend', false);
  await scheduler.show(context('w1', 'focused'), 'window-opened');

  assert.equal(await scheduler.offerBack('w1', back()), 'not-handled');

  const handler = new RecordingSurface();
  handler.backOutcome = 'handled';
  scheduler.register('w2', handler, 'suspend', true);
  assert.equal(
    await scheduler.offerBack('w2', back()),
    'not-handled',
    'A Surface that is not focused does not receive Back.',
  );

  await scheduler.show(context('w2', 'focused'), 'window-opened');
  assert.equal(await scheduler.offerBack('w2', back()), 'handled');
});

test('a Back that misses its deadline is not-handled and records a bounded failure', async () => {
  const surface = new RecordingSurface();
  surface.hold = true;
  surface.holdExcept = new Set(['resume', 'activate']);
  const failures: SurfaceFailure[] = [];
  const scheduler = new SurfaceScheduler({
    onFailure: (failure) => failures.push(failure),
    backDeadlineMs: 20,
  });
  scheduler.register('w1', surface, 'suspend', true);
  await scheduler.show(context('w1', 'focused'), 'window-opened');

  const outcome = await scheduler.offerBack('w1', back());

  assert.equal(outcome, 'not-handled', 'Shell navigation continues rather than stalling on a package.');
  assert.equal(failures.at(-1)?.code, surfaceErrorCodes.timeout);
  assert.equal(scheduler.state('w1'), 'foreground-focused', 'A failed Back does not change execution state.');
});

test('a second Surface for the same window is refused', () => {
  const scheduler = new SurfaceScheduler();
  scheduler.register('w1', new RecordingSurface(), 'suspend', true);

  assert.throws(
    () => scheduler.register('w1', new RecordingSurface(), 'suspend', true),
    SurfaceContractError,
  );
});

test('driving an unregistered window does nothing', async () => {
  const scheduler = new SurfaceScheduler();

  await scheduler.show(context('missing', 'focused'), 'window-opened');
  await scheduler.background('missing', 'window-backgrounded');

  assert.equal(scheduler.state('missing'), null);
});

function context(windowId: string, presentation: 'focused' | 'visible'): SurfaceContext {
  return {
    windowId,
    workspaceClass: 'phone',
    presentation,
    bounds: { x: 0, y: 0, width: 390, height: 780 },
    revision: 1,
  };
}

function back(): { source: 'system'; sequence: number } {
  return { source: 'system', sequence: 1 };
}

/** A package Surface that records what the Shell asked of it. */
class RecordingSurface implements SurfaceHost {
  readonly calls: (keyof SurfaceHost)[] = [];
  readonly aborted: (keyof SurfaceHost)[] = [];
  readonly rejectOn = new Set<keyof SurfaceHost>();
  readonly #release: (() => void)[] = [];
  hold = false;
  holdExcept: Set<keyof SurfaceHost> = new Set();
  ignoreAbort = false;
  backOutcome: BackOutcome = 'not-handled';

  public activate(_context: SurfaceContext, signal: AbortSignal): Promise<void> {
    return this.#call('activate', signal);
  }

  public deactivate(_reason: SurfaceReason, signal: AbortSignal): Promise<void> {
    return this.#call('deactivate', signal);
  }

  public suspend(_reason: SurfaceReason, signal: AbortSignal): Promise<void> {
    return this.#call('suspend', signal);
  }

  public resume(_context: SurfaceContext, signal: AbortSignal): Promise<void> {
    return this.#call('resume', signal);
  }

  public async handleBack(_context: unknown, signal: AbortSignal): Promise<BackOutcome> {
    await this.#call('handleBack', signal);
    return this.backOutcome;
  }

  public dispose(_reason: SurfaceReason, signal: AbortSignal): Promise<void> {
    return this.#call('dispose', signal);
  }

  public releaseAll(): void {
    this.hold = false;
    for (const release of this.#release.splice(0)) {
      release();
    }
  }

  #call(name: keyof SurfaceHost, signal: AbortSignal): Promise<void> {
    this.calls.push(name);
    if (this.rejectOn.has(name)) {
      return Promise.reject(new Error(`${name} failed`));
    }
    if (!this.hold || this.holdExcept.has(name)) {
      return Promise.resolve();
    }

    return new Promise<void>((resolve, reject) => {
      this.#release.push(resolve);
      if (this.ignoreAbort) {
        return;
      }
      signal.addEventListener('abort', () => {
        this.aborted.push(name);
        reject(new Error(`${name} aborted`));
      });
    });
  }
}
