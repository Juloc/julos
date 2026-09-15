// MOB-001: the package Surface lifecycle contract from docs/MOBILE_PWA.md sections 9 to 11.
//
// Window presentation, Surface execution and runtime Session state are three independent
// lifecycles. This module describes exactly one of them — Surface execution — and
// deliberately contains no window geometry and no session identity, so the three cannot be
// conflated in a contract. MOB-006 implements the host that drives it.

import type { WorkspaceClass } from './workspace-contract.js';

/** Frontend-surface execution state. */
export type SurfaceExecutionState =
  | 'foreground-focused'
  | 'foreground-visible'
  | 'background-active'
  | 'suspended'
  | 'faulted'
  | 'terminated';

/** Why the Shell is driving a transition. */
export type SurfaceReason =
  | 'window-opened'
  | 'presentation-changed'
  | 'window-backgrounded'
  | 'workspace-changed'
  | 'user-requested'
  | 'page-hidden'
  | 'window-closed'
  | 'shell-dispose';

/** Resolved background behaviour for one application in one workspace. */
export type BackgroundMode = 'suspend' | 'keep-surface-active';

/** Result of offering Back to a Surface. */
export type BackOutcome = 'handled' | 'not-handled';

/** Visible presentation of a Surface. A Surface never receives window geometry it does not own. */
export type SurfacePresentation = 'focused' | 'visible';

/** Every Surface execution state. */
export const surfaceExecutionStates: readonly SurfaceExecutionState[] = Object.freeze([
  'foreground-focused',
  'foreground-visible',
  'background-active',
  'suspended',
  'faulted',
  'terminated',
]);

/** Deadlines the Shell aborts on, from section 10. */
export const surfaceDeadlines = Object.freeze({
  /** activate, deactivate, suspend, resume and dispose. */
  lifecycleMs: 2000,
  /** handleBack only. */
  backMs: 500,
});

/**
 * What a Surface is told about its placement.
 *
 * It carries no secret and no runtime credential: a Surface that needs one asks Core for a
 * scoped lease through its package, it is never handed one by the Shell.
 */
export interface SurfaceContext {
  readonly windowId: string;
  readonly workspaceClass: WorkspaceClass;
  readonly presentation: SurfacePresentation;
  readonly bounds: Readonly<{ x: number; y: number; width: number; height: number }>;
  readonly revision: number;
}

/** What a Surface is told about a Back press. Input source and sequence only. */
export interface BackContext {
  readonly source: 'system' | 'browser' | 'pointer' | 'keyboard';
  /** Monotonically increasing Shell navigation sequence that was departed. */
  readonly sequence: number;
}

/** The host-side interface a package Surface implements. */
export interface SurfaceHost {
  activate(context: SurfaceContext, signal: AbortSignal): Promise<void>;
  deactivate(reason: SurfaceReason, signal: AbortSignal): Promise<void>;
  suspend(reason: SurfaceReason, signal: AbortSignal): Promise<void>;
  resume(context: SurfaceContext, signal: AbortSignal): Promise<void>;
  handleBack(context: BackContext, signal: AbortSignal): Promise<BackOutcome>;
  dispose(reason: SurfaceReason, signal: AbortSignal): Promise<void>;
}

/** The `Surface` object a mobile-capable application declares in its package manifest. */
export interface SurfaceManifestDeclaration {
  readonly ContractVersion: string;
  readonly SupportedBackgroundModes: readonly BackgroundMode[];
  readonly HandlesBack: boolean;
}

/** Surface contract major version this Shell implements. */
export const surfaceContractMajorVersion = 1;

/** Stable Surface errors from section 16. */
export class SurfaceContractError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = 'SurfaceContractError';
    this.code = code;
  }
}

/**
 * Maps a resolved background mode to the state a backgrounded Surface reaches.
 * The mapping is exact: an application can neither request nor persist a mode.
 */
