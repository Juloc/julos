// MOB-006: the host that drives the Surface lifecycle from docs/MOBILE_PWA.md section 10.
//
// MOB-001 fixed the contract — the states, the reasons, the deadlines and the closed
// transition table. This module is the part that actually calls a package: it serializes
// transitions per Surface, makes a repeated transition a no-op, aborts on the deadline and
// decides what a failure means. It owns no window geometry and no session identity, so
// driving execution can never move a window or end a runtime session.

import {
  backgroundStateFor,
  surfaceDeadlines,
  SurfaceContractError,
  transitionCalls,
  type BackContext,
  type BackOutcome,
  type BackgroundMode,
  type SurfaceContext,
  type SurfaceExecutionState,
  type SurfaceHost,
  type SurfaceReason,
} from './surface-contract.js';

/** What the Shell learns when a Surface misbehaves. */
export interface SurfaceFailure {
  readonly windowId: string;
  readonly code: string;
  readonly call: keyof SurfaceHost | null;
  readonly reason: SurfaceReason | null;
  readonly cause: unknown;
}

export interface SurfaceSchedulerOptions {
  /** Reports a bounded package failure. The Shell keeps running either way. */
  readonly onFailure?: (failure: SurfaceFailure) => void;
  /** Observes every completed state change, for instrumentation and tests. */
  readonly onStateChanged?: (windowId: string, state: SurfaceExecutionState) => void;
  /** Overridable so tests do not wait two real seconds. */
  readonly lifecycleDeadlineMs?: number;
  readonly backDeadlineMs?: number;
}

/** Stable Surface failure codes from `docs/MOBILE_PWA.md` section 16. */
export const surfaceErrorCodes = Object.freeze({
  /** A lifecycle call did not settle inside its deadline. */
  timeout: 'package.surface_timeout',
  /** The Surface was disposed and cannot be driven again. */
  terminated: 'package.surface_terminated',
  /** The declared contract is not one this Shell implements. */
  unsupported: 'package.surface_contract_unsupported',
});

interface SurfaceRecord {
  readonly host: SurfaceHost;
  readonly backgroundMode: BackgroundMode;
  readonly handlesBack: boolean;
  state: SurfaceExecutionState;
  /** Serializes transitions: every request chains onto the previous one. */
  queue: Promise<void>;
}

/**
 * Drives registered Surfaces through the lifecycle.
 *
 * One scheduler serves the whole Shell. A Surface that faults or times out is isolated:
 * the failure is reported, the Shell keeps going, and no other Surface is affected.
 */
export class SurfaceScheduler {
  readonly #surfaces = new Map<string, SurfaceRecord>();
  readonly #onFailure: (failure: SurfaceFailure) => void;
  readonly #onStateChanged: (windowId: string, state: SurfaceExecutionState) => void;
  readonly #lifecycleDeadlineMs: number;
  readonly #backDeadlineMs: number;

  public constructor(options: SurfaceSchedulerOptions = {}) {
    this.#onFailure = options.onFailure ?? (() => undefined);
    this.#onStateChanged = options.onStateChanged ?? (() => undefined);
    this.#lifecycleDeadlineMs = options.lifecycleDeadlineMs ?? surfaceDeadlines.lifecycleMs;
    this.#backDeadlineMs = options.backDeadlineMs ?? surfaceDeadlines.backMs;
  }

