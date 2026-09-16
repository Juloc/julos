// MOB-008: the Desktop Operation Center from docs/MOBILE_PWA.md section 12.
//
// Operations are durable server-side work. They exist independently of the window that
// started them: closing, suspending or reloading a surface does not cancel anything, and
// cancellation is a separate permission-checked action rather than a side effect of the UI
// going away. This module is the state model for showing that work; it owns no window.

import { JulOsApiClient, JulOsApiError } from './api-client.js';

/** The five public operation states. */
export type OperationState = 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled';

/** One durable operation as its owner sees it. */
export interface OperationView {
  readonly operationId: string;
  readonly operationType: string;
  readonly sourcePackageId: string | null;
  readonly targetReference: string;
  readonly state: OperationState;
  readonly progressPercent: number | null;
  readonly currentStep: string | null;
  readonly createdAtUtc: string;
  readonly startedAtUtc: string | null;
  readonly completedAtUtc: string | null;
  readonly failureCode: string | null;
  readonly failureDetail: string | null;
  readonly correlationId: string;
  readonly cancellationRequested: boolean;
  readonly revision: number;
}

interface OperationPageResponse {
  readonly items: readonly OperationView[];
  readonly nextCursor: string | null;
}

/** What the Operation Center is currently showing. */
export interface OperationCenterSnapshot {
  readonly operations: readonly OperationView[];
  readonly loading: boolean;
  readonly hasMore: boolean;
  readonly lastError: string | null;
  readonly filter: OperationFilter;
}

/** Which of the user's own operations are shown. */
export interface OperationFilter {
  readonly states: readonly OperationState[];
  readonly sourcePackageId: string | null;
}

export type OperationCenterListener = (snapshot: OperationCenterSnapshot) => void;

interface AntiforgeryToken {
  readonly headerName: string;
  readonly token: string;
}

/** Operations that are still going, which is what the Center leads with. */
export const activeOperationStates: readonly OperationState[] = Object.freeze(['queued', 'running']);

/** Whether an operation has reached a state it will not leave. */
export function isSettled(operation: OperationView): boolean {
  return operation.state === 'succeeded'
    || operation.state === 'failed'
    || operation.state === 'cancelled';
}

/**
 * Whether cancelling an operation is still meaningful.
 *
 * A settled operation cannot be cancelled, and asking twice is not an error worth showing:
 * the request is already stored and the executor acknowledges it when it can.
 */
export function isCancellable(operation: OperationView): boolean {
  return !isSettled(operation) && !operation.cancellationRequested;
}

/** State model for the Operation Center application. */
export class OperationCenterStore {
  readonly #api: JulOsApiClient;
  readonly #listeners = new Set<OperationCenterListener>();
  #operations: OperationView[] = [];
  #cursor: string | null = null;
  #loading = false;
  #lastError: string | null = null;
  #filter: OperationFilter = { states: [], sourcePackageId: null };
  #antiforgery: AntiforgeryToken | null = null;

  public constructor(fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.#api = new JulOsApiClient(fetchImplementation);
  }

  public subscribe(listener: OperationCenterListener): () => void {
    this.#listeners.add(listener);
    listener(this.snapshot());
    return () => this.#listeners.delete(listener);
  }

  public snapshot(): OperationCenterSnapshot {
    return {
      operations: [...this.#operations],
      loading: this.#loading,
      hasMore: this.#cursor !== null,
      lastError: this.#lastError,
      filter: { ...this.#filter, states: [...this.#filter.states] },
    };
  }

  /** Replaces the list with the first page of the current filter. */
  public async refresh(): Promise<void> {
    await this.#run(async () => {
      const page = await this.#read(null);
      this.#operations = [...page.items];
      this.#cursor = page.nextCursor;
    });
  }

  /** Narrows the list and reloads it from the first page. */
  public async setFilter(filter: OperationFilter): Promise<void> {
    // The cursor binds the filter set, so changing the filter discards it rather than
    // continuing a page that belonged to a different question.
    this.#filter = { states: [...filter.states], sourcePackageId: filter.sourcePackageId };
    this.#cursor = null;
    await this.refresh();
  }