export function backgroundStateFor(mode: BackgroundMode): SurfaceExecutionState {
  switch (mode) {
    case 'suspend':
      return 'suspended';
    case 'keep-surface-active':
      return 'background-active';
    default:
      throw new SurfaceContractError(
        'application.background_mode_unsupported',
        `'${String(mode)}' is not a background mode.`,
      );
  }
}

/**
 * Resolves the background mode for one application, refusing a mode its manifest does not
 * support rather than silently falling back.
 */
export function resolveBackgroundMode(
  declaration: SurfaceManifestDeclaration,
  requested: BackgroundMode,
): BackgroundMode {
  if (!declaration.SupportedBackgroundModes.includes(requested)) {
    throw new SurfaceContractError(
      'application.background_mode_unsupported',
      `The application does not support background mode '${requested}'.`,
    );
  }
  return requested;
}

/** Validates the manifest declaration, including the no-silent-fallback major-version rule. */
export function validateSurfaceDeclaration(
  declaration: SurfaceManifestDeclaration,
): readonly string[] {
  const errors: string[] = [];
  const version = /^(\d+)\.(\d+)\.(\d+)$/.exec(declaration.ContractVersion ?? '');

  if (version === null) {
    errors.push('Surface.ContractVersion must be a three-part semantic version.');
  } else if (Number(version[1]) !== surfaceContractMajorVersion) {
    errors.push(
      `Surface.ContractVersion major ${version[1]} is unsupported; this Shell implements ${surfaceContractMajorVersion}.`,
    );
  }

  if (!Array.isArray(declaration.SupportedBackgroundModes)
    || declaration.SupportedBackgroundModes.length === 0) {
    errors.push('Surface.SupportedBackgroundModes must list at least one mode.');
  } else {
    for (const mode of declaration.SupportedBackgroundModes) {
      if (mode !== 'suspend' && mode !== 'keep-surface-active') {
        errors.push(`Surface.SupportedBackgroundModes contains unknown mode '${String(mode)}'.`);
      }
    }
    if (!declaration.SupportedBackgroundModes.includes('suspend')) {
      errors.push('Surface.SupportedBackgroundModes must include "suspend", the default Phone behaviour.');
    }
  }

  if (typeof declaration.HandlesBack !== 'boolean') {
    errors.push('Surface.HandlesBack must be a boolean.');
  }

  return errors;
}

/**
 * The calls the Shell makes to move a Surface from one state to another.
 *
 * Section 10 defines these as a closed set. Repeating a completed transition to the same
 * state is idempotent and produces no calls; a transition that is not listed is a contract
 * error rather than a best guess.
 */
export function transitionCalls(
  from: SurfaceExecutionState,
  to: SurfaceExecutionState,
): readonly (keyof SurfaceHost)[] {
  if (from === to) {
    return [];
  }

  if (from === 'terminated') {
    throw new SurfaceContractError(
      'package.surface_terminated',
      'A disposed Surface cannot transition again.',
    );
  }

  if (to === 'terminated') {
    return ['dispose'];
  }

  const foreground = to === 'foreground-focused' || to === 'foreground-visible';

  if (foreground) {
    // From suspended the Surface must re-read authoritative data before it is shown.
    if (from === 'suspended') {
      return ['resume', 'activate'];
    }
    // background-active kept its state, and a focus change inside the foreground is
    // presentation only.
    return ['activate'];
  }

  if (to === 'background-active') {
    return from === 'suspended' ? ['resume'] : ['deactivate'];
  }

  if (to === 'suspended') {
    // Losing visible presentation is deactivate; suspend follows only for `suspend` mode.
    return from === 'background-active' ? ['suspend'] : ['deactivate', 'suspend'];
  }

  if (to === 'faulted') {
    return [];
  }

  throw new SurfaceContractError(
    'package.surface_contract_unsupported',
    `No defined transition from '${from}' to '${to}'.`,
  );
}
