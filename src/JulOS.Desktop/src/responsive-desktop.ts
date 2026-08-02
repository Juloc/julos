import type { DesktopWindowSnapshot } from './window-store.js';
import type { ViewportClass } from './layout-persistence.js';

export interface ResponsiveDesktopState {
  readonly viewportClass: ViewportClass;
  readonly visibleWindows: readonly DesktopWindowSnapshot[];
  readonly usesTaskSwitching: boolean;
}

export function classifyViewport(width: number): ViewportClass {
  if (!Number.isFinite(width) || width <= 0) {
    throw new RangeError('Viewport width must be a positive finite number.');
  }

  return width < 720 ? 'mobile' : width < 1100 ? 'tablet' : 'desktop';
}

export function deriveResponsiveDesktop(
  width: number,
  windows: readonly DesktopWindowSnapshot[],
  activeWindowId: string | null,
): ResponsiveDesktopState {
  const viewportClass = classifyViewport(width);
  if (viewportClass !== 'mobile') {
    return {
      viewportClass,
      visibleWindows: windows.filter((window) => window.state !== 'minimized'),
      usesTaskSwitching: false,
    };
  }

  const visible = windows.filter((window) => window.state !== 'minimized');
  const active = visible.find((window) => window.id === activeWindowId) ?? visible.at(-1);
  return {
    viewportClass,
    visibleWindows: active === undefined ? [] : [active],
    usesTaskSwitching: true,
  };
}

export function viewportLayoutKey(userId: string, viewportClass: ViewportClass): string {
  const normalizedUser = userId.trim();
  if (normalizedUser.length === 0 || normalizedUser !== userId) {
    throw new TypeError('User identifier is invalid.');
  }
  return `${normalizedUser}:${viewportClass}`;
}
