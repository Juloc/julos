// MOB-001: the workspace, client-device and layout-resolution contract from
// docs/MOBILE_PWA.md sections 2 to 4.
//
// This module is pure contract and pure logic. Client-device registration and layout
// persistence land with MOB-003 and MOB-004; nothing here reads a cookie, touches storage
// or talks to Server.

/** Persisted Shell presentation class. */
export type WorkspaceClass = 'phone' | 'tablet' | 'desktop-single' | 'desktop-multi';

/** Compatibility an application declares in its package manifest. */
export type ApplicationViewportClass = 'mobile' | 'tablet' | 'desktop';

/** A workspace class a client device may be pinned to. */
export type WorkspaceClassOverride = Exclude<WorkspaceClass, 'desktop-multi'>;

/** Whether a layout is shared by the user or bound to one client device. */
export type LayoutScope = 'shared' | 'device';

/** Whether a workspace restores its windows or starts empty. */
export type RestoreMode = 'resume' | 'fresh';

/** The fixed workspace-to-application mapping from section 2. */
export const workspaceApplicationViewport: Readonly<Record<WorkspaceClass, ApplicationViewportClass>> =
  Object.freeze({
    phone: 'mobile',
    tablet: 'tablet',
    'desktop-single': 'desktop',
    'desktop-multi': 'desktop',
  });

/** Every workspace class, in declaration order. */
export const workspaceClasses: readonly WorkspaceClass[] = Object.freeze([
  'phone',
  'tablet',
  'desktop-single',
  'desktop-multi',
]);

/**
 * The only inputs automatic classification is allowed to read.
 *
 * User-agent strings, device memory, CPU counts and any other hardware fingerprint are
 * deliberately absent: section 2 forbids them, so they cannot be passed in at all.
 */
export interface PresentationCapabilities {
  /** `(pointer: coarse)` — the primary pointer is imprecise. */
  readonly primaryPointerCoarse: boolean;
  /** `(any-pointer: fine)` — some precise pointer exists. */
  readonly anyPointerFine: boolean;
  /**
   * `min(screen.width, screen.height)` in CSS pixels.
   *
   * The minimum is stable across orientation, so a phone in landscape stays a phone.
   */
  readonly screenMinimumDimensionCssPx: number;
  /**
   * `document.documentElement.clientWidth` in CSS pixels.
   *
   * Deliberately not `VisualViewport`: a software keyboard must not change workspace
   * identity.
   */
  readonly layoutViewportWidthCssPx: number;
}

/** Inputs to the resolution order in section 4. */
export interface WorkspaceResolutionInput {
  readonly capabilities: PresentationCapabilities;
  /** A stored device override is authoritative over detection. */
  readonly override: WorkspaceClassOverride | null;
  /** True only when the user explicitly entered the Multi-Display controller. */
  readonly multiDisplayRequested: boolean;
  /** Active display participants; `desktop-multi` needs at least two. */
  readonly activeDisplayParticipants: number;
}

/** The resolved workspace, keeping the detected value visible for `LastDetectedWorkspaceClass`. */
export interface ResolvedWorkspace {
  readonly detected: WorkspaceClassOverride;
  readonly resolved: WorkspaceClass;
  readonly overrideApplied: boolean;
}

/** Stable workspace and client-device errors from section 16. */
export class WorkspaceContractError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = 'WorkspaceContractError';
    this.code = code;
  }
}

/** Phone threshold in CSS pixels, exclusive. */
const phoneWidthCssPx = 600;

/** Tablet threshold in CSS pixels, exclusive. */
const tabletWidthCssPx = 1024;

/**
 * Classifies a workspace from presentation capabilities alone, exactly as section 2
 * specifies. `desktop-multi` is never produced here: it is entered only through the
 * explicit Multi-Display controller.
 */
