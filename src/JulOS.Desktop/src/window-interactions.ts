import {
  WindowStore,
  WindowStoreError,
  type DesktopWindowSnapshot,
  type UsableArea,
  type WindowBounds,
} from './window-store.js';

export type ResizeEdge =
  | 'top'
  | 'right'
  | 'bottom'
  | 'left'
  | 'top-left'
  | 'top-right'
  | 'bottom-left'
  | 'bottom-right';

export type TitleBarPointerSource = 'draggable' | 'interactive';

export interface PointerSample {
  readonly pointerId: number;
  readonly pointerType: string;
  readonly clientX: number;
  readonly clientY: number;
}

export interface MinimumWindowSize {
  readonly width: number;
  readonly height: number;
}

export interface MoveInteractionOptions {
  readonly usableArea: UsableArea;
  readonly titleBarHeight: number;
  readonly minimumVisibleTitleBarWidth: number;
  readonly source: TitleBarPointerSource;
}

export interface ResizeInteractionOptions {
  readonly usableArea: UsableArea;
  readonly minimumSize: MinimumWindowSize;
  readonly edge: ResizeEdge;
}

export interface SettledResize {
  readonly windowId: string;
  readonly bounds: WindowBounds;
  readonly pointerType: string;
}

export interface AnimationFrameScheduler {
  request(callback: (timestamp: number) => void): number;
  cancel(handle: number): void;
}

export interface WindowInteractionOptions {
  readonly scheduler?: AnimationFrameScheduler;
  readonly onResizeSettled?: (resize: SettledResize) => void | Promise<void>;
}

type ActiveInteraction = ActiveMove | ActiveResize;

interface ActiveBase {
  readonly windowId: string;
  readonly pointerId: number;
  readonly pointerType: string;
  readonly startPointer: PointerPoint;
  readonly initialBounds: WindowBounds;
  readonly usableArea: UsableArea;
  pendingPointer: PointerPoint | null;
}

interface ActiveMove extends ActiveBase {
  readonly kind: 'move';
  readonly titleBarHeight: number;
  readonly minimumVisibleTitleBarWidth: number;
}

interface ActiveResize extends ActiveBase {
  readonly kind: 'resize';
  readonly minimumSize: MinimumWindowSize;
  readonly edge: ResizeEdge;
}

interface PointerPoint {
  readonly x: number;
  readonly y: number;
}

const browserScheduler: AnimationFrameScheduler = {
  request: (callback) => globalThis.requestAnimationFrame(callback),
  cancel: (handle) => globalThis.cancelAnimationFrame(handle),
};

/**
 * Converts mouse, touch and pen Pointer Events into deterministic store commands.
 * Pointer movement is reduced to at most one state update per animation frame.
 */
export class WindowInteractionController {
  readonly #store: WindowStore;
  readonly #scheduler: AnimationFrameScheduler;
  readonly #onResizeSettled: (resize: SettledResize) => void | Promise<void>;

  #active: ActiveInteraction | null = null;
  #frameHandle: number | null = null;

  public constructor(store: WindowStore, options: WindowInteractionOptions = {}) {
    this.#store = store;
    this.#scheduler = options.scheduler ?? browserScheduler;
    this.#onResizeSettled = options.onResizeSettled ?? (() => undefined);
  }

  public get activeWindowId(): string | null {
    return this.#active?.windowId ?? null;
  }

  /** Interactive title-bar controls return false and never begin a move. */
  public beginMove(
    windowId: string,
    pointer: PointerSample,
    options: MoveInteractionOptions,
  ): boolean {
    if (options.source === 'interactive') {
      return false;
    }

    this.#requireIdle();
    validatePointer(pointer);
    validateUsableArea(options.usableArea);
    validatePositive(options.titleBarHeight, 'titleBarHeight');
    validatePositive(options.minimumVisibleTitleBarWidth, 'minimumVisibleTitleBarWidth');
    const window = requireNormalWindow(this.#store, windowId);

    this.#store.focus(windowId);
    this.#active = {
      kind: 'move',
      windowId,
      pointerId: pointer.pointerId,
      pointerType: pointer.pointerType,
      startPointer: toPoint(pointer),
      initialBounds: window.bounds,
      usableArea: options.usableArea,
      titleBarHeight: options.titleBarHeight,
      minimumVisibleTitleBarWidth: options.minimumVisibleTitleBarWidth,
      pendingPointer: null,
    };
    return true;
  }

