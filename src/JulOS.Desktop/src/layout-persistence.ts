import { JulOsApiClient, JulOsApiError } from './api-client.js';
import type { WindowPresentationState, WindowStore } from './window-store.js';

export type DesktopViewport = 'desktop' | 'tablet' | 'mobile';

export interface PersistedDesktopWindow {
  readonly windowId: string;
  readonly applicationDefinitionId: string;
  readonly launchTargetId: string | null;
  readonly state: WindowPresentationState;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
  readonly restoreX: number;
  readonly restoreY: number;
  readonly restoreWidth: number;
  readonly restoreHeight: number;
  readonly zIndex: number;
  readonly sessionReferenceId: string | null;
}

export interface PersistedWidgetPlacement {
  readonly widgetPlacementId: string;
  readonly widgetKey: string;
  readonly gridColumn: number;
  readonly gridRow: number;
  readonly widthUnits: number;
  readonly heightUnits: number;
}

export interface DesktopLayoutDocument {
  readonly layoutId: string;
  readonly viewport: DesktopViewport;
  readonly name: string;
  readonly revision: number;
  readonly updatedAtUtc: string;
  readonly windows: readonly PersistedDesktopWindow[];
  readonly widgets: readonly PersistedWidgetPlacement[];
}

export interface SaveDesktopLayoutDocument {
  readonly revision: number;
  readonly windows: readonly PersistedDesktopWindow[];
  readonly widgets: readonly PersistedWidgetPlacement[];
}

export interface AntiforgeryToken {
  readonly headerName: string;
  readonly token: string;
}

export interface LayoutConflict {
  readonly viewport: DesktopViewport;
  readonly localRevision: number;
  readonly currentRevision: number | null;
  readonly correlationId: string | null;
}

export interface LayoutPersistenceOptions {
  readonly debounceMilliseconds?: number;
  readonly onConflict?: (conflict: LayoutConflict) => void | Promise<void>;
  readonly onFailure?: (error: unknown) => void | Promise<void>;
}

interface PendingSave {
  document: SaveDesktopLayoutDocument;
  timer: ReturnType<typeof globalThis.setTimeout> | null;
  inFlight: Promise<DesktopLayoutDocument> | null;
}

/**
 * Persists independent viewport documents. Pointer movement never calls this service;
 * callers schedule only settled window and widget state.
 */
export class DesktopLayoutPersistence {
  readonly #api: JulOsApiClient;
  readonly #debounceMilliseconds: number;
  readonly #onConflict: (conflict: LayoutConflict) => void | Promise<void>;
  readonly #onFailure: (error: unknown) => void | Promise<void>;
  readonly #pending = new Map<DesktopViewport, PendingSave>();
  readonly #revisions = new Map<DesktopViewport, number>();
  #antiforgery: AntiforgeryToken | null = null;