  /** Appends the next page, if there is one. */
  public async loadMore(): Promise<void> {
    if (this.#cursor === null) {
      return;
    }

    await this.#run(async () => {
      const page = await this.#read(this.#cursor);
      const known = new Set(this.#operations.map((operation) => operation.operationId));
      this.#operations = [
        ...this.#operations,
        ...page.items.filter((operation) => !known.has(operation.operationId)),
      ];
      this.#cursor = page.nextCursor;
    });
  }

  /**
   * Reacts to `operation.changed`.
   *
   * The event carries identity and revision only, so the Center refetches the operation and
   * shows authoritative state. Events are at least once: an operation already at that
   * revision is left alone, so a repeated event cannot duplicate or reorder anything.
   */
  public async applyChange(operationId: string, revision: number | null): Promise<void> {
    const known = this.#operations.find((operation) => operation.operationId === operationId);
    if (known !== undefined && revision !== null && known.revision >= revision) {
      return;
    }

    try {
      const current = await this.#api.get<OperationView>(
        `/api/v1/operations/${encodeURIComponent(operationId)}`,
      );
      this.#merge(current);
      this.#publish();
    } catch (error) {
      if (error instanceof JulOsApiError && error.status === 404) {
        // An operation that is no longer visible to this user simply leaves the list.
        this.#operations = this.#operations.filter(
          (operation) => operation.operationId !== operationId,
        );
        this.#publish();
        return;
      }
      throw error;
    }
  }

  /** Requests cancellation. Separate action, separate permission, never a UI side effect. */
  public async cancel(operationId: string): Promise<void> {
    await this.#run(async () => {
      const antiforgery = await this.#readAntiforgery();
      const cancelled = await this.#api.requestJson<OperationView>(
        `/api/v1/operations/${encodeURIComponent(operationId)}/cancellation`,
        {
          method: 'POST',
          body: {},
          headers: { [antiforgery.headerName]: antiforgery.token },
        },
      );
      this.#merge(cancelled);
    });
  }

  #merge(operation: OperationView): void {
    const index = this.#operations.findIndex(
      (candidate) => candidate.operationId === operation.operationId,
    );
    if (index < 0) {
      // A newly visible operation belongs at the front: the list is newest first.
      this.#operations = [operation, ...this.#operations];
      return;
    }

    const known = this.#operations[index];
    if (known !== undefined && known.revision > operation.revision) {
      return;
    }
    this.#operations = this.#operations.map(
      (candidate, position) => (position === index ? operation : candidate),
    );
  }

  #read(cursor: string | null): Promise<OperationPageResponse> {
    const query = new URLSearchParams();
    if (this.#filter.states.length > 0) {
      query.set('states', this.#filter.states.join(','));
    }
    if (this.#filter.sourcePackageId !== null) {
      query.set('sourcePackageId', this.#filter.sourcePackageId);
    }
    if (cursor !== null) {
      query.set('cursor', cursor);
    }

    const suffix = query.toString();
    return this.#api.get<OperationPageResponse>(
      suffix.length === 0 ? '/api/v1/operations' : `/api/v1/operations?${suffix}`,
    );
  }

  async #readAntiforgery(): Promise<AntiforgeryToken> {
    this.#antiforgery ??= await this.#api.get<AntiforgeryToken>('/api/v1/auth/antiforgery');
    return this.#antiforgery;
  }

  async #run(action: () => Promise<void>): Promise<void> {
    this.#loading = true;
    this.#lastError = null;
    this.#publish();
    try {
      await action();
    } catch (error) {
      this.#lastError = error instanceof Error && error.message.trim().length > 0
        ? error.message
        : 'The operation request failed.';
      throw error;
    } finally {
      this.#loading = false;
      this.#publish();
    }
  }

  #publish(): void {
    const snapshot = this.snapshot();
    for (const listener of this.#listeners) {
      listener(snapshot);
    }
  }
}
