// MOB-005: where every window goes in the current workspace, from
// docs/MOBILE_PWA.md sections 6, 7 and 8.
//
// This module is pure. It reads a window list, a usable area and the stored phone
// foreground state, and returns placements. It never touches the DOM, never persists and
// never decides which workspace class is current — that identity comes from MOB-004.

import type { WorkspaceClass } from './workspace-contract.js';
import type { DesktopWindowSnapshot, UsableArea, WindowBounds } from './window-store.js';

/** How the Shell arranges the windows it is showing. */
export type StagePresentation = 'windowed' | 'tiled' | 'phone-empty' | 'phone-single' | 'phone-split';

/** Which half of a phone split a window occupies. */
export type PhonePane = 'primary' | 'secondary';

/** Portrait splits top and bottom; landscape splits left and right. */
export type StageOrientation = 'portrait' | 'landscape';

/** One window and where it is shown. */
export interface StagePlacement {
  readonly windowId: string;
  readonly bounds: WindowBounds;
  /** The phone pane this window occupies, or null outside a phone workspace. */
  readonly pane: PhonePane | null;
}

/** The complete arrangement of one workspace. */
export interface WorkspaceStage {
  readonly presentation: StagePresentation;
  readonly orientation: StageOrientation;
  /** Windows that are on screen, in the order they should be rendered. */
  readonly placements: readonly StagePlacement[];
  /** Every open window, nearest the front first. All of them stay reachable here. */
  readonly taskWindows: readonly DesktopWindowSnapshot[];
  readonly activeWindowId: string | null;
  /** The draggable divider between the two phone panes, or null. */
  readonly divider: WindowBounds | null;
}

/** The persisted phone foreground state a stage renders. */
export interface PhoneForeground {
  readonly primaryWindowId: string | null;
  readonly secondaryWindowId: string | null;
  readonly splitRatioPermille: number | null;
}

export interface WorkspaceStageInput {
  readonly workspaceClass: WorkspaceClass;
  readonly windows: readonly DesktopWindowSnapshot[];
  readonly area: UsableArea;
  readonly activeWindowId: string | null;
  readonly phone?: PhoneForeground;
  /**
   * Whether a tablet places windows freely instead of tiling them.
   *
   * Section 7 enables free placement when there is enough area and a precise pointer, or
   * when the user opts in. Both of those are decided by the caller, because neither is a
   * property of the window list.
   */
  readonly freeWindowPlacement?: boolean;
  /** The display slot this participant renders, for `desktop-multi` only. */
  readonly displaySlot?: number;
  /** The display slots currently active, for `desktop-multi` only. */
  readonly activeDisplaySlots?: readonly number[];
  /** Stored slot per window, for `desktop-multi` only. */
  readonly windowDisplaySlots?: ReadonlyMap<string, number>;
}

/** The lowest split position the divider persists, in permille. */
export const minimumSplitRatioPermille = 250;

/** The highest split position the divider persists, in permille. */
export const maximumSplitRatioPermille = 750;

/** Thickness of the divider between two phone panes, in CSS pixels. */
export const phoneDividerThicknessCssPx = 8;

/** The smallest usable pane edge; below this a split would not be operable. */
const minimumPaneEdgeCssPx = 96;

/** How many windows a tablet tiles before the rest stay in the task switcher. */
export const maximumTiledWindows = 4;

export class WorkspaceStageError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = 'WorkspaceStageError';
    this.code = code;
  }
}

/** Computes the arrangement of one workspace. */
export function deriveWorkspaceStage(input: WorkspaceStageInput): WorkspaceStage {
  assertArea(input.area);

  const taskWindows = [...input.windows].sort((left, right) => right.zIndex - left.zIndex);
  const orientation: StageOrientation = input.area.width >= input.area.height ? 'landscape' : 'portrait';

  if (input.workspaceClass === 'phone') {
    return phoneStage(input, taskWindows, orientation);
  }

  if (input.workspaceClass === 'tablet' && input.freeWindowPlacement !== true) {
    return tiledStage(input, taskWindows, orientation);
  }

  return windowedStage(input, taskWindows, orientation);
}

/**
 * Clamps a split position into the range the divider persists.
 *
 * A drag produces a continuous position; only positions inside the documented range are
 * ever stored, so the two panes both stay operable.
 */
export function clampSplitRatioPermille(permille: number): number {
  if (!Number.isFinite(permille)) {
    throw new WorkspaceStageError('desktop.layout_invalid', 'A split position must be a number.');
  }
  return Math.min(
    maximumSplitRatioPermille,
    Math.max(minimumSplitRatioPermille, Math.round(permille)),
  );
}