  public beginResize(
    windowId: string,
    pointer: PointerSample,
    options: ResizeInteractionOptions,
  ): void {
    this.#requireIdle();
    validatePointer(pointer);
    validateUsableArea(options.usableArea);
    validateMinimumSize(options.minimumSize);
    const window = requireNormalWindow(this.#store, windowId);

    this.#store.focus(windowId);
    this.#active = {
      kind: 'resize',
      windowId,
      pointerId: pointer.pointerId,
      pointerType: pointer.pointerType,
      startPointer: toPoint(pointer),
      initialBounds: window.bounds,
      usableArea: options.usableArea,
      minimumSize: options.minimumSize,
      edge: options.edge,
      pendingPointer: null,
    };
  }

  public updatePointer(pointer: PointerSample): boolean {
    validatePointer(pointer);
    const active = this.#active;
    if (active === null || active.pointerId !== pointer.pointerId) {
      return false;
    }

    active.pendingPointer = toPoint(pointer);
    if (this.#frameHandle === null) {
      this.#frameHandle = this.#scheduler.request(() => {
        this.#frameHandle = null;
        this.#applyPendingPointer();
      });
    }

    return true;
  }

  public async endPointer(pointer: PointerSample): Promise<boolean> {
    validatePointer(pointer);
    const active = this.#active;
    if (active === null || active.pointerId !== pointer.pointerId) {
      return false;
    }

    active.pendingPointer = toPoint(pointer);
    if (this.#frameHandle !== null) {
      this.#scheduler.cancel(this.#frameHandle);
      this.#frameHandle = null;
    }

    this.#applyPendingPointer();
    this.#active = null;

    if (active.kind === 'resize') {
      const window = requireWindow(this.#store, active.windowId);
      await this.#onResizeSettled({
        windowId: active.windowId,
        bounds: window.bounds,
        pointerType: active.pointerType,
      });
    }

    return true;
  }

  public cancelPointer(pointerId: number): boolean {
    const active = this.#active;
    if (active === null || active.pointerId !== pointerId) {
      return false;
    }

    if (this.#frameHandle !== null) {
      this.#scheduler.cancel(this.#frameHandle);
      this.#frameHandle = null;
    }

    this.#active = null;
    return true;
  }

  #applyPendingPointer(): void {
    const active = this.#active;
    if (active?.pendingPointer === null || active === null) {
      return;
    }

    const pointer = active.pendingPointer;
    active.pendingPointer = null;
    const deltaX = pointer.x - active.startPointer.x;
    const deltaY = pointer.y - active.startPointer.y;
    const bounds = active.kind === 'move'
      ? moveBounds(
          active.initialBounds,
          deltaX,
          deltaY,
          active.usableArea,
          active.titleBarHeight,
          active.minimumVisibleTitleBarWidth,
        )
      : resizeBounds(
          active.initialBounds,
          deltaX,
          deltaY,
          active.edge,
          active.minimumSize,
          active.usableArea,
        );

    this.#store.setBounds(active.windowId, bounds);
  }

  #requireIdle(): void {
    if (this.#active !== null) {
      throw new WindowStoreError(
        'window.interaction_active',
        'Finish the active window interaction before starting another one.',
      );
    }
  }
}

/** Keeps enough of the title bar inside the usable area to recover the window. */
export function moveBounds(
  initial: WindowBounds,
  deltaX: number,
  deltaY: number,
  usableArea: UsableArea,
  titleBarHeight: number,
  minimumVisibleTitleBarWidth: number,
): WindowBounds {
  validateUsableArea(usableArea);
  validatePositive(titleBarHeight, 'titleBarHeight');
  validatePositive(minimumVisibleTitleBarWidth, 'minimumVisibleTitleBarWidth');

  const visibleWidth = Math.min(minimumVisibleTitleBarWidth, initial.width, usableArea.width);
  const visibleHeight = Math.min(titleBarHeight, initial.height, usableArea.height);
  const minimumX = usableArea.x + visibleWidth - initial.width;
  const maximumX = usableArea.x + usableArea.width - visibleWidth;
  const minimumY = usableArea.y;
  const maximumY = usableArea.y + usableArea.height - visibleHeight;

  return {
    x: clamp(initial.x + deltaX, minimumX, maximumX),
    y: clamp(initial.y + deltaY, minimumY, maximumY),
    width: initial.width,
    height: initial.height,
  };
}

