// MOB-001: the Shell-owned Back navigation contract from docs/MOBILE_PWA.md section 13.
//
// MOB-009 implements the controller. This module fixes the layer order, the history entry
// shape and the reconciliation rule that unwinds a multi-entry browser jump, so the
// implementation cannot quietly invent a different order.

/** Back-consumable layers, in the order the dispatcher offers them. */
export type BackLayerKind =
  | 'overlay'
  | 'application'
  | 'detail'
  | 'task-switcher'
  | 'root';

/**
 * The dispatcher order from section 13.
 *
 * 1. dismiss the top dialog, menu or transient overlay;
 * 2. invoke the active application's registered `handleBack`;
 * 3. collapse app detail or Phone split focus/history;
 * 4. leave the task switcher or active app view for the workspace;
 * 5. at the JulOS root, allow the browser or operating system to leave.
 */
export const backDispatcherOrder: readonly BackLayerKind[] = Object.freeze([
  'overlay',
  'application',
  'detail',
  'task-switcher',
  'root',
]);

/** Supported Back inputs. All of them enter the one controller. */
export type BackInputSource = 'system' | 'browser' | 'pointer' | 'keyboard';

/**
 * A JulOS history entry.
 *
 * It carries no URL credential, secret, package state or runtime descriptor — only an
 * opaque non-secret layer token.
 */
export interface ShellHistoryEntry {
  /** Marks the entry as JulOS-owned; the value is the contract version. */
  readonly julos: 1;
  /** Random per-page value; a stale epoch means reload or BFCache restore. */
  readonly epoch: string;
  /** Monotonically increasing within one epoch; the root entry is zero. */
  readonly sequence: number;
  readonly kind: BackLayerKind;
  /** Opaque non-secret token identifying the layer that pushed the entry. */
  readonly token: string | null;
}

/** Stable navigation errors. */
export class ShellNavigationContractError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = 'ShellNavigationContractError';
    this.code = code;
  }
}

/** The root entry the Shell writes with `replaceState` on boot. No guard entry is pushed. */
export function rootHistoryEntry(epoch: string): ShellHistoryEntry {
  if (epoch.length === 0) {
    throw new ShellNavigationContractError(
      'desktop.workspace_class_invalid',
      'A history epoch must not be empty.',
    );
  }
  return { julos: 1, epoch, sequence: 0, kind: 'root', token: null };
}

/** Reports whether a value is a JulOS history entry from the current page epoch. */
export function isCurrentEntry(value: unknown, epoch: string): value is ShellHistoryEntry {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const entry = value as Partial<ShellHistoryEntry>;
  return entry.julos === 1
    && entry.epoch === epoch
    && typeof entry.sequence === 'number'
    && Number.isInteger(entry.sequence)
    && entry.sequence >= 0;
}

/**
 * The layers a `popstate` departed, newest first.
 *
 * `popstate` is an already-completed history move, not a cancellable event, so a browser
 * jump across several entries unwinds every departed JulOS layer once rather than only the
 * topmost one.
 */
export function departedLayers(
  stack: readonly ShellHistoryEntry[],
  targetSequence: number,
): readonly ShellHistoryEntry[] {
  if (!Number.isInteger(targetSequence) || targetSequence < 0) {
    throw new ShellNavigationContractError(
      'desktop.workspace_class_invalid',
      'A target sequence must be a non-negative integer.',
    );
  }

  return stack
    .filter((entry) => entry.sequence > targetSequence)
    .slice()
    .sort((left, right) => right.sequence - left.sequence);
}

/**
 * Whether Back at this point leaves JulOS entirely.
 *
 * At sequence zero there is no guard entry, so Back reaches the previous external history
 * entry or lets a standalone PWA close. No handler may restore a sentinel or trap history.
 */
export function leavesJulOs(currentSequence: number): boolean {
  return currentSequence <= 0;
}

/**
 * Forward navigation truncates abandoned layers exactly as browser history truncates
 * forward entries.
 */
export function truncateForward(
  stack: readonly ShellHistoryEntry[],
  currentSequence: number,
): readonly ShellHistoryEntry[] {
  return stack.filter((entry) => entry.sequence <= currentSequence);
}
