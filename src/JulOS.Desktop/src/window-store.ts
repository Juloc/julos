export type WindowPresentationState =
  | 'normal'
  | 'minimized'
  | 'maximized'
  | 'snapped-left'
  | 'snapped-right'
  | 'snapped-top-left'
  | 'snapped-top-right'
  | 'snapped-bottom-left'
  | 'snapped-bottom-right'
  | 'full-screen';

export type FixedWindowState = Exclude<WindowPresentationState, 'normal' | 'minimized'>;

export interface WindowBounds {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

export interface UsableArea extends WindowBounds {}

export interface DesktopWindowSnapshot {
  readonly id: string;
  readonly applicationId: string;
  readonly launchTargetId: string | null;
  readonly title: string;
  readonly state: WindowPresentationState;
  readonly bounds: WindowBounds;
  readonly restoreBounds: WindowBounds;
  readonly zIndex: number;
}

export interface OpenWindowRequest {
  readonly id?: string;
  readonly applicationId: string;
  readonly launchTargetId?: string | null;
  readonly title: string;
  readonly bounds: WindowBounds;
}

export type WindowStoreListener = (windows: readonly DesktopWindowSnapshot[]) => void;
export type WindowIdentifierGenerator = () => string;

export class WindowStoreError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = 'WindowStoreError';
    this.code = code;
  }
}

interface StoredWindow {
  readonly id: string;
  readonly applicationId: string;
  readonly launchTargetId: string | null;
  readonly title: string;
  state: WindowPresentationState;
  bounds: WindowBounds;
  restoreBounds: WindowBounds;
  zIndex: number;
  stateBeforeMinimize: Exclude<WindowPresentationState, 'minimized'> | null;
}

/**
 * Authoritative in-memory presentation state for one Desktop viewport.
 * The ordered array is the source of truth; z-index values are always derived from it.
 */
export class WindowStore {
  readonly #windows: StoredWindow[] = [];
  readonly #listeners = new Set<WindowStoreListener>();
  readonly #generateIdentifier: WindowIdentifierGenerator;

  public constructor(generateIdentifier: WindowIdentifierGenerator = () => crypto.randomUUID()) {
    this.#generateIdentifier = generateIdentifier;
  }

  public get windows(): readonly DesktopWindowSnapshot[] {
    return this.#snapshot();
  }

  public get frontWindow(): DesktopWindowSnapshot | null {
    const window = this.#windows.at(-1);
    return window === undefined ? null : toSnapshot(window);
  }