export function classifyWorkspace(capabilities: PresentationCapabilities): WorkspaceClassOverride {
  assertPositive(capabilities.screenMinimumDimensionCssPx, 'screenMinimumDimensionCssPx');
  assertPositive(capabilities.layoutViewportWidthCssPx, 'layoutViewportWidthCssPx');

  const touchOnly = capabilities.primaryPointerCoarse && !capabilities.anyPointerFine;

  if (touchOnly && capabilities.screenMinimumDimensionCssPx < phoneWidthCssPx) {
    return 'phone';
  }
  if (touchOnly) {
    return 'tablet';
  }
  if (capabilities.layoutViewportWidthCssPx < phoneWidthCssPx) {
    return 'phone';
  }
  if (capabilities.layoutViewportWidthCssPx < tabletWidthCssPx) {
    return 'tablet';
  }
  return 'desktop-single';
}

/** Applies the device override and Multi-Display rules on top of classification. */
export function resolveWorkspace(input: WorkspaceResolutionInput): ResolvedWorkspace {
  const detected = classifyWorkspace(input.capabilities);

  if (input.override !== null && !isWorkspaceClassOverride(input.override)) {
    throw new WorkspaceContractError(
      'client_device.workspace_preference_invalid',
      `'${String(input.override)}' is not a workspace class a device may be pinned to.`,
    );
  }

  const base = input.override ?? detected;
  const multiDisplay = input.multiDisplayRequested && input.activeDisplayParticipants >= 2;

  return {
    detected,
    resolved: multiDisplay ? 'desktop-multi' : base,
    overrideApplied: input.override !== null,
  };
}

/** A device's stored preference for one workspace class. */
export interface DeviceWorkspacePreference {
  readonly workspaceClass: WorkspaceClass;
  readonly layoutScope: LayoutScope;
  readonly restoreMode: RestoreMode;
}

/** Which layout a resolved workspace loads, and whether it may be written back. */
export interface LayoutSelection {
  readonly workspaceClass: WorkspaceClass;
  readonly scope: LayoutScope;
  /** `fresh` starts with no restored windows and never persists window state. */
  readonly restoresWindows: boolean;
  readonly persistsWindowState: boolean;
}

/**
 * Steps 5 to 9 of section 4. Resolution is deterministic and never writes geometry into a
 * workspace class other than the one it resolved.
 */
export function selectLayout(
  workspaceClass: WorkspaceClass,
  preference: DeviceWorkspacePreference | null,
): LayoutSelection {
  if (!workspaceClasses.includes(workspaceClass)) {
    throw new WorkspaceContractError(
      'desktop.workspace_class_invalid',
      `'${String(workspaceClass)}' is not a workspace class.`,
    );
  }

  if (preference !== null && preference.workspaceClass !== workspaceClass) {
    throw new WorkspaceContractError(
      'client_device.workspace_preference_invalid',
      'A device preference belongs to exactly one workspace class.',
    );
  }

  const scope = preference?.layoutScope ?? 'shared';
  const fresh = preference?.restoreMode === 'fresh';

  return {
    workspaceClass,
    scope,
    restoresWindows: !fresh,
    persistsWindowState: !fresh,
  };
}

/** Reports whether a value is a workspace class a device may be pinned to. */
export function isWorkspaceClassOverride(value: unknown): value is WorkspaceClassOverride {
  return value === 'phone' || value === 'tablet' || value === 'desktop-single';
}

/** The application viewport class a workspace presents. */
export function applicationViewportFor(workspaceClass: WorkspaceClass): ApplicationViewportClass {
  const viewport = workspaceApplicationViewport[workspaceClass];
  if (viewport === undefined) {
    throw new WorkspaceContractError(
      'desktop.workspace_class_invalid',
      `'${String(workspaceClass)}' is not a workspace class.`,
    );
  }
  return viewport;
}

function assertPositive(value: number, name: string): void {
  if (!Number.isFinite(value) || value <= 0) {
    throw new WorkspaceContractError(
      'desktop.workspace_class_invalid',
      `${name} must be a positive number of CSS pixels.`,
    );
  }
}
