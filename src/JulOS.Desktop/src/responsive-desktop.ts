import type { DesktopViewport } from './layout-persistence.js';
import type { DesktopWindowSnapshot, WindowStore } from './window-store.js';

export type DesktopPresentationMode = 'windowed' | 'focused' | 'task-switching';

export interface ResponsiveDesktopSnapshot {
  readonly viewport: DesktopViewport;
  readonly presentation: DesktopPresentationMode;
  readonly visibleWindows: readonly DesktopWindowSnapshot[];
  readonly taskWindows: readonly DesktopWindowSnapshot[];
  readonly activeWindowId: string | null;
}

export interface DerivedResponsiveDesktop {
  readonly viewportClass: DesktopViewport;
  readonly presentation: DesktopPresentationMode;
  readonly usesTaskSwitching: boolean;
  readonly visibleWindows: readonly DesktopWindowSnapshot[];
  readonly taskWindows: readonly DesktopWindowSnapshot[];
  readonly activeWindowId: string | null;
}

export interface ViewportThresholds {
  readonly tablet: number;
  readonly desktop: number;
}

const defaultThresholds: ViewportThresholds = {
  tablet: 720,
  desktop: 1100,
};

/** Separates persisted layouts and presentation rules by viewport class. */
export class ResponsiveDesktopController {
  readonly #store: WindowStore;
  readonly #thresholds: ViewportThresholds;
  #viewport: DesktopViewport;
  #taskSelection: string | null = null;

  public constructor(
    store: WindowStore,
    width: number,
    thresholds: ViewportThresholds = defaultThresholds,
  ) {
    validateThresholds(thresholds);
    this.#store = store;
    this.#thresholds = thresholds;
    this.#viewport = classifyViewport(width, thresholds);
  }

  public get viewport(): DesktopViewport {
    return this.#viewport;
  }

  public resize(width: number): DesktopViewport {
    this.#viewport = classifyViewport(width, this.#thresholds);
    if (this.#viewport === 'desktop') {
      this.#taskSelection = null;
    }
    return this.#viewport;
  }

  public selectTask(windowId: string): void {
    if (!this.#store.windows.some((window) => window.id === windowId)) {
      throw new ResponsiveDesktopError('desktop.window_missing', 'The selected task is not open.');
    }
    this.#taskSelection = windowId;
  }

  public snapshot(): ResponsiveDesktopSnapshot {
    const taskWindows = [...this.#store.windows].reverse();
    if (this.#viewport === 'desktop') {
      return {
        viewport: 'desktop',
        presentation: 'windowed',
        visibleWindows: this.#store.windows.filter((window) => window.state !== 'minimized'),
        taskWindows,
        activeWindowId: this.#store.frontWindow?.id ?? null,
      };
    }

    const selected = this.#selectFocusedWindow(taskWindows);
    return {
      viewport: this.#viewport,
      presentation: this.#viewport === 'tablet' ? 'focused' : 'task-switching',
      visibleWindows: selected === null ? [] : [selected],
      taskWindows,
      activeWindowId: selected?.id ?? null,
    };
  }

  #selectFocusedWindow(windows: readonly DesktopWindowSnapshot[]): DesktopWindowSnapshot | null {
    const requested = this.#taskSelection === null
      ? null
      : windows.find((window) => window.id === this.#taskSelection) ?? null;
    const selected = requested
      ?? windows.find((window) => window.state !== 'minimized')
      ?? windows[0]
      ?? null;
    if (selected !== null) {
      this.#taskSelection = selected.id;
    }
    return selected;
  }
}

export class ResponsiveDesktopError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = 'ResponsiveDesktopError';
    this.code = code;
  }
}

export function classifyViewport(
  width: number,
  thresholds: ViewportThresholds = defaultThresholds,
): DesktopViewport {
  if (!Number.isFinite(width) || width <= 0) {
    throw new ResponsiveDesktopError('desktop.viewport_invalid', 'Viewport width must be positive.');
  }
  validateThresholds(thresholds);
  return width >= thresholds.desktop ? 'desktop' : width >= thresholds.tablet ? 'tablet' : 'mobile';
}

export function deriveResponsiveDesktop(
  width: number,
  windows: readonly DesktopWindowSnapshot[],
  activeWindowId: string | null,
  thresholds: ViewportThresholds = defaultThresholds,
): DerivedResponsiveDesktop {
  const viewportClass = classifyViewport(width, thresholds);
  const taskWindows = [...windows].sort((left, right) => right.zIndex - left.zIndex);
  if (viewportClass === 'desktop') {
    return {
      viewportClass,
      presentation: 'windowed',
      usesTaskSwitching: false,
      visibleWindows: windows.filter((window) => window.state !== 'minimized'),
      taskWindows,
      activeWindowId: activeWindowId ?? taskWindows[0]?.id ?? null,
    };
  }

  const requested = activeWindowId === null
    ? null
    : windows.find((window) => window.id === activeWindowId && window.state !== 'minimized') ?? null;
  const selected = requested
    ?? taskWindows.find((window) => window.state !== 'minimized')
    ?? null;
  return {
    viewportClass,
    presentation: viewportClass === 'mobile' ? 'task-switching' : 'focused',
    usesTaskSwitching: viewportClass === 'mobile',
    visibleWindows: selected === null ? [] : [selected],
    taskWindows,
    activeWindowId: selected?.id ?? null,
  };
}

export function viewportLayoutKey(userId: string, viewport: DesktopViewport): string {
  const normalizedUser = userId.trim();
  if (normalizedUser.length === 0 || normalizedUser !== userId) {
    throw new ResponsiveDesktopError('desktop.user_invalid', 'User identifier is invalid.');
  }
  return `${normalizedUser}:${viewport}`;
}

function validateThresholds(thresholds: ViewportThresholds): void {
  if (
    !Number.isFinite(thresholds.tablet)
    || !Number.isFinite(thresholds.desktop)
    || thresholds.tablet < 320
    || thresholds.desktop <= thresholds.tablet
  ) {
    throw new ResponsiveDesktopError('desktop.thresholds_invalid', 'Viewport thresholds are invalid.');
  }
}