  public subscribe(listener: WindowStoreListener): () => void {
    this.#listeners.add(listener);
    listener(this.#snapshot());
    return () => this.#listeners.delete(listener);
  }

  public open(request: OpenWindowRequest): DesktopWindowSnapshot {
    validateIdentifier(request.applicationId, 'applicationId');
    validateOptionalIdentifier(request.launchTargetId, 'launchTargetId');
    validateTitle(request.title);
    const bounds = validateBounds(request.bounds);
    const id = request.id ?? this.#generateIdentifier();
    validateIdentifier(id, 'id');

    if (this.#windows.some((window) => window.id === id)) {
      throw new WindowStoreError(
        'window.already_open',
        `A window with identifier '${id}' is already open.`,
      );
    }

    const window: StoredWindow = {
      id,
      applicationId: request.applicationId,
      launchTargetId: request.launchTargetId ?? null,
      title: request.title,
      state: 'normal',
      bounds,
      restoreBounds: bounds,
      zIndex: this.#windows.length,
      stateBeforeMinimize: null,
    };

    this.#windows.push(window);
    this.#normalizeZOrder();
    this.#publish();
    return toSnapshot(window);
  }

  public close(windowId: string): void {
    const index = this.#requireIndex(windowId);
    this.#windows.splice(index, 1);
    this.#normalizeZOrder();
    this.#publish();
  }

  public focus(windowId: string): DesktopWindowSnapshot {
    const index = this.#requireIndex(windowId);
    const [window] = this.#windows.splice(index, 1);
    if (window === undefined) {
      throw new WindowStoreError('window.not_open', 'The requested window is not open.');
    }

    this.#windows.push(window);
    this.#normalizeZOrder();
    this.#publish();
    return toSnapshot(window);
  }

  public move(windowId: string, x: number, y: number): DesktopWindowSnapshot {
    const window = this.#requireWindow(windowId);
    this.#requireNormalGeometry(window);
    const bounds = validateBounds({ ...window.bounds, x, y });
    window.bounds = bounds;
    window.restoreBounds = bounds;
    this.#publish();
    return toSnapshot(window);
  }

  public resize(windowId: string, width: number, height: number): DesktopWindowSnapshot {
    const window = this.#requireWindow(windowId);
    this.#requireNormalGeometry(window);
    const bounds = validateBounds({ ...window.bounds, width, height });
    window.bounds = bounds;
    window.restoreBounds = bounds;
    this.#publish();
    return toSnapshot(window);
  }

  public setBounds(windowId: string, bounds: WindowBounds): DesktopWindowSnapshot {
    const window = this.#requireWindow(windowId);
    this.#requireNormalGeometry(window);
    const validated = validateBounds(bounds);
    window.bounds = validated;
    window.restoreBounds = validated;
    this.#publish();
    return toSnapshot(window);
  }

  public minimize(windowId: string): DesktopWindowSnapshot {
    const window = this.#requireWindow(windowId);
    if (window.state !== 'minimized') {
      window.stateBeforeMinimize = window.state;
      window.state = 'minimized';
      this.#publish();
    }

    return toSnapshot(window);
  }

  public maximize(windowId: string, usableArea: UsableArea): DesktopWindowSnapshot {
    return this.applyFixedState(windowId, 'maximized', usableArea);
  }

  public applyFixedState(
    windowId: string,
    state: FixedWindowState,
    usableArea: UsableArea,
  ): DesktopWindowSnapshot {
    const window = this.#requireWindow(windowId);
    if (window.state === 'minimized') {
      throw new WindowStoreError(
        'window.not_visible',
        'Restore a minimized window before applying fixed geometry.',
      );
    }

    const bounds = boundsForWindowState(state, validateUsableArea(usableArea));
    if (window.state === 'normal') {
      window.restoreBounds = window.bounds;
    }

    window.stateBeforeMinimize = null;
    window.state = state;
    window.bounds = bounds;
    this.focus(windowId);
    return toSnapshot(window);
  }

  /**
   * Restores a minimized window to its prior state, or a fixed window to normal bounds.
   */
  public restore(windowId: string, usableArea?: UsableArea): DesktopWindowSnapshot {
    const window = this.#requireWindow(windowId);

    if (window.state === 'minimized') {
      const previous = window.stateBeforeMinimize ?? 'normal';
      window.stateBeforeMinimize = null;
      window.state = previous;
      window.bounds = previous === 'normal'
        ? window.restoreBounds
        : boundsForWindowState(previous, requireUsableArea(usableArea));
      this.focus(windowId);
      return toSnapshot(window);
    }

    if (window.state !== 'normal') {
      window.stateBeforeMinimize = null;
      window.state = 'normal';
      window.bounds = window.restoreBounds;
      this.focus(windowId);
      return toSnapshot(window);
    }

    return this.focus(windowId);
  }

  public clear(): void {
    if (this.#windows.length === 0) {
      return;
    }

    this.#windows.length = 0;
    this.#publish();
  }

  #requireWindow(windowId: string): StoredWindow {
    validateIdentifier(windowId, 'windowId');
    const window = this.#windows.find((candidate) => candidate.id === windowId);
    if (window === undefined) {
      throw new WindowStoreError(
        'window.not_open',
        `No open window has identifier '${windowId}'.`,
      );
    }

    return window;
  }

  #requireIndex(windowId: string): number {
    validateIdentifier(windowId, 'windowId');
    const index = this.#windows.findIndex((window) => window.id === windowId);
    if (index < 0) {
      throw new WindowStoreError(
        'window.not_open',
        `No open window has identifier '${windowId}'.`,
      );
    }

