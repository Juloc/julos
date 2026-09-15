// MOB-002: the page half of the service-worker update handshake from
// docs/MOBILE_PWA.md section 14.
//
// The rule the whole design exists for: activation never forces a reload. A page reloads
// only after its own layout is clean or successfully flushed, or after the user explicitly
// discards that one page's pending changes. A conflict or an offline flush leaves the page
// running and never blocks another client.

import {
  mayReloadImmediately,
  updateMessages,
  type UpdateLayoutState,
} from './pwa-cache-policy.js';

/** What the Shell can do when an update is waiting. */
export type UpdateDecision = 'reload' | 'awaiting-user' | 'blocked';

/** Reports the current page's layout state and flushes it on request. */
export interface LayoutFlushPort {
  /** Layout state of this page right now. */
  readonly state: () => UpdateLayoutState;
  /**
   * Flushes a dirty layout with its expected revision.
   *
   * Resolves `true` when the layout reached the server, `false` on a `409` revision
   * conflict or while offline. It never throws for those two expected outcomes.
   */
  readonly flush: () => Promise<boolean>;
}

/** What the update surface needs to render, in the Shell's current language. */
export interface UpdateAvailability {
  readonly buildId: string;
  readonly layoutState: UpdateLayoutState;
  readonly decision: UpdateDecision;
}

/** Minimal view of the worker container this module needs. */
export interface ServiceWorkerMessaging {
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
  readonly controller: { postMessage(message: unknown): void } | null;
}

export interface PwaUpdateOptions {
  readonly messaging: ServiceWorkerMessaging;
  readonly layout: LayoutFlushPort;
  /** Called whenever an update becomes available or its decision changes. */
  readonly onAvailable: (availability: UpdateAvailability) => void;
  /** Reloads this page. Injected so tests never navigate. */
  readonly reload: () => void;
  /** Stable identifier of this client, echoed back to the worker. */
  readonly clientId: string;
}

/**
 * Drives the handshake for one page.
 *
 * The controller is deliberately passive until the user accepts: receiving
 * `JULOS_UPDATE_READY` only reports the state, it never activates and never reloads.
 */
export class PwaUpdateController {
  readonly #options: PwaUpdateOptions;
  #pendingBuildId: string | null = null;

  public constructor(options: PwaUpdateOptions) {
    this.#options = options;
    options.messaging.addEventListener('message', (event) => this.#onMessage(event));
  }

  /** The build waiting to be activated, if any. */
  public get pendingBuildId(): string | null {
    return this.#pendingBuildId;
  }

  /**
   * Accepts the waiting update for this page.
   *
   * `discardLocalChanges` is the explicit "Reload without saving" confirmation. It is
   * scoped to this page only: it discards this page's pending presentation changes and
   * can never approve another client.
   */
  public async accept(discardLocalChanges = false): Promise<UpdateDecision> {
    const buildId = this.#pendingBuildId;
    if (buildId === null) {
      return 'blocked';
    }

    const state = this.#options.layout.state();

    if (!mayReloadImmediately(state) && !discardLocalChanges) {
      const flushed = await this.#options.layout.flush();
      if (!flushed) {
        // Offline or a revision conflict: this page keeps running and shows Retry or
        // Resolve. No loop repeatedly reloads it and no other client is blocked.
        this.#report(buildId, state, 'blocked');
        return 'blocked';
      }
    }

    this.#options.messaging.controller?.postMessage({
      type: updateMessages.activateUpdate,
      buildId,
    });
    this.#options.messaging.controller?.postMessage({
      type: updateMessages.clientReadyToReload,
      buildId,
      clientId: this.#options.clientId,
    });

    this.#options.reload();
    return 'reload';
  }

  #onMessage(event: MessageEvent): void {
    const data = event.data as { type?: unknown; buildId?: unknown } | null;
    if (data === null || typeof data !== 'object' || data.type !== updateMessages.updateReady) {
      return;
    }
    if (typeof data.buildId !== 'string' || data.buildId.length === 0) {
      return;
    }

    this.#pendingBuildId = data.buildId;
    const state = this.#options.layout.state();

    this.#options.messaging.controller?.postMessage({
      type: updateMessages.updateStatus,
      buildId: data.buildId,
      clientId: this.#options.clientId,
      layoutState: state,
    });

    this.#report(data.buildId, state, mayReloadImmediately(state) ? 'reload' : 'awaiting-user');
  }

  #report(buildId: string, layoutState: UpdateLayoutState, decision: UpdateDecision): void {
    this.#options.onAvailable({ buildId, layoutState, decision });
  }
}
