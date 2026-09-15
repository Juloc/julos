import { JulOsApiClient, JulOsApiError } from './api-client.js';
import { desktopMultiDisplayWorkspace } from './multi-display-workspace.js';
import type { LayoutScope, RestoreMode, WorkspaceClass } from './workspace-contract.js';
import type { WindowPresentationState, WindowStore } from './window-store.js';

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
  /** Stable zero-based logical display position; always zero outside `desktop-multi`. */
  readonly displaySlot: number;
}

export interface PersistedWidgetPlacement {
  readonly widgetPlacementId: string;
  readonly widgetKey: string;
  readonly gridColumn: number;
  readonly gridRow: number;
  readonly widthUnits: number;
  readonly heightUnits: number;
}

/** How a stored layout arranges its windows. */
export type PresentationMode = 'freeform' | 'tiled' | 'phone-empty' | 'phone-single' | 'phone-split';

/** The arrangement of one stored layout. */
export interface WorkspaceLayoutDocument {
  /** Null for a transient fresh-mode layout that was never stored. */
  readonly layoutId: string | null;
  readonly name: string;
  readonly presentationMode: PresentationMode;
  readonly primaryWindowId: string | null;
  readonly secondaryWindowId: string | null;
  readonly splitRatioPermille: number | null;
  readonly displayCount: number;
  readonly updatedAtUtc: string;
  readonly windows: readonly PersistedDesktopWindow[];
  readonly widgets: readonly PersistedWidgetPlacement[];
}

/** The layout a session resolved for one workspace class, and how it resolved. */
export interface WorkspaceLayoutResponse {
  readonly workspaceClass: WorkspaceClass;
  readonly layoutScope: LayoutScope;
  readonly restoreMode: RestoreMode;
  /** False in fresh mode, where the server accepts no write at all. */
  readonly persistenceEnabled: boolean;
  readonly revision: number;
  readonly layout: WorkspaceLayoutDocument;
}

export interface AntiforgeryToken {
  readonly headerName: string;
  readonly token: string;
}

export interface LayoutConflict {
  readonly workspaceClass: WorkspaceClass;
  readonly localRevision: number;
  readonly currentRevision: number | null;
  readonly correlationId: string | null;
}

export interface LayoutPersistenceOptions {
  readonly debounceMilliseconds?: number;
  readonly onConflict?: (conflict: LayoutConflict) => void | Promise<void>;
  readonly onFailure?: (error: unknown) => void | Promise<void>;
}

/**
 * How the workspace is arranged, when the caller is changing it.
 *
 * An ordinary autosave stores where the windows are and carries the arrangement over
 * unchanged; only an explicit presentation change passes this.
 */
export interface LayoutPresentation {
  readonly presentationMode: PresentationMode;
  readonly primaryWindowId: string | null;
  readonly secondaryWindowId: string | null;
  readonly splitRatioPermille: number | null;
}

interface PendingSave {
  document: WorkspaceLayoutDocument;
  timer: ReturnType<typeof globalThis.setTimeout> | null;
  inFlight: Promise<WorkspaceLayoutResponse> | null;
}

/**
 * Persists one independent layout document per workspace class.
 *
 * Which stored layout a workspace class resolves to — the user's shared one or the one
 * private to this device — is decided by the server from the device cookie. This client
 * names the workspace class and nothing else, so it cannot reach a layout it was not
 * given.
 *
 * Pointer movement never calls this service; callers schedule only settled window and
 * widget state.
 */
export class DesktopLayoutPersistence {
  readonly #api: JulOsApiClient;
  readonly #debounceMilliseconds: number;
  readonly #onConflict: (conflict: LayoutConflict) => void | Promise<void>;
  readonly #onFailure: (error: unknown) => void | Promise<void>;
  readonly #pending = new Map<WorkspaceClass, PendingSave>();
  readonly #revisions = new Map<WorkspaceClass, number>();
  readonly #documents = new Map<WorkspaceClass, WorkspaceLayoutResponse>();
  readonly #writable = new Set<WorkspaceClass>();
  #antiforgery: AntiforgeryToken | null = null;
  #disposed = false;

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