/**
 * Resolves which display each window is rendered by.
 *
 * A window whose stored slot is not currently active is recovered onto the earliest
 * surviving display. Its stored slot is deliberately not rewritten: the display may come
 * back, and a temporary absence must not silently move the window for good.
 */
export function resolveDisplaySlots(
  windowDisplaySlots: ReadonlyMap<string, number>,
  activeDisplaySlots: readonly number[],
): ReadonlyMap<string, number> {
  const active = [...new Set(activeDisplaySlots)].sort((left, right) => left - right);
  if (active.length === 0) {
    throw new WorkspaceStageError(
      'desktop.layout_invalid',
      'A multi-display workspace has at least one active display.');
  }

  const fallback = active[0] as number;
  const resolved = new Map<string, number>();
  for (const [windowId, slot] of windowDisplaySlots) {
    resolved.set(windowId, active.includes(slot) ? slot : fallback);
  }
  return resolved;
}

function phoneStage(
  input: WorkspaceStageInput,
  taskWindows: readonly DesktopWindowSnapshot[],
  orientation: StageOrientation,
): WorkspaceStage {
  const foreground = input.phone ?? { primaryWindowId: null, secondaryWindowId: null, splitRatioPermille: null };
  const eligible = new Set(
    taskWindows.filter((window) => window.state !== 'minimized').map((window) => window.id),
  );

  // A foreground identifier that no longer names an open, visible window is ignored rather
  // than rendered as an empty pane.
  const primary = foreground.primaryWindowId !== null && eligible.has(foreground.primaryWindowId)
    ? foreground.primaryWindowId
    : null;
  const secondary = foreground.secondaryWindowId !== null
    && foreground.secondaryWindowId !== primary
    && eligible.has(foreground.secondaryWindowId)
    ? foreground.secondaryWindowId
    : null;

  if (primary === null) {
    return {
      presentation: 'phone-empty',
      orientation,
      placements: [],
      taskWindows,
      activeWindowId: null,
      divider: null,
    };
  }

  if (secondary === null || !splitFits(input.area, orientation)) {
    // A screen too small to show two operable panes presents the primary window alone.
    // The stored split is untouched, so rotating back restores it.
    return {
      presentation: 'phone-single',
      orientation,
      placements: [{ windowId: primary, bounds: { ...input.area }, pane: 'primary' }],
      taskWindows,
      activeWindowId: primary,
      divider: null,
    };
  }

  const ratio = clampSplitRatioPermille(foreground.splitRatioPermille ?? 500);
  const split = splitGeometry(input.area, orientation, ratio);
  const focused = input.activeWindowId === secondary ? secondary : primary;

  return {
    presentation: 'phone-split',
    orientation,
    placements: [
      { windowId: primary, bounds: split.primary, pane: 'primary' },
      { windowId: secondary, bounds: split.secondary, pane: 'secondary' },
    ],
    taskWindows,
    activeWindowId: focused,
    divider: split.divider,
  };
}

interface SplitGeometry {
  readonly primary: WindowBounds;
  readonly secondary: WindowBounds;
  readonly divider: WindowBounds;
}

function splitGeometry(
  area: UsableArea,
  orientation: StageOrientation,
  ratioPermille: number,
): SplitGeometry {
  const half = phoneDividerThicknessCssPx / 2;

  if (orientation === 'portrait') {
    const primaryHeight = Math.round((area.height * ratioPermille) / 1000) - half;
    const dividerTop = area.y + primaryHeight;
    return {
      primary: { x: area.x, y: area.y, width: area.width, height: primaryHeight },
      secondary: {
        x: area.x,
        y: dividerTop + phoneDividerThicknessCssPx,
        width: area.width,
        height: area.height - primaryHeight - phoneDividerThicknessCssPx,
      },
      divider: { x: area.x, y: dividerTop, width: area.width, height: phoneDividerThicknessCssPx },
    };
  }

  const primaryWidth = Math.round((area.width * ratioPermille) / 1000) - half;
  const dividerLeft = area.x + primaryWidth;
  return {
    primary: { x: area.x, y: area.y, width: primaryWidth, height: area.height },
    secondary: {
      x: dividerLeft + phoneDividerThicknessCssPx,
      y: area.y,
      width: area.width - primaryWidth - phoneDividerThicknessCssPx,
      height: area.height,
    },
    divider: { x: dividerLeft, y: area.y, width: phoneDividerThicknessCssPx, height: area.height },
  };
}

