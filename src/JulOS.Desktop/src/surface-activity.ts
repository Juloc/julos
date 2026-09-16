// MOB-006: instrumentation that shows whether a suspended Surface actually stopped.
//
// `suspend()` is a contract the package fulfils: only the package can stop its own timers,
// polling and display connections. The Shell cannot reach inside a package realm and stop
// them for it, and pretending otherwise would be a placeholder.
//
// What the Shell can do is watch the observable consequence. A Surface that is still
// rendering is still working, and rendering is a DOM mutation inside its own subtree. This
// monitor watches exactly that, so "the suspended test app has no rendering activity" is a
// measurement rather than an assumption, and a package that keeps working after suspend
// produces a bounded, attributable failure instead of silently draining the device.

import { surfaceErrorCodes } from './surface-scheduler.js';

/** One observation of a Surface working while it was supposed to be stopped. */
export interface SurfaceActivityViolation {
  readonly windowId: string;
  readonly code: string;
  /** How many mutations were seen in the window that produced this violation. */
  readonly mutations: number;
}

/** The subset of `MutationObserver` this monitor needs, so tests can supply their own. */
export interface MutationObserverLike {
  observe(target: Node, options: MutationObserverInit): void;
  disconnect(): void;
}

export type MutationObserverFactory = (
  callback: (mutations: readonly MutationRecord[]) => void,
) => MutationObserverLike;

export interface SurfaceActivityOptions {
  readonly onViolation?: (violation: SurfaceActivityViolation) => void;
  readonly createObserver?: MutationObserverFactory;
}

interface Watch {
  readonly observer: MutationObserverLike;
  mutations: number;
  reported: boolean;
}

/**
 * Watches suspended Surfaces for rendering activity.
 *
 * Only suspended Surfaces are watched. A `background-active` Surface is deliberately still
 * running, because its owner chose "keep active in background", so mutations there are
 * expected and are not observed at all.
 */
export class SurfaceActivityMonitor {
  readonly #watches = new Map<string, Watch>();
  readonly #onViolation: (violation: SurfaceActivityViolation) => void;
  readonly #createObserver: MutationObserverFactory;

  public constructor(options: SurfaceActivityOptions = {}) {
    this.#onViolation = options.onViolation ?? (() => undefined);
    this.#createObserver = options.createObserver
      ?? ((callback) => new globalThis.MutationObserver((mutations) => callback(mutations)));
  }

  /** Begins watching a Surface that has just been suspended. */
  public watch(windowId: string, element: Node): void {
    this.release(windowId);

    const watch: Watch = {
      observer: this.#createObserver((mutations) => this.#record(windowId, mutations.length)),
      mutations: 0,
      reported: false,
    };
    watch.observer.observe(element, {
      childList: true,
      subtree: true,
      attributes: true,
      characterData: true,
    });
    this.#watches.set(windowId, watch);
  }

  /** Stops watching, because the Surface is legitimately running again or is gone. */
  public release(windowId: string): void {
    const watch = this.#watches.get(windowId);
    if (watch === undefined) {
      return;
    }
    watch.observer.disconnect();
    this.#watches.delete(windowId);
  }

  public releaseAll(): void {
    for (const windowId of [...this.#watches.keys()]) {
      this.release(windowId);
    }
  }

  /** How much activity a watched Surface has produced since it was suspended. */
  public mutationsSinceSuspend(windowId: string): number {
    return this.#watches.get(windowId)?.mutations ?? 0;
  }

  /** Whether a Surface has been quiet since it was suspended. */
  public isQuiet(windowId: string): boolean {
    return this.mutationsSinceSuspend(windowId) === 0;
  }

  #record(windowId: string, mutations: number): void {
    const watch = this.#watches.get(windowId);
    if (watch === undefined) {
      return;
    }

    watch.mutations += mutations;
    if (watch.reported) {
      // One bounded failure per suspension. A package that renders in a loop must not turn
      // into a loop of failure reports.
      return;
    }

    watch.reported = true;
    this.#onViolation({
      windowId,
      code: surfaceErrorCodes.timeout,
      mutations: watch.mutations,
    });
  }
}