  public constructor(
    fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis),
    options: LayoutPersistenceOptions = {},
  ) {
    this.#api = new JulOsApiClient(fetchImplementation);
    this.#debounceMilliseconds = options.debounceMilliseconds ?? 650;
    this.#onConflict = options.onConflict ?? (() => undefined);
    this.#onFailure = options.onFailure ?? (() => undefined);
    if (!Number.isFinite(this.#debounceMilliseconds) || this.#debounceMilliseconds < 0) {
      throw new RangeError('Layout persistence debounce must be non-negative.');
    }
  }

  public async load(viewport: DesktopViewport): Promise<DesktopLayoutDocument> {
    const layout = await this.#api.get<DesktopLayoutDocument>(layoutPath(viewport));
    this.#revisions.set(viewport, layout.revision);
    return layout;
  }

  public schedule(
    viewport: DesktopViewport,
    windows: readonly PersistedDesktopWindow[],
    widgets: readonly PersistedWidgetPlacement[],
  ): void {
    const revision = this.#revisions.get(viewport) ?? 0;
    const pending = this.#pending.get(viewport) ?? {
      document: { revision, windows: [], widgets: [] },
      timer: null,
      inFlight: null,
    };
    pending.document = { revision, windows: cloneWindows(windows), widgets: cloneWidgets(widgets) };
    if (pending.timer !== null) {
      globalThis.clearTimeout(pending.timer);
    }
    pending.timer = globalThis.setTimeout(() => {
      pending.timer = null;
      void this.#flush(viewport, pending);
    }, this.#debounceMilliseconds);
    this.#pending.set(viewport, pending);
  }

  public async flush(viewport?: DesktopViewport): Promise<void> {
    if (viewport !== undefined) {
      const pending = this.#pending.get(viewport);
      if (pending !== undefined) {
        if (pending.timer !== null) {
          globalThis.clearTimeout(pending.timer);
          pending.timer = null;
        }
        await this.#flush(viewport, pending);
      }
      return;
    }

    await Promise.all([...this.#pending.entries()].map(async ([key, pending]) => {
      if (pending.timer !== null) {
        globalThis.clearTimeout(pending.timer);
        pending.timer = null;
      }
      await this.#flush(key, pending);
    }));
  }

  public cancel(viewport?: DesktopViewport): void {
    const targets = viewport === undefined
      ? [...this.#pending.values()]
      : [this.#pending.get(viewport)].filter((value): value is PendingSave => value !== undefined);
    for (const pending of targets) {
      if (pending.timer !== null) {
        globalThis.clearTimeout(pending.timer);
        pending.timer = null;
      }
    }
    if (viewport === undefined) {
      this.#pending.clear();
    } else {
      this.#pending.delete(viewport);
    }
  }

  async #flush(viewport: DesktopViewport, pending: PendingSave): Promise<DesktopLayoutDocument> {
    if (pending.inFlight !== null) {
      await pending.inFlight;
    }

    const token = await this.#readAntiforgery();
    const document = {
      ...pending.document,
      revision: this.#revisions.get(viewport) ?? pending.document.revision,
    };
    const save = this.#api.requestJson<DesktopLayoutDocument>(layoutPath(viewport), {
      method: 'PUT',
      body: document,
      headers: { [token.headerName]: token.token },
    });
    pending.inFlight = save;

    try {
      const stored = await save;
      this.#revisions.set(viewport, stored.revision);
      pending.document = { ...pending.document, revision: stored.revision };
      return stored;
    } catch (error) {
      if (error instanceof JulOsApiError && error.status === 409) {
        await this.#onConflict({
          viewport,
          localRevision: document.revision,
          currentRevision: error.problem?.currentRevision ?? null,
          correlationId: error.correlationId,
        });
      } else {
        await this.#onFailure(error);
      }
      throw error;
    } finally {
      pending.inFlight = null;
    }
  }

  async #readAntiforgery(): Promise<AntiforgeryToken> {
    if (this.#antiforgery === null) {
      this.#antiforgery = await this.#api.get<AntiforgeryToken>('/api/v1/auth/antiforgery');
    }
    return this.#antiforgery;
  }
}

export function windowsForPersistence(store: WindowStore): readonly PersistedDesktopWindow[] {
  return store.windows.map((window) => ({
    windowId: window.id,
    applicationDefinitionId: window.applicationId,
    launchTargetId: window.launchTargetId,
    state: window.state,
    x: Math.round(window.bounds.x),
    y: Math.round(window.bounds.y),
    width: Math.round(window.bounds.width),
    height: Math.round(window.bounds.height),
    restoreX: Math.round(window.restoreBounds.x),
    restoreY: Math.round(window.restoreBounds.y),
    restoreWidth: Math.round(window.restoreBounds.width),
    restoreHeight: Math.round(window.restoreBounds.height),
    zIndex: window.zIndex,
    sessionReferenceId: null,
  }));
}

function layoutPath(viewport: DesktopViewport): string {
  return `/api/v1/desktop/layouts/${viewport}`;
}

function cloneWindows(windows: readonly PersistedDesktopWindow[]): readonly PersistedDesktopWindow[] {
  return windows.map((window) => ({ ...window }));
}

function cloneWidgets(widgets: readonly PersistedWidgetPlacement[]): readonly PersistedWidgetPlacement[] {
  return widgets.map((widget) => ({ ...widget }));
}