  /**
   * Registers a newly created Surface.
   *
   * It starts `suspended`, which is the state a Surface that has never run is in. Nothing
   * is called until the Shell asks for a transition, and section 10 requires `activate` to
   * complete before the Surface receives input.
   */
  public register(
    windowId: string,
    host: SurfaceHost,
    backgroundMode: BackgroundMode,
    handlesBack: boolean,
  ): void {
    if (this.#surfaces.has(windowId)) {
      throw new SurfaceContractError(
        surfaceErrorCodes.unsupported,
        `A Surface is already registered for window '${windowId}'.`,
      );
    }

    this.#surfaces.set(windowId, {
      host,
      backgroundMode,
      handlesBack,
      state: 'suspended',
      queue: Promise.resolve(),
    });
  }

  public state(windowId: string): SurfaceExecutionState | null {
    return this.#surfaces.get(windowId)?.state ?? null;
  }

  public get registeredWindowIds(): readonly string[] {
    return [...this.#surfaces.keys()];
  }

  /** Brings a Surface into the visible foreground, focused or merely visible. */
  public show(context: SurfaceContext, reason: SurfaceReason): Promise<void> {
    const target: SurfaceExecutionState = context.presentation === 'focused'
      ? 'foreground-focused'
      : 'foreground-visible';
    return this.#transition(context.windowId, target, reason, context);
  }

  /**
   * Takes a Surface out of the visible foreground.
   *
   * Where it lands is the resolved background mode, which is the user's stored preference
   * and never something the application asked for.
   */
  public background(windowId: string, reason: SurfaceReason): Promise<void> {
    const record = this.#surfaces.get(windowId);
    if (record === undefined) {
      return Promise.resolve();
    }
    return this.#transition(windowId, backgroundStateFor(record.backgroundMode), reason, null);
  }

  /** Destroys a Surface. Terminal: later calls fail with `package.surface_terminated`. */
  public dispose(windowId: string, reason: SurfaceReason): Promise<void> {
    return this.#transition(windowId, 'terminated', reason, null);
  }

  /**
   * Offers Back to the focused Surface.
   *
   * A Surface that does not declare `HandlesBack`, rejects, or misses the 500 ms deadline
   * is `not-handled`, so Shell navigation continues rather than stalling on a package.
   */
  public async offerBack(windowId: string, context: BackContext): Promise<BackOutcome> {
    const record = this.#surfaces.get(windowId);
    if (record === undefined || !record.handlesBack) {
      return 'not-handled';
    }
    if (record.state !== 'foreground-focused') {
      return 'not-handled';
    }

    try {
      const outcome = await this.#withDeadline(
        (signal) => record.host.handleBack(context, signal),
        this.#backDeadlineMs,
      );
      return outcome === 'handled' ? 'handled' : 'not-handled';
    } catch (cause) {
      this.#report(windowId, failureCode(cause), 'handleBack', null, cause);
      return 'not-handled';
    }
  }

  /** Forgets a window without driving it, for a Shell that is tearing everything down. */
  public forget(windowId: string): void {
    this.#surfaces.delete(windowId);
  }

  #transition(
    windowId: string,
    target: SurfaceExecutionState,
    reason: SurfaceReason,
    context: SurfaceContext | null,
  ): Promise<void> {
    const record = this.#surfaces.get(windowId);
    if (record === undefined) {
      return Promise.resolve();
    }

    // Every request for one Surface chains onto the previous one, so two Shell events in
    // the same tick can never interleave calls into the same package.
    const run = record.queue.then(() => this.#run(windowId, record, target, reason, context));
    record.queue = run.catch(() => undefined);
    return run;
  }

  async #run(
    windowId: string,
    record: SurfaceRecord,
    target: SurfaceExecutionState,
    reason: SurfaceReason,
    context: SurfaceContext | null,
  ): Promise<void> {
    if (record.state === 'terminated') {
      // Disposal is terminal. Reporting rather than throwing keeps a late Shell event from
      // becoming an unhandled rejection in the middle of teardown.
      this.#report(windowId, surfaceErrorCodes.terminated, null, reason, null);
      return;
    }

    if (record.state === target) {
      return;
    }

    let calls: readonly (keyof SurfaceHost)[];
    try {
      calls = transitionCalls(record.state, target);
    } catch (cause) {
      this.#report(windowId, failureCode(cause), null, reason, cause);
      return;
    }

    for (const call of calls) {
      try {
        await this.#invoke(record, call, reason, context);
      } catch (cause) {
        this.#fail(windowId, record, call, reason, cause);
        return;
      }
    }

    this.#settle(windowId, record, target);
  }

  #invoke(
    record: SurfaceRecord,
    call: keyof SurfaceHost,
    reason: SurfaceReason,
    context: SurfaceContext | null,
  ): Promise<void> {
    return this.#withDeadline((signal) => {
      switch (call) {
        case 'activate':
          return record.host.activate(requireContext(context, call), signal);
        case 'resume':
          return record.host.resume(requireContext(context, call), signal);
        case 'deactivate':
          return record.host.deactivate(reason, signal);
        case 'suspend':
          return record.host.suspend(reason, signal);
        case 'dispose':
          return record.host.dispose(reason, signal);
        default:
          throw new SurfaceContractError(
            surfaceErrorCodes.unsupported,
            `'${String(call)}' is not a lifecycle call.`,
          );
      }
    }, this.#lifecycleDeadlineMs);
  }

  /**
   * Decides what a failed call means.
   *
   * A Surface that cannot start is `faulted` and the Shell shows a retryable error. A
   * Surface that cannot stop is torn down: its frontend realm is gone as far as the Shell
   * is concerned, and the failure is recorded. Neither case touches a runtime Session.
   */
  #fail(
    windowId: string,
    record: SurfaceRecord,
    call: keyof SurfaceHost,
    reason: SurfaceReason,
    cause: unknown,
  ): void {
    this.#report(windowId, failureCode(cause), call, reason, cause);
    this.#settle(windowId, record, call === 'activate' || call === 'resume' ? 'faulted' : 'terminated');
  }

  #settle(windowId: string, record: SurfaceRecord, state: SurfaceExecutionState): void {
    record.state = state;
    this.#onStateChanged(windowId, state);
  }

  #report(
    windowId: string,
    code: string,
    call: keyof SurfaceHost | null,
    reason: SurfaceReason | null,
    cause: unknown,
  ): void {
    this.#onFailure({ windowId, code, call, reason, cause });
  }

  /**
   * Runs one call under a deadline.
   *
   * The Shell aborts on the deadline. A package that ignores its `AbortSignal` cannot hold
   * the Shell: the promise this returns rejects on the deadline regardless of whether the
   * package ever settles.
   */
  async #withDeadline<T>(
    call: (signal: AbortSignal) => Promise<T>,
    deadlineMs: number,
  ): Promise<T> {
    const controller = new AbortController();
    let timer: ReturnType<typeof globalThis.setTimeout> | null = null;
    let timedOut = false;

    const deadline = new Promise<never>((_resolve, reject) => {
      timer = globalThis.setTimeout(() => {
        timedOut = true;
        controller.abort();
        reject(this.#timeout(deadlineMs));
      }, deadlineMs);
    });

    try {
      return await Promise.race([call(controller.signal), deadline]);
    } catch (cause) {
      // A package that rejects because the Shell aborted it has still missed the
      // deadline. Reporting whatever it threw would attribute the Shell's own timeout
      // to a contract problem the package does not have.
      throw timedOut ? this.#timeout(deadlineMs) : cause;
    } finally {
      if (timer !== null) {
        globalThis.clearTimeout(timer);
      }
    }
  }

  #timeout(deadlineMs: number): SurfaceContractError {
    return new SurfaceContractError(
      surfaceErrorCodes.timeout,
      `The Surface did not settle within ${deadlineMs} ms.`,
    );
  }
}

