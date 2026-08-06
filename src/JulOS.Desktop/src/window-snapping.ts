import { moveBounds } from './window-interactions.js';
import {
  WindowStore,
  WindowStoreError,
  boundsForWindowState,
  type DesktopWindowSnapshot,
  type FixedWindowState,
  type UsableArea,
  type WindowBounds,
} from './window-store.js';

export type SnapTarget =
  | 'left'
  | 'right'
  | 'top-left'
  | 'top-right'
  | 'bottom-left'
  | 'bottom-right'
  | 'maximize';

export type SnapKeyboardCommand = SnapTarget | 'restore';

export interface SnapPointer {
  readonly x: number;
  readonly y: number;
}

export interface SnapPreview {
  readonly target: SnapTarget;
  readonly bounds: WindowBounds;
}

export type SnapPreviewListener = (preview: SnapPreview | null) => void;

/** Pointer and keyboard snapping share this single geometry and state transition service. */
export class WindowSnapController {
  readonly #store: WindowStore;
  readonly #listeners = new Set<SnapPreviewListener>();
  #preview: SnapPreview | null = null;

  public constructor(store: WindowStore) {
    this.#store = store;
  }

  public get preview(): SnapPreview | null {
    return this.#preview === null
      ? null
      : { target: this.#preview.target, bounds: cloneBounds(this.#preview.bounds) };
  }

  public subscribe(listener: SnapPreviewListener): () => void {
    this.#listeners.add(listener);
    listener(this.preview);
    return () => this.#listeners.delete(listener);
  }

  public updatePreview(
    pointer: SnapPointer,
    usableArea: UsableArea,
    edgeThreshold = 32,
  ): SnapPreview | null {
    const target = detectSnapTarget(pointer, usableArea, edgeThreshold);
    const preview = target === null
      ? null
      : {
          target,
          bounds: boundsForWindowState(targetState(target), usableArea),
        };

    if (!samePreview(this.#preview, preview)) {
      this.#preview = preview;
      this.#publish();
    }

    return this.preview;
  }

  public clearPreview(): void {
    if (this.#preview === null) {
      return;
    }

    this.#preview = null;
    this.#publish();
  }

  public commitPointer(
    windowId: string,
    pointer: SnapPointer,
    usableArea: UsableArea,
    edgeThreshold = 32,
  ): DesktopWindowSnapshot | null {
    const target = detectSnapTarget(pointer, usableArea, edgeThreshold);
    this.clearPreview();
    return target === null
      ? null
      : this.#store.applyFixedState(windowId, targetState(target), usableArea);
  }

  public applyKeyboard(
    windowId: string,
    command: SnapKeyboardCommand,
    usableArea: UsableArea,
  ): DesktopWindowSnapshot {
    this.clearPreview();
    return command === 'restore'
      ? this.#store.restore(windowId, usableArea)
      : this.#store.applyFixedState(windowId, targetState(command), usableArea);
  }

  /**
   * Restores a fixed window under the pointer while preserving its horizontal pointer ratio.
   */
  public restoreForDrag(
    windowId: string,
    pointer: SnapPointer,
    usableArea: UsableArea,
    titleBarHeight: number,
    minimumVisibleTitleBarWidth: number,
  ): DesktopWindowSnapshot {
    const fixed = requireWindow(this.#store, windowId);
    if (fixed.state === 'normal') {
      return fixed;
    }

    if (fixed.state === 'minimized') {
      throw new WindowStoreError(
        'window.not_visible',
        'A minimized window must be restored from the taskbar before dragging.',
      );
    }

    const horizontalRatio = clamp((pointer.x - fixed.bounds.x) / fixed.bounds.width, 0, 1);
    const titleOffset = clamp(pointer.y - fixed.bounds.y, 0, titleBarHeight);
    const restored = this.#store.restore(windowId, usableArea);
    const desiredX = pointer.x - restored.bounds.width * horizontalRatio;
    const desiredY = pointer.y - titleOffset;
    const reachable = moveBounds(
      restored.bounds,
      desiredX - restored.bounds.x,
      desiredY - restored.bounds.y,
      usableArea,
      titleBarHeight,
      minimumVisibleTitleBarWidth,
    );

    return this.#store.setBounds(windowId, reachable);
  }

  #publish(): void {
    const preview = this.preview;
    for (const listener of this.#listeners) {
      listener(preview);
    }
  }
}

export function detectSnapTarget(
  pointer: SnapPointer,
  usableArea: UsableArea,
  edgeThreshold = 32,
): SnapTarget | null {
  validatePointer(pointer);
  validateUsableArea(usableArea);
  if (!Number.isFinite(edgeThreshold) || edgeThreshold <= 0) {
    throw new WindowStoreError(
      'window.snap_threshold_invalid',
      'The snap edge threshold must be positive.',
    );
  }

  const threshold = Math.min(edgeThreshold, usableArea.width / 2, usableArea.height / 2);
  const right = usableArea.x + usableArea.width;
  const bottom = usableArea.y + usableArea.height;
  const nearLeft = pointer.x <= usableArea.x + threshold;
  const nearRight = pointer.x >= right - threshold;
  const nearTop = pointer.y <= usableArea.y + threshold;
  const nearBottom = pointer.y >= bottom - threshold;

  if (nearTop && nearLeft) {
    return 'top-left';
  }
  if (nearTop && nearRight) {
    return 'top-right';
  }
  if (nearBottom && nearLeft) {
    return 'bottom-left';
  }
  if (nearBottom && nearRight) {
    return 'bottom-right';
  }
  if (nearTop) {
    return 'maximize';
  }
  if (nearLeft) {
    return 'left';
  }
  if (nearRight) {
    return 'right';
  }

  return null;
}

export function targetState(target: SnapTarget): FixedWindowState {
  switch (target) {
    case 'left':
      return 'snapped-left';
    case 'right':
      return 'snapped-right';
    case 'top-left':
      return 'snapped-top-left';
    case 'top-right':
      return 'snapped-top-right';
    case 'bottom-left':
      return 'snapped-bottom-left';
    case 'bottom-right':
      return 'snapped-bottom-right';
    case 'maximize':
      return 'maximized';
  }
}

function requireWindow(store: WindowStore, windowId: string): DesktopWindowSnapshot {
  const window = store.windows.find((candidate) => candidate.id === windowId);
  if (window === undefined) {
    throw new WindowStoreError(
      'window.not_open',
      `No open window has identifier '${windowId}'.`,
    );
  }
  return window;
}

function samePreview(left: SnapPreview | null, right: SnapPreview | null): boolean {
  if (left === null || right === null) {
    return left === right;
  }

  return left.target === right.target
    && left.bounds.x === right.bounds.x
    && left.bounds.y === right.bounds.y
    && left.bounds.width === right.bounds.width
    && left.bounds.height === right.bounds.height;
}

function cloneBounds(bounds: WindowBounds): WindowBounds {
  return { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height };
}

function validatePointer(pointer: SnapPointer): void {
  if (!Number.isFinite(pointer.x) || !Number.isFinite(pointer.y)) {
    throw new WindowStoreError('window.snap_pointer_invalid', 'Snap pointer coordinates must be finite.');
  }
}

function validateUsableArea(area: UsableArea): void {
  if (
    !Number.isFinite(area.x)
    || !Number.isFinite(area.y)
    || !Number.isFinite(area.width)
    || !Number.isFinite(area.height)
    || area.width <= 0
    || area.height <= 0
  ) {
    throw new WindowStoreError('window.usable_area_invalid', 'The usable area is invalid.');
  }
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}
