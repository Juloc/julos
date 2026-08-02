import type { DesktopWindowSnapshot } from './window-store.js';

export type ViewportClass = 'desktop' | 'tablet' | 'mobile';

export interface StoredDesktopLayout {
  readonly viewportClass: ViewportClass;
  readonly windows: readonly DesktopWindowSnapshot[];
  readonly revision: number;
}

export interface SaveDesktopLayoutRequest {
  readonly viewportClass: ViewportClass;
  readonly windows: readonly DesktopWindowSnapshot[];
  readonly expectedRevision: number;
}

export interface DesktopLayoutApi {
  read(viewportClass: ViewportClass): Promise<StoredDesktopLayout | null>;
  save(request: SaveDesktopLayoutRequest): Promise<StoredDesktopLayout>;
}

export class LayoutRevisionConflictError extends Error {
  public constructor(public readonly currentRevision: number) {
    super(`The desktop layout is already at revision ${currentRevision}.`);
    this.name = 'LayoutRevisionConflictError';
  }
}

export type LayoutConflictHandler = (
  conflict: LayoutRevisionConflictError,
  attempted: SaveDesktopLayoutRequest,
) => void | Promise<void>;

interface PendingSave {
  readonly viewportClass: ViewportClass;
  readonly windows: readonly DesktopWindowSnapshot[];
  readonly expectedRevision: number;
}

export class DebouncedLayoutPersistence {
  readonly #api: DesktopLayoutApi;
  readonly #delayMilliseconds: number;
  readonly #onConflict: LayoutConflictHandler;
  #timer: ReturnType<typeof setTimeout> | null = null;
  #pending: PendingSave | null = null;
  #inFlight: Promise<StoredDesktopLayout | null> | null = null;

  public constructor(
    api: DesktopLayoutApi,
    delayMilliseconds = 500,
    onConflict: LayoutConflictHandler = () => undefined,
  ) {
    if (!Number.isInteger(delayMilliseconds) || delayMilliseconds < 0 || delayMilliseconds > 60_000) {
      throw new RangeError('Layout persistence delay must be between 0 and 60000 milliseconds.');
    }

    this.#api = api;
    this.#delayMilliseconds = delayMilliseconds;
    this.#onConflict = onConflict;
  }

  public restore(viewportClass: ViewportClass): Promise<StoredDesktopLayout | null> {
    return this.#api.read(viewportClass);
  }

  public schedule(
    viewportClass: ViewportClass,
    windows: readonly DesktopWindowSnapshot[],
    expectedRevision: number,
  ): void {
    validateRevision(expectedRevision);
    this.#pending = {
      viewportClass,
      windows: cloneWindows(windows),
      expectedRevision,
    };

    if (this.#timer !== null) {
      clearTimeout(this.#timer);
    }

    this.#timer = setTimeout(() => {
      this.#timer = null;
      void this.flush();
    }, this.#delayMilliseconds);
  }

  public async flush(): Promise<StoredDesktopLayout | null> {
    if (this.#timer !== null) {
      clearTimeout(this.#timer);
      this.#timer = null;
    }

    if (this.#pending === null) {
      return this.#inFlight === null ? null : this.#inFlight;
    }

    const pending = this.#pending;
    this.#pending = null;
    const request: SaveDesktopLayoutRequest = {
      viewportClass: pending.viewportClass,
      windows: cloneWindows(pending.windows),
      expectedRevision: pending.expectedRevision,
    };

    const save = this.#save(request);
    this.#inFlight = save;
    try {
      return await save;
    } finally {
      if (this.#inFlight === save) {
        this.#inFlight = null;
      }
    }
  }

  public dispose(): void {
    if (this.#timer !== null) {
      clearTimeout(this.#timer);
      this.#timer = null;
    }
    this.#pending = null;
  }

  async #save(request: SaveDesktopLayoutRequest): Promise<StoredDesktopLayout | null> {
    try {
      return await this.#api.save(request);
    } catch (error) {
      if (error instanceof LayoutRevisionConflictError) {
        await this.#onConflict(error, request);
        return null;
      }
      throw error;
    }
  }
}

function validateRevision(revision: number): void {
  if (!Number.isInteger(revision) || revision < 0) {
    throw new RangeError('Layout revision must be a non-negative integer.');
  }
}

function cloneWindows(windows: readonly DesktopWindowSnapshot[]): readonly DesktopWindowSnapshot[] {
  return windows.map((window) => ({
    ...window,
    bounds: { ...window.bounds },
    restoreBounds: { ...window.restoreBounds },
  }));
}