/** Resizes only the selected edges while preserving reachable handles and minimum size. */
export function resizeBounds(
  initial: WindowBounds,
  deltaX: number,
  deltaY: number,
  edge: ResizeEdge,
  minimumSize: MinimumWindowSize,
  usableArea: UsableArea,
): WindowBounds {
  validateMinimumSize(minimumSize);
  validateUsableArea(usableArea);

  const effectiveMinimumWidth = Math.min(minimumSize.width, usableArea.width);
  const effectiveMinimumHeight = Math.min(minimumSize.height, usableArea.height);
  let left = initial.x;
  let top = initial.y;
  let right = initial.x + initial.width;
  let bottom = initial.y + initial.height;
  const horizontal = horizontalEdge(edge);
  const vertical = verticalEdge(edge);

  if (horizontal === 'left') {
    right = clamp(right, usableArea.x + effectiveMinimumWidth, usableArea.x + usableArea.width);
    left = clamp(initial.x + deltaX, usableArea.x, right - effectiveMinimumWidth);
  } else if (horizontal === 'right') {
    left = clamp(left, usableArea.x, usableArea.x + usableArea.width - effectiveMinimumWidth);
    right = clamp(
      initial.x + initial.width + deltaX,
      left + effectiveMinimumWidth,
      usableArea.x + usableArea.width,
    );
  }

  if (vertical === 'top') {
    bottom = clamp(bottom, usableArea.y + effectiveMinimumHeight, usableArea.y + usableArea.height);
    top = clamp(initial.y + deltaY, usableArea.y, bottom - effectiveMinimumHeight);
  } else if (vertical === 'bottom') {
    top = clamp(top, usableArea.y, usableArea.y + usableArea.height - effectiveMinimumHeight);
    bottom = clamp(
      initial.y + initial.height + deltaY,
      top + effectiveMinimumHeight,
      usableArea.y + usableArea.height,
    );
  }

  return {
    x: left,
    y: top,
    width: right - left,
    height: bottom - top,
  };
}

function requireNormalWindow(store: WindowStore, windowId: string): DesktopWindowSnapshot {
  const window = requireWindow(store, windowId);
  if (window.state !== 'normal') {
    throw new WindowStoreError(
      'window.interaction_state_invalid',
      `A window in state '${window.state}' cannot be moved or resized.`,
    );
  }

  return window;
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

function toPoint(pointer: PointerSample): PointerPoint {
  return { x: pointer.clientX, y: pointer.clientY };
}

function validatePointer(pointer: PointerSample): void {
  if (!Number.isInteger(pointer.pointerId) || pointer.pointerId < 0) {
    throw new WindowStoreError('window.pointer_invalid', 'Pointer identifier is invalid.');
  }

  if (pointer.pointerType.trim().length === 0 || pointer.pointerType !== pointer.pointerType.trim()) {
    throw new WindowStoreError('window.pointer_invalid', 'Pointer type is invalid.');
  }

  if (!Number.isFinite(pointer.clientX) || !Number.isFinite(pointer.clientY)) {
    throw new WindowStoreError('window.pointer_invalid', 'Pointer coordinates must be finite.');
  }
}

function validateMinimumSize(size: MinimumWindowSize): void {
  validatePositive(size.width, 'minimumSize.width');
  validatePositive(size.height, 'minimumSize.height');
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

function validatePositive(value: number, name: string): void {
  if (!Number.isFinite(value) || value <= 0) {
    throw new WindowStoreError('window.interaction_option_invalid', `${name} must be positive.`);
  }
}

function horizontalEdge(edge: ResizeEdge): 'left' | 'right' | null {
  return edge.includes('left') ? 'left' : edge.includes('right') ? 'right' : null;
}

function verticalEdge(edge: ResizeEdge): 'top' | 'bottom' | null {
  return edge.includes('top') ? 'top' : edge.includes('bottom') ? 'bottom' : null;
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}
