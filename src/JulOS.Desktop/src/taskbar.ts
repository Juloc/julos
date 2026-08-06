import {
  WindowStore,
  type DesktopWindowSnapshot,
  type OpenWindowRequest,
  type UsableArea,
} from './window-store.js';

export type ApplicationInstancePolicy = 'single-user' | 'single-target' | 'multiple';

export interface TaskbarGroup {
  readonly applicationId: string;
  readonly windows: readonly DesktopWindowSnapshot[];
  readonly count: number;
  readonly focused: boolean;
  readonly minimizedOnly: boolean;
}

export function buildTaskbarGroups(
  windows: readonly DesktopWindowSnapshot[],
  focusedWindowId: string | null,
): readonly TaskbarGroup[] {
  const groups = new Map<string, DesktopWindowSnapshot[]>();
  for (const window of windows) {
    const group = groups.get(window.applicationId) ?? [];
    group.push(window);
    groups.set(window.applicationId, group);
  }

  return [...groups.entries()].map(([applicationId, groupedWindows]) => ({
    applicationId,
    windows: groupedWindows,
    count: groupedWindows.length,
    focused: groupedWindows.some((window) => window.id === focusedWindowId),
    minimizedOnly: groupedWindows.every((window) => window.state === 'minimized'),
  }));
}

export class DesktopWindowCoordinator {
  readonly #store: WindowStore;

  public constructor(store: WindowStore) {
    this.#store = store;
  }

  public openOrFocus(
    request: OpenWindowRequest,
    policy: ApplicationInstancePolicy,
    usableArea: UsableArea,
  ): DesktopWindowSnapshot {
    const existing = this.#findExisting(request, policy);
    if (existing === undefined) {
      return this.#store.open(request);
    }

    return existing.state === 'minimized'
      ? this.#store.restore(existing.id, usableArea)
      : this.#store.focus(existing.id);
  }

  public activate(windowId: string, usableArea: UsableArea): DesktopWindowSnapshot {
    const window = this.#store.windows.find((candidate) => candidate.id === windowId);
    if (window === undefined) {
      return this.#store.focus(windowId);
    }

    return window.state === 'minimized'
      ? this.#store.restore(windowId, usableArea)
      : this.#store.focus(windowId);
  }

  public switchByOffset(offset: number, usableArea: UsableArea): DesktopWindowSnapshot | null {
    const windows = [...this.#store.windows].reverse();
    if (windows.length === 0) {
      return null;
    }

    const focusedId = this.#store.frontWindow?.id ?? null;
    const currentIndex = Math.max(0, windows.findIndex((window) => window.id === focusedId));
    const normalizedOffset = ((offset % windows.length) + windows.length) % windows.length;
    const target = windows[(currentIndex + normalizedOffset) % windows.length];
    return target === undefined ? null : this.activate(target.id, usableArea);
  }

  #findExisting(
    request: OpenWindowRequest,
    policy: ApplicationInstancePolicy,
  ): DesktopWindowSnapshot | undefined {
    if (policy === 'multiple') {
      return undefined;
    }

    return this.#store.windows.find((window) => {
      if (window.applicationId !== request.applicationId) {
        return false;
      }

      return policy === 'single-user'
        || window.launchTargetId === (request.launchTargetId ?? null);
    });
  }
}