  public async load(workspaceClass: WorkspaceClass): Promise<WorkspaceLayoutResponse> {
    this.#ensureActive();
    const resolved = await this.#api.get<WorkspaceLayoutResponse>(layoutPath(workspaceClass));
    this.#accept(workspaceClass, resolved);
    return cloneResponse(resolved);
  }

  /** Whether the resolved workspace stores window state at all. */
  public persistenceEnabled(workspaceClass: WorkspaceClass): boolean {
    this.#ensureActive();
    return this.#writable.has(workspaceClass);
  }

  public snapshot(workspaceClass: WorkspaceClass): WorkspaceLayoutResponse {
    this.#ensureActive();
    const document = this.#documents.get(workspaceClass);
    if (document === undefined) {
      throw new Error(`No ${workspaceClass} layout has been loaded or saved.`);
    }
    return cloneResponse(document);
  }

  public schedule(
    workspaceClass: WorkspaceClass,
    windows: readonly PersistedDesktopWindow[],
    widgets: readonly PersistedWidgetPlacement[],
    presentation?: LayoutPresentation,
  ): void {
    this.#ensureActive();

    // Fresh mode performs no persistence at all. Scheduling a write that the server would
    // refuse would turn every autosave into a visible error the user cannot act on.
    if (!this.#writable.has(workspaceClass)) {
      return;
    }

    const resolved = this.#documents.get(workspaceClass);
    const revision = this.#revisions.get(workspaceClass) ?? 0;
    const pending = this.#pending.get(workspaceClass) ?? {
      document: emptyDocument(resolved?.layout),
      timer: null,
      inFlight: null,
    };
    pending.document = {
      ...emptyDocument(resolved?.layout),
      ...(presentation ?? {}),
      windows: cloneWindows(windows),
      widgets: cloneWidgets(widgets),
    };
    this.#revisions.set(workspaceClass, revision);
    if (pending.timer !== null) {
      globalThis.clearTimeout(pending.timer);
    }
    pending.timer = globalThis.setTimeout(() => {
      pending.timer = null;
      void this.#flush(workspaceClass, pending);
    }, this.#debounceMilliseconds);
    this.#pending.set(workspaceClass, pending);
  }

  public async flush(workspaceClass?: WorkspaceClass): Promise<void> {
    this.#ensureActive();
    if (workspaceClass !== undefined) {
      const pending = this.#pending.get(workspaceClass);
      if (pending !== undefined) {
        if (pending.timer !== null) {
          globalThis.clearTimeout(pending.timer);
          pending.timer = null;
        }
        await this.#flush(workspaceClass, pending);
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

  public cancel(workspaceClass?: WorkspaceClass): void {
    this.#ensureActive();
    this.#cancel(workspaceClass);
  }

  public dispose(): void {
    if (this.#disposed) {
      return;
    }
    this.#cancel();
    this.#documents.clear();
    this.#revisions.clear();
    this.#writable.clear();
    this.#antiforgery = null;
    this.#disposed = true;
  }

  async #flush(workspaceClass: WorkspaceClass, pending: PendingSave): Promise<WorkspaceLayoutResponse> {
    if (pending.inFlight !== null) {
      await pending.inFlight;
    }

    const token = await this.#readAntiforgery();
    const expectedRevision = this.#revisions.get(workspaceClass) ?? 0;
    const save = this.#save(workspaceClass, pending.document, expectedRevision, token);
    pending.inFlight = save;

    try {
      const stored = await save;
      this.#accept(workspaceClass, stored);
      return cloneResponse(stored);
    } catch (error) {
      if (error instanceof JulOsApiError && error.status === 409) {
        await this.#onConflict({
          workspaceClass,
          localRevision: expectedRevision,
          currentRevision: error.problem?.currentRevision ?? null,
          correlationId: error.correlationId,
        });

        const currentRevision = error.problem?.currentRevision;
        if (typeof currentRevision === 'number') {
          const current = await this.#api.get<WorkspaceLayoutResponse>(layoutPath(workspaceClass));
          if (current.revision === currentRevision) {
            this.#accept(workspaceClass, current);
            const retried = await this.#save(workspaceClass, pending.document, current.revision, token);
            this.#accept(workspaceClass, retried);
            return cloneResponse(retried);
          }
        }
      } else {
        await this.#onFailure(error);
      }
      throw error;
    } finally {
      pending.inFlight = null;
    }
  }