function requireContext(context: SurfaceContext | null, call: keyof SurfaceHost): SurfaceContext {
  if (context === null) {
    throw new SurfaceContractError(
      surfaceErrorCodes.unsupported,
      `'${String(call)}' needs the Surface context describing where the Surface is shown.`,
    );
  }
  return context;
}

function failureCode(cause: unknown): string {
  return cause instanceof SurfaceContractError ? cause.code : surfaceErrorCodes.unsupported;
}

/**
 * Reads the Surface host a package element implements.
 *
 * The element itself is the Surface: the Shell looks for the six lifecycle methods on the
 * instance it created. A package that declares the contract in its manifest but does not
 * implement it is a contract error, not something the Shell quietly runs anyway — section
 * 10 is explicit that there is no silent mobile lifecycle fallback.
 */
export function readSurfaceHost(element: unknown): SurfaceHost {
  const candidate = element as Partial<Record<keyof SurfaceHost, unknown>> | null;
  const required: (keyof SurfaceHost)[] = [
    'activate',
    'deactivate',
    'suspend',
    'resume',
    'handleBack',
    'dispose',
  ];
  const missing = candidate === null
    ? required
    : required.filter((name) => typeof candidate[name] !== 'function');

  if (missing.length > 0) {
    throw new SurfaceContractError(
      surfaceErrorCodes.unsupported,
      `The package element does not implement the Surface contract: ${missing.join(', ')}.`,
    );
  }

  return element as SurfaceHost;
}
