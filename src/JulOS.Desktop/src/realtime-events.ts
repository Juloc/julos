/** The current small JulOS real-time event envelope. */
export interface RealtimeEventEnvelope {
  readonly eventId: string;
  readonly eventType: string;
  readonly contractVersion: number;
  readonly occurredAtUtc: string;
  readonly correlationId: string;
  readonly resourceId: string;
  readonly revision: number | null;
  readonly payload: unknown;
}

/** Transport boundary used by the event-state service and its tests. */
export interface RealtimeConnection {
  setEventHandler(handler: (event: RealtimeEventEnvelope) => void | Promise<void>): void;
  setReconnectedHandler(handler: () => void | Promise<void>): void;
  start(): Promise<void>;
  stop(): Promise<void>;
}

/**
 * Deduplicates at-least-once notifications and forces an authoritative API refresh after reconnect.
 */
export class RealtimeEventService {
  readonly #connection: RealtimeConnection;
  readonly #onEvent: (event: RealtimeEventEnvelope) => void | Promise<void>;
  readonly #refreshAuthoritativeState: () => void | Promise<void>;
  readonly #seenEventIds = new Set<string>();
  readonly #seenOrder: string[] = [];
  readonly #deduplicationCapacity: number;

  public constructor(
    connection: RealtimeConnection,
    onEvent: (event: RealtimeEventEnvelope) => void | Promise<void>,
    refreshAuthoritativeState: () => void | Promise<void>,
    deduplicationCapacity = 2048,
  ) {
    if (!Number.isInteger(deduplicationCapacity) || deduplicationCapacity < 1) {
      throw new RangeError('The real-time event deduplication capacity must be positive.');
    }

    this.#connection = connection;
    this.#onEvent = onEvent;
    this.#refreshAuthoritativeState = refreshAuthoritativeState;
    this.#deduplicationCapacity = deduplicationCapacity;

    connection.setEventHandler(async (event) => this.#accept(event));
    connection.setReconnectedHandler(async () => this.#refreshAuthoritativeState());
  }

  public start(): Promise<void> {
    return this.#connection.start();
  }

  public stop(): Promise<void> {
    return this.#connection.stop();
  }

  async #accept(event: RealtimeEventEnvelope): Promise<void> {
    if (event.contractVersion !== 1 || this.#seenEventIds.has(event.eventId)) {
      return;
    }

    this.#seenEventIds.add(event.eventId);
    this.#seenOrder.push(event.eventId);

    if (this.#seenOrder.length > this.#deduplicationCapacity) {
      const expired = this.#seenOrder.shift();
      if (expired !== undefined) {
        this.#seenEventIds.delete(expired);
      }
    }

    await this.#onEvent(event);
  }
}

interface NegotiationResponse {
  readonly connectionToken: string;
  readonly availableTransports: readonly {
    readonly transport: string;
    readonly transferFormats: readonly string[];
  }[];
}

interface SignalRInvocation {
  readonly type: number;
  readonly target?: string;
  readonly arguments?: readonly unknown[];
}

interface RealtimeSocket {
  readonly readyState: number;
  addEventListener(type: 'open', listener: () => void): void;
  addEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void;
  addEventListener(type: 'close', listener: () => void): void;
  addEventListener(type: 'error', listener: () => void): void;
  send(data: string): void;
  close(code?: number, reason?: string): void;
}

export type RealtimeSocketFactory = (url: string) => RealtimeSocket;
export type RealtimeFetch = typeof fetch;
export type ReconnectDelay = (milliseconds: number) => Promise<void>;

const recordSeparator = '\u001e';
const openSocketState = 1;
const reconnectDelays = [0, 2000, 10_000, 30_000] as const;

/** Splits one or more SignalR JSON protocol records without retaining separators. */
export function splitSignalRFrames(message: string): string[] {
  return message
    .split(recordSeparator)
    .map((frame) => frame.trim())
    .filter((frame) => frame.length > 0);
}

/**
 * Minimal same-origin SignalR JSON/WebSocket transport. It intentionally exposes no token to callers.
 */
export class SignalRJsonConnection implements RealtimeConnection {
  readonly #hubPath: string;
  readonly #fetch: RealtimeFetch;
  readonly #socketFactory: RealtimeSocketFactory;
  readonly #delay: ReconnectDelay;

  #eventHandler: (event: RealtimeEventEnvelope) => void | Promise<void> = () => undefined;
  #reconnectedHandler: () => void | Promise<void> = () => undefined;
  #socket: RealtimeSocket | null = null;
  #stopped = true;
  #connectionAttempt: Promise<void> | null = null;

  public constructor(
    hubPath = '/hubs/events',
    fetchImplementation: RealtimeFetch = globalThis.fetch.bind(globalThis),
    socketFactory: RealtimeSocketFactory = (url) => new WebSocket(url),
    delay: ReconnectDelay = async (milliseconds) =>
      new Promise((resolve) => globalThis.setTimeout(resolve, milliseconds)),
  ) {
    if (!hubPath.startsWith('/')) {
      throw new TypeError('The real-time hub path must be same-origin and absolute.');
    }

    this.#hubPath = hubPath;
    this.#fetch = fetchImplementation;
    this.#socketFactory = socketFactory;
    this.#delay = delay;
  }