    return index;
  }

  #requireNormalGeometry(window: StoredWindow): void {
    if (window.state !== 'normal') {
      throw new WindowStoreError(
        'window.bounds_not_owned',
        `A window in state '${window.state}' does not own free geometry.`,
      );
    }
  }

  #normalizeZOrder(): void {
    this.#windows.forEach((window, index) => {
      window.zIndex = index;
    });
  }

  #snapshot(): readonly DesktopWindowSnapshot[] {
    return this.#windows.map(toSnapshot);
  }

  #publish(): void {
    const snapshot = this.#snapshot();
    for (const listener of this.#listeners) {
      listener(snapshot);
    }
  }
}

export function boundsForWindowState(
  state: FixedWindowState,
  area: WindowBounds,
): WindowBounds {
  const validatedArea = validateBounds(area);
  if (state === 'maximized' || state === 'full-screen') {
    return cloneBounds(validatedArea);
  }

  const leftWidth = Math.floor(validatedArea.width / 2);
  const rightWidth = validatedArea.width - leftWidth;
  const topHeight = Math.floor(validatedArea.height / 2);
  const bottomHeight = validatedArea.height - topHeight;

  switch (state) {
    case 'snapped-left':
      return {
        x: validatedArea.x,
        y: validatedArea.y,
        width: leftWidth,
        height: validatedArea.height,
      };
    case 'snapped-right':
      return {
        x: validatedArea.x + leftWidth,
        y: validatedArea.y,
        width: rightWidth,
        height: validatedArea.height,
      };
    case 'snapped-top-left':
      return {
        x: validatedArea.x,
        y: validatedArea.y,
        width: leftWidth,
        height: topHeight,
      };
    case 'snapped-top-right':
      return {
        x: validatedArea.x + leftWidth,
        y: validatedArea.y,
        width: rightWidth,
        height: topHeight,
      };
    case 'snapped-bottom-left':
      return {
        x: validatedArea.x,
        y: validatedArea.y + topHeight,
        width: leftWidth,
        height: bottomHeight,
      };
    case 'snapped-bottom-right':
      return {
        x: validatedArea.x + leftWidth,
        y: validatedArea.y + topHeight,
        width: rightWidth,
        height: bottomHeight,
      };
  }
}

function toSnapshot(window: StoredWindow): DesktopWindowSnapshot {
  return {
    id: window.id,
    applicationId: window.applicationId,
    launchTargetId: window.launchTargetId,
    title: window.title,
    state: window.state,
    bounds: cloneBounds(window.bounds),
    restoreBounds: cloneBounds(window.restoreBounds),
    zIndex: window.zIndex,
  };
}

function cloneBounds(bounds: WindowBounds): WindowBounds {
  return {
    x: bounds.x,
    y: bounds.y,
    width: bounds.width,
    height: bounds.height,
  };
}

function validateBounds(bounds: WindowBounds): WindowBounds {
  for (const [name, value] of Object.entries(bounds)) {
    if (!Number.isFinite(value)) {
      throw new WindowStoreError('window.bounds_invalid', `Window ${name} must be finite.`);
    }
  }

  if (bounds.width <= 0 || bounds.height <= 0) {
    throw new WindowStoreError(
      'window.bounds_invalid',
      'Window width and height must be greater than zero.',
    );
  }

  return cloneBounds(bounds);
}

function validateUsableArea(area: UsableArea): WindowBounds {
  return validateBounds(area);
}

function requireUsableArea(area: UsableArea | undefined): WindowBounds {
  if (area === undefined) {
    throw new WindowStoreError(
      'window.usable_area_required',
      'Restoring a fixed minimized window requires the current usable area.',
    );
  }

  return validateUsableArea(area);
}

function validateIdentifier(value: string, name: string): void {
  if (
    value.trim().length === 0
    || value !== value.trim()
    || value.length > 256
    || [...value].some((character) => character < ' ')
  ) {
    throw new WindowStoreError('window.identifier_invalid', `Window field '${name}' is invalid.`);
  }
}

function validateOptionalIdentifier(value: string | null | undefined, name: string): void {
  if (value !== null && value !== undefined) {
    validateIdentifier(value, name);
  }
}

function validateTitle(value: string): void {
  if (value.trim().length === 0 || value !== value.trim() || value.length > 512) {
    throw new WindowStoreError('window.title_invalid', 'Window title is invalid.');
  }
}