  #save(
    workspaceClass: WorkspaceClass,
    layout: WorkspaceLayoutDocument,
    expectedRevision: number,
    token: AntiforgeryToken,
  ): Promise<WorkspaceLayoutResponse> {
    return this.#api.requestJson<WorkspaceLayoutResponse>(layoutPath(workspaceClass), {
      method: 'PUT',
      body: { layout, expectedRevision },
      headers: { [token.headerName]: token.token },
    });
  }

  #accept(workspaceClass: WorkspaceClass, resolved: WorkspaceLayoutResponse): void {
    this.#revisions.set(workspaceClass, resolved.revision);
    this.#documents.set(workspaceClass, cloneResponse(resolved));
    if (resolved.persistenceEnabled) {
      this.#writable.add(workspaceClass);
    } else {
      this.#writable.delete(workspaceClass);
      this.#cancel(workspaceClass);
    }
  }

  async #readAntiforgery(): Promise<AntiforgeryToken> {
    this.#antiforgery ??= await this.#api.get<AntiforgeryToken>('/api/v1/auth/antiforgery');
    return this.#antiforgery;
  }

  #cancel(workspaceClass?: WorkspaceClass): void {
    const targets = workspaceClass === undefined
      ? [...this.#pending.values()]
      : [this.#pending.get(workspaceClass)].filter((value): value is PendingSave => value !== undefined);
    for (const pending of targets) {
      if (pending.timer !== null) {
        globalThis.clearTimeout(pending.timer);
        pending.timer = null;
      }
    }
    if (workspaceClass === undefined) {
      this.#pending.clear();
    } else {
      this.#pending.delete(workspaceClass);
    }
  }

  #ensureActive(): void {
    if (this.#disposed) {
      throw new Error('Desktop layout persistence has been disposed.');
    }
  }
}

export function windowsForPersistence(store: WindowStore): readonly PersistedDesktopWindow[] {
  return desktopMultiDisplayWorkspace.windows(store).map((window) => ({
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
    // Multi-display slot assignment lands with the Multi-Display controller; a
    // single-display workspace has exactly one slot and the server rejects anything else.
    displaySlot: 0,
  }));
}

function layoutPath(workspaceClass: WorkspaceClass): string {
  return `/api/v1/workspace-layouts/${encodeURIComponent(workspaceClass)}/current`;
}

/**
 * The arrangement a write starts from.
 *
 * Presentation mode, the phone foreground windows and the display count are carried over
 * from the loaded layout rather than invented here: an autosave stores where the windows
 * are, it does not decide how the workspace is arranged.
 */
function emptyDocument(previous: WorkspaceLayoutDocument | undefined): WorkspaceLayoutDocument {
  return {
    layoutId: previous?.layoutId ?? null,
    name: previous?.name ?? 'Default',
    presentationMode: previous?.presentationMode ?? 'freeform',
    primaryWindowId: previous?.primaryWindowId ?? null,
    secondaryWindowId: previous?.secondaryWindowId ?? null,
    splitRatioPermille: previous?.splitRatioPermille ?? null,
    displayCount: previous?.displayCount ?? 1,
    updatedAtUtc: previous?.updatedAtUtc ?? new Date(0).toISOString(),
    windows: [],
    widgets: [],
  };
}

function cloneResponse(response: WorkspaceLayoutResponse): WorkspaceLayoutResponse {
  return { ...response, layout: cloneDocument(response.layout) };
}

function cloneDocument(document: WorkspaceLayoutDocument): WorkspaceLayoutDocument {
  return {
    ...document,
    windows: cloneWindows(document.windows),
    widgets: cloneWidgets(document.widgets),
  };
}

function cloneWindows(windows: readonly PersistedDesktopWindow[]): readonly PersistedDesktopWindow[] {
  return windows.map((window) => ({ ...window }));
}

function cloneWidgets(widgets: readonly PersistedWidgetPlacement[]): readonly PersistedWidgetPlacement[] {
  return widgets.map((widget) => ({ ...widget }));
}