  public setEventHandler(handler: (event: RealtimeEventEnvelope) => void | Promise<void>): void {
    this.#eventHandler = handler;
  }

  public setReconnectedHandler(handler: () => void | Promise<void>): void {
    this.#reconnectedHandler = handler;
  }

  public async start(): Promise<void> {
    if (!this.#stopped) {
      return this.#connectionAttempt ?? Promise.resolve();
    }

    this.#stopped = false;
    this.#connectionAttempt = this.#connect(isReconnect: false);
    try {
      await this.#connectionAttempt;
    } finally {
      this.#connectionAttempt = null;
    }
  }

  public async stop(): Promise<void> {
    this.#stopped = true;
    const socket = this.#socket;
    this.#socket = null;
    if (socket !== null && socket.readyState <= openSocketState) {
      socket.close(1000, 'Client stopped.');
    }
  }

  async #connect(isReconnect: boolean): Promise<void> {
    const negotiation = await this.#negotiate();
    const socketUrl = this.#buildSocketUrl(negotiation.connectionToken);
    const socket = this.#socketFactory(socketUrl);
    this.#socket = socket;

    await new Promise<void>((resolve, reject) => {
      let handshakeComplete = false;
      let settled = false;

      const fail = (error: Error): void => {
        if (!settled) {
          settled = true;
          reject(error);
        }
      };

      socket.addEventListener('open', () => {
        socket.send(`${JSON.stringify({ protocol: 'json', version: 1 })}${recordSeparator}`);
      });

      socket.addEventListener('message', (message) => {
        if (typeof message.data !== 'string') {
          return;
        }

        for (const frame of splitSignalRFrames(message.data)) {
          if (!handshakeComplete) {
            const handshake = JSON.parse(frame) as { readonly error?: unknown };
            if (typeof handshake.error === 'string') {
              fail(new Error('The real-time hub rejected the protocol handshake.'));
              return;
            }

            handshakeComplete = true;
            if (!settled) {
              settled = true;
              resolve();
            }
            continue;
          }

          this.#handleProtocolFrame(frame);
        }
      });

      socket.addEventListener('error', () => fail(new Error('The real-time socket failed.')));
      socket.addEventListener('close', () => {
        if (!handshakeComplete) {
          fail(new Error('The real-time socket closed before the protocol handshake completed.'));
        }

        if (!this.#stopped && this.#socket === socket) {
          this.#socket = null;
          void this.#reconnect();
        }
      });
    });

    if (isReconnect) {
      await this.#reconnectedHandler();
    }
  }

  async #reconnect(): Promise<void> {
    if (this.#connectionAttempt !== null || this.#stopped) {
      return;
    }

    this.#connectionAttempt = (async () => {
      let attempt = 0;
      while (!this.#stopped) {
        const delay = reconnectDelays[Math.min(attempt, reconnectDelays.length - 1)];
        await this.#delay(delay);
        if (this.#stopped) {
          return;
        }

        try {
          await this.#connect(isReconnect: true);
          return;
        } catch {
          attempt += 1;
        }
      }
    })();

    try {
      await this.#connectionAttempt;
    } finally {
      this.#connectionAttempt = null;
    }
  }

  async #negotiate(): Promise<NegotiationResponse> {
    const response = await this.#fetch(`${this.#hubPath}/negotiate?negotiateVersion=1`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-Requested-With': 'XMLHttpRequest' },
    });

    if (!response.ok) {
      throw new Error(`The real-time hub negotiation failed with status ${response.status}.`);
    }

    const value = (await response.json()) as Partial<NegotiationResponse>;
    if (
      typeof value.connectionToken !== 'string' ||
      !Array.isArray(value.availableTransports) ||
      !value.availableTransports.some(
        (transport) =>
          transport.transport === 'WebSockets' && transport.transferFormats.includes('Text'),
      )
    ) {
      throw new Error('The real-time hub did not offer the required WebSocket text transport.');
    }

    return value as NegotiationResponse;
  }

  #buildSocketUrl(connectionToken: string): string {
    const url = new URL(this.#hubPath, globalThis.location.origin);
    url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
    url.searchParams.set('id', connectionToken);
    return url.toString();
  }

  #handleProtocolFrame(frame: string): void {
    const message = JSON.parse(frame) as SignalRInvocation;
    if (message.type !== 1 || message.target !== 'event' || message.arguments?.length !== 1) {
      return;
    }

    const event = message.arguments[0];
    if (isRealtimeEventEnvelope(event)) {
      void this.#eventHandler(event);
    }
  }
}

function isRealtimeEventEnvelope(value: unknown): value is RealtimeEventEnvelope {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<RealtimeEventEnvelope>;
  return (
    typeof candidate.eventId === 'string' &&
    typeof candidate.eventType === 'string' &&
    typeof candidate.contractVersion === 'number' &&
    typeof candidate.occurredAtUtc === 'string' &&
    typeof candidate.correlationId === 'string' &&
    typeof candidate.resourceId === 'string' &&
    (candidate.revision === null || typeof candidate.revision === 'number') &&
    'payload' in candidate
  );
}