function splitFits(area: UsableArea, orientation: StageOrientation): boolean {
  const edge = orientation === 'portrait' ? area.height : area.width;
  const smallestPane = Math.round((edge * minimumSplitRatioPermille) / 1000);
  return smallestPane >= minimumPaneEdgeCssPx;
}

/**
 * The tablet default: visible windows are tiled without overlap.
 *
 * Up to four windows are tiled, splitting along the longer edge first so the tiles stay
 * as square as the area allows. Anything beyond that stays in the task switcher rather
 * than being tiled into a strip too narrow to use.
 */
function tiledStage(
  input: WorkspaceStageInput,
  taskWindows: readonly DesktopWindowSnapshot[],
  orientation: StageOrientation,
): WorkspaceStage {
  const visible = taskWindows
    .filter((window) => window.state !== 'minimized')
    .slice(0, maximumTiledWindows);

  if (visible.length === 0) {
    return {
      presentation: 'tiled',
      orientation,
      placements: [],
      taskWindows,
      activeWindowId: null,
      divider: null,
    };
  }

  const tiles = tileBounds(input.area, orientation, visible.length);
  return {
    presentation: 'tiled',
    orientation,
    placements: visible.map((window, index) => ({
      windowId: window.id,
      bounds: tiles[index] as WindowBounds,
      pane: null,
    })),
    taskWindows,
    activeWindowId: activeOf(input, visible),
    divider: null,
  };
}

function tileBounds(
  area: UsableArea,
  orientation: StageOrientation,
  count: number,
): readonly WindowBounds[] {
  if (count === 1) {
    return [{ ...area }];
  }

  const [first, second] = halve(area, orientation);
  const inner: StageOrientation = orientation === 'landscape' ? 'portrait' : 'landscape';

  if (count === 2) {
    return [first, second];
  }

  if (count === 3) {
    const [top, bottom] = halve(second, inner);
    return [first, top, bottom];
  }

  const [firstTop, firstBottom] = halve(first, inner);
  const [secondTop, secondBottom] = halve(second, inner);
  return [firstTop, firstBottom, secondTop, secondBottom];
}

function halve(area: WindowBounds, orientation: StageOrientation): readonly [WindowBounds, WindowBounds] {
  if (orientation === 'landscape') {
    const width = Math.floor(area.width / 2);
    return [
      { x: area.x, y: area.y, width, height: area.height },
      { x: area.x + width, y: area.y, width: area.width - width, height: area.height },
    ];
  }

  const height = Math.floor(area.height / 2);
  return [
    { x: area.x, y: area.y, width: area.width, height },
    { x: area.x, y: area.y + height, width: area.width, height: area.height - height },
  ];
}

function windowedStage(
  input: WorkspaceStageInput,
  taskWindows: readonly DesktopWindowSnapshot[],
  orientation: StageOrientation,
): WorkspaceStage {
  const owned = input.workspaceClass === 'desktop-multi'
    ? windowsOnThisDisplay(input, taskWindows)
    : taskWindows;
  const visible = owned.filter((window) => window.state !== 'minimized');

  return {
    presentation: 'windowed',
    orientation,
    placements: visible.map((window) => ({
      windowId: window.id,
      bounds: { ...window.bounds },
      pane: null,
    })),
    taskWindows,
    activeWindowId: activeOf(input, visible),
    divider: null,
  };
}

function windowsOnThisDisplay(
  input: WorkspaceStageInput,
  taskWindows: readonly DesktopWindowSnapshot[],
): readonly DesktopWindowSnapshot[] {
  const slots = input.windowDisplaySlots;
  const active = input.activeDisplaySlots;
  if (slots === undefined || active === undefined || input.displaySlot === undefined) {
    return taskWindows;
  }

  const resolved = resolveDisplaySlots(slots, active);
  return taskWindows.filter((window) => (resolved.get(window.id) ?? active[0]) === input.displaySlot);
}

function activeOf(
  input: WorkspaceStageInput,
  visible: readonly DesktopWindowSnapshot[],
): string | null {
  const requested = input.activeWindowId === null
    ? null
    : visible.find((window) => window.id === input.activeWindowId)?.id ?? null;
  return requested ?? visible[0]?.id ?? null;
}

function assertArea(area: UsableArea): void {
  if (!Number.isFinite(area.width) || !Number.isFinite(area.height) || area.width <= 0 || area.height <= 0) {
    throw new WorkspaceStageError(
      'desktop.viewport_invalid',
      'A workspace stage needs a usable area with positive width and height.');
  }
}
