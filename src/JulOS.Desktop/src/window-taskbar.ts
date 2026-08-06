import {
  WindowStore,
  WindowStoreError,
  type DesktopWindowSnapshot,
  type OpenWindowRequest,
  type UsableArea,
} from './window-store.js';

export type ApplicationInstancePolicy =
  | 'single-instance-per-user'
  | 'single-instance-per-target'
  | 'multiple-instances';

export interface WindowLaunchRequest extends OpenWindowRequest {
  readonly instancePolicy: ApplicationInstancePolicy;
}

export interface WindowLaunchResult {
  readonly outcome: 'opened' | 'focused-existing';
  readonly window: DesktopWindowSnapshot;
}

export interface TaskbarApplicationGroup {
  readonly applicationId: string;
  readonly title: string;
  readonly count: number;
  readonly minimizedCount: number;
  readonly windowIds: readonly string[];
  readonly activeWindowId: string | null;
}

export interface WindowSwitcherSnapshot {
  readonly windowIds: readonly string[];
  readonly selectedWindowId: string;
  readonly selectedIndex: number;
}

/** Launches windows according to the application-declared instance policy. */
export class WindowLaunchCoordinator {
  readonly #store: WindowStore;

  public constructor(store: WindowStore) {
    this.#store = store;
  }

  public launch(request: WindowLaunchRequest, usableArea: UsableArea): WindowLaunchResult {
    const existing = this.#findExisting(request);
    if (existing !== null) {
      return {
        outcome: 'focused-existing',
        window: activateWindow(this.#store, existing.id, usableArea),
      };
    }

    const { instancePolicy: _, ...openRequest } = request;
    return {
      outcome: 'opened',
      window: this.#store.open(openRequest),
    };
  }

  #findExisting(request: WindowLaunchRequest): DesktopWindowSnapshot | null {
    if (request.instancePolicy === 'multiple-instances') {
      return null;
    }

    const windows = [...this.#store.windows].reverse();
    return windows.find((window) => {
      if (window.applicationId !== request.applicationId) {
        return false;
      }

      return request.instancePolicy === 'single-instance-per-user'
        || window.launchTargetId === (request.launchTargetId ?? null);
    }) ?? null;
  }
}

/** Derives grouped taskbar state and activates minimized or background windows. */
export class TaskbarWindowModel {
  readonly #store: WindowStore;

  public constructor(store: WindowStore) {
    this.#store = store;
  }

  public get groups(): readonly TaskbarApplicationGroup[] {
    const groups = new Map<string, DesktopWindowSnapshot[]>();
    for (const window of this.#store.windows) {
      const group = groups.get(window.applicationId);
      if (group === undefined) {
        groups.set(window.applicationId, [window]);
      } else {
        group.push(window);
      }
    }

    const frontWindowId = this.#store.frontWindow?.id ?? null;
    return [...groups.entries()].map(([applicationId, windows]) => {
      const frontmost = windows.at(-1);
      if (frontmost === undefined) {
        throw new WindowStoreError('window.group_empty', 'A taskbar group cannot be empty.');
      }

      return {
        applicationId,
        title: frontmost.title,
        count: windows.length,
        minimizedCount: windows.filter((window) => window.state === 'minimized').length,
        windowIds: [...windows].reverse().map((window) => window.id),
        activeWindowId: windows.some((window) => window.id === frontWindowId)
          ? frontWindowId
          : null,
      };
    });
  }

  public activateWindow(windowId: string, usableArea: UsableArea): DesktopWindowSnapshot {
    return activateWindow(this.#store, windowId, usableArea);
  }
}

/** Freezes one MRU order while Alt+Tab is held and applies focus only on commit. */
export class AltTabWindowSwitcher {
  readonly #store: WindowStore;
  readonly #taskbar: TaskbarWindowModel;
  #frozenOrder: string[] | null = null;
  #selectedIndex = 0;

  public constructor(store: WindowStore) {
    this.#store = store;
    this.#taskbar = new TaskbarWindowModel(store);
  }

  public get current(): WindowSwitcherSnapshot | null {
    const order = this.#availableOrder();
    if (order.length === 0) {
      return null;
    }

    this.#selectedIndex = normalizeIndex(this.#selectedIndex, order.length);
    return {
      windowIds: order,
      selectedWindowId: order[this.#selectedIndex] ?? order[0] ?? '',
      selectedIndex: this.#selectedIndex,
    };
  }

  public begin(): WindowSwitcherSnapshot | null {
    if (this.#frozenOrder === null) {
      this.#frozenOrder = [...this.#store.windows].reverse().map((window) => window.id);
      this.#selectedIndex = this.#frozenOrder.length > 1 ? 1 : 0;
    }

    return this.current;
  }

  public next(): WindowSwitcherSnapshot | null {
    const current = this.begin();
    if (current === null) {
      return null;
    }

    this.#selectedIndex = normalizeIndex(this.#selectedIndex + 1, current.windowIds.length);
    return this.current;
  }

  public previous(): WindowSwitcherSnapshot | null {
    const current = this.begin();
    if (current === null) {
      return null;
    }

    this.#selectedIndex = normalizeIndex(this.#selectedIndex - 1, current.windowIds.length);
    return this.current;
  }

  public commit(usableArea: UsableArea): DesktopWindowSnapshot | null {
    const current = this.current;
    this.#reset();
    return current === null
      ? null
      : this.#taskbar.activateWindow(current.selectedWindowId, usableArea);
  }

  public cancel(): void {
    this.#reset();
  }

  #availableOrder(): string[] {
    const order = this.#frozenOrder;
    if (order === null) {
      return [];
    }

    const openIdentifiers = new Set(this.#store.windows.map((window) => window.id));
    return order.filter((windowId) => openIdentifiers.has(windowId));
  }

  #reset(): void {
    this.#frozenOrder = null;
    this.#selectedIndex = 0;
  }
}

function activateWindow(
  store: WindowStore,
  windowId: string,
  usableArea: UsableArea,
): DesktopWindowSnapshot {
  const window = store.windows.find((candidate) => candidate.id === windowId);
  if (window === undefined) {
    throw new WindowStoreError(
      'window.not_open',
      `No open window has identifier '${windowId}'.`,
    );
  }

  return window.state === 'minimized'
    ? store.restore(windowId, usableArea)
    : store.focus(windowId);
}

function normalizeIndex(value: number, length: number): number {
  return ((value % length) + length) % length;
}
