// MOB-005: the phone foreground rules from docs/MOBILE_PWA.md section 6.
//
// A phone shows at most two foreground windows. This controller owns which windows those
// are and which pane has focus; workspace-stage.ts turns that into geometry, and the
// layout persists it. Every window stays open and reachable in the task switcher: leaving
// the foreground is a presentation change, never a close.

import {
  clampSplitRatioPermille,
  type PhoneForeground,
  type PhonePane,
} from './workspace-stage.js';

/** What changed after a foreground operation. */
export interface PhoneForegroundChange {
  readonly foreground: PhoneForeground;
  /**
   * The window pushed out of the foreground, if any.
   *
   * It stays open. The caller hands it to the Surface scheduler so it takes its configured
   * background execution state; that scheduler arrives with MOB-006.
   */
  readonly replacedWindowId: string | null;
}

/** The default split position when the user first opens a split. */
const defaultSplitRatioPermille = 500;

export class PhoneForegroundController {
  #primaryWindowId: string | null = null;
  #secondaryWindowId: string | null = null;
  #splitRatioPermille: number | null = null;
  #focusedPane: PhonePane = 'primary';

  /** Loads the persisted foreground state of a layout. */
  public restore(foreground: PhoneForeground): void {
    this.#primaryWindowId = foreground.primaryWindowId;
    this.#secondaryWindowId = foreground.secondaryWindowId;
    this.#splitRatioPermille = foreground.secondaryWindowId === null
      ? null
      : clampSplitRatioPermille(foreground.splitRatioPermille ?? defaultSplitRatioPermille);
    this.#focusedPane = 'primary';
  }

  public state(): PhoneForeground {
    return {
      primaryWindowId: this.#primaryWindowId,
      secondaryWindowId: this.#secondaryWindowId,
      splitRatioPermille: this.#splitRatioPermille,
    };
  }

  public get focusedPane(): PhonePane {
    return this.#secondaryWindowId === null ? 'primary' : this.#focusedPane;
  }

  /** The window currently in the focused pane. */
  public get focusedWindowId(): string | null {
    return this.focusedPane === 'secondary' ? this.#secondaryWindowId : this.#primaryWindowId;
  }

  /**
   * Shows a window in the foreground, replacing whatever the focused pane held.
   *
   * This is what opening an application does. In single mode it replaces the visible
   * stage; in split mode it replaces exactly one pane and leaves the other alone.
   */
  public show(windowId: string): PhoneForegroundChange {
    requireWindowId(windowId);

    if (windowId === this.#primaryWindowId) {
      this.#focusedPane = 'primary';
      return this.#unchanged();
    }

    if (windowId === this.#secondaryWindowId) {
      this.#focusedPane = 'secondary';
      return this.#unchanged();
    }

    if (this.focusedPane === 'secondary') {
      const replaced = this.#secondaryWindowId;
      this.#secondaryWindowId = windowId;
      return { foreground: this.state(), replacedWindowId: replaced };
    }

    const replaced = this.#primaryWindowId;
    this.#primaryWindowId = windowId;
    this.#focusedPane = 'primary';
    return { foreground: this.state(), replacedWindowId: replaced };
  }

  /**
   * Puts a window in the second pane, which is the only way a split begins.
   *
   * Split is always an explicit user action: dragging an app from the task switcher into
   * the secondary slot, or choosing "Open in split". Nothing infers it.
   */
  public showInSplit(windowId: string): PhoneForegroundChange {
    requireWindowId(windowId);

    if (this.#primaryWindowId === null) {
      // There is nothing to split beside, so the window simply becomes the stage.
      return this.show(windowId);
    }

    if (windowId === this.#primaryWindowId) {
      this.#focusedPane = 'primary';
      return this.#unchanged();
    }

    const replaced = this.#secondaryWindowId;
    this.#secondaryWindowId = windowId;
    this.#splitRatioPermille = clampSplitRatioPermille(
      this.#splitRatioPermille ?? defaultSplitRatioPermille,
    );
    this.#focusedPane = 'secondary';
    return { foreground: this.state(), replacedWindowId: replaced };
  }

  /** Ends the split, keeping the focused window as the single foreground window. */
  public closeSplit(): PhoneForegroundChange {
    if (this.#secondaryWindowId === null) {
      return this.#unchanged();
    }

    const kept = this.focusedWindowId;
    const replaced = kept === this.#secondaryWindowId ? this.#primaryWindowId : this.#secondaryWindowId;
    this.#primaryWindowId = kept;
    this.#secondaryWindowId = null;
    this.#splitRatioPermille = null;
    this.#focusedPane = 'primary';
    return { foreground: this.state(), replacedWindowId: replaced };
  }

  /** Moves focus to one pane. Focus belongs to exactly one pane at a time. */
  public focus(pane: PhonePane): void {
    if (pane === 'secondary' && this.#secondaryWindowId === null) {
      throw new PhoneForegroundError(
        'desktop.phone_foreground_limit_exceeded',
        'There is no second pane to focus.');
    }
    this.#focusedPane = pane;
  }

  /** Moves focus to the pane holding a window, if it is in the foreground. */
  public focusWindow(windowId: string): boolean {
    if (windowId === this.#secondaryWindowId) {
      this.#focusedPane = 'secondary';
      return true;
    }
    if (windowId === this.#primaryWindowId) {
      this.#focusedPane = 'primary';
      return true;
    }
    return false;
  }

  /** Stores a divider position. Only positions inside the documented range persist. */
  public setSplitRatio(permille: number): PhoneForegroundChange {
    if (this.#secondaryWindowId === null) {
      throw new PhoneForegroundError(
        'desktop.phone_foreground_limit_exceeded',
        'There is no divider to move without a second pane.');
    }

    this.#splitRatioPermille = clampSplitRatioPermille(permille);
    return this.#unchanged();
  }

  /** Reacts to a window that is no longer open. */
  public closed(windowId: string): PhoneForegroundChange {
    if (windowId === this.#secondaryWindowId) {
      this.#secondaryWindowId = null;
      this.#splitRatioPermille = null;
      this.#focusedPane = 'primary';
      return this.#unchanged();
    }

    if (windowId !== this.#primaryWindowId) {
      return this.#unchanged();
    }

    // The surviving split window becomes the single foreground window rather than
    // disappearing with the one that was closed.
    this.#primaryWindowId = this.#secondaryWindowId;
    this.#secondaryWindowId = null;
    this.#splitRatioPermille = null;
    this.#focusedPane = 'primary';
    return this.#unchanged();
  }

  #unchanged(): PhoneForegroundChange {
    return { foreground: this.state(), replacedWindowId: null };
  }
}

export class PhoneForegroundError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = 'PhoneForegroundError';
    this.code = code;
  }
}

function requireWindowId(windowId: string): void {
  if (windowId.trim().length === 0) {
    throw new PhoneForegroundError('desktop.window_missing', 'A window identity is required.');
  }
}
