import {
  RealtimeEventService,
  type RealtimeConnection,
  type RealtimeEventEnvelope,
} from './realtime-events.js';

/** Coordinates initial API state, event delivery and authoritative reconnect refresh. */
export class DesktopClientServices {
  readonly #refreshAuthoritativeState: () => void | Promise<void>;
  readonly #realtime: RealtimeEventService;
  #started = false;

  public constructor(
    connection: RealtimeConnection,
    applyEvent: (event: RealtimeEventEnvelope) => void | Promise<void>,
    refreshAuthoritativeState: () => void | Promise<void>,
  ) {
    this.#refreshAuthoritativeState = refreshAuthoritativeState;
    this.#realtime = new RealtimeEventService(
      connection,
      applyEvent,
      refreshAuthoritativeState,
    );
  }

  public async start(): Promise<void> {
    if (this.#started) {
      return;
    }

    await this.#refreshAuthoritativeState();
    await this.#realtime.start();
    this.#started = true;
  }

  public async stop(): Promise<void> {
    if (!this.#started) {
      return;
    }

    await this.#realtime.stop();
    this.#started = false;
  }
}
