// MOB-006: the background-execution preference client from docs/MOBILE_PWA.md section 11.
//
// The preference belongs to the user, not to the application. This client only carries the
// user's choice to Server, which refuses a mode the application's manifest never declared;
// nothing here can widen what an application is allowed to do.

import { JulOsApiClient } from './api-client.js';
import type { BackgroundMode } from './surface-contract.js';
import type { LayoutScope, WorkspaceClass } from './workspace-contract.js';

/** What happens to one application's surface when it leaves the visible foreground. */
export interface ExecutionPreference {
  readonly applicationDefinitionId: string;
  readonly workspaceClass: WorkspaceClass;
  readonly layoutScope: LayoutScope;
  readonly backgroundMode: BackgroundMode;
  /** Whether the application declares that it can stay active at all. */
  readonly supportsKeepSurfaceActive: boolean;
  readonly revision: number;
}

interface AntiforgeryToken {
  readonly headerName: string;
  readonly token: string;
}

/**
 * Reads and writes the background-execution preference.
 *
 * Resolved values are cached per application and workspace class so that opening a window
 * does not re-ask for something the Shell already knows; a write replaces the cache entry
 * with the authoritative answer rather than assuming the write landed as sent.
 */
export class ExecutionPreferenceClient {
  readonly #api: JulOsApiClient;
  readonly #cache = new Map<string, ExecutionPreference>();
  #antiforgery: AntiforgeryToken | null = null;

  public constructor(fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.#api = new JulOsApiClient(fetchImplementation);
  }

  /** The preference already known for an application, without asking Server. */
  public cached(applicationDefinitionId: string, workspaceClass: WorkspaceClass): ExecutionPreference | null {
    return this.#cache.get(key(applicationDefinitionId, workspaceClass)) ?? null;
  }

  public async read(
    applicationDefinitionId: string,
    workspaceClass: WorkspaceClass,
  ): Promise<ExecutionPreference> {
    const resolved = await this.#api.get<ExecutionPreference>(
      path(applicationDefinitionId, workspaceClass),
    );
    this.#cache.set(key(applicationDefinitionId, workspaceClass), resolved);
    return resolved;
  }

  public async write(
    applicationDefinitionId: string,
    workspaceClass: WorkspaceClass,
    backgroundMode: BackgroundMode,
    expectedRevision: number | null,
  ): Promise<ExecutionPreference> {
    const antiforgery = await this.#readAntiforgery();
    const stored = await this.#api.requestJson<ExecutionPreference>(
      path(applicationDefinitionId, workspaceClass),
      {
        method: 'PUT',
        // A revision of zero means nothing is stored yet, which the API expresses as null.
        body: { backgroundMode, expectedRevision: expectedRevision === 0 ? null : expectedRevision },
        headers: { [antiforgery.headerName]: antiforgery.token },
      },
    );
    this.#cache.set(key(applicationDefinitionId, workspaceClass), stored);
    return stored;
  }

  /** Forgets everything, for a Shell that is tearing down. */
  public clear(): void {
    this.#cache.clear();
    this.#antiforgery = null;
  }

  async #readAntiforgery(): Promise<AntiforgeryToken> {
    this.#antiforgery ??= await this.#api.get<AntiforgeryToken>('/api/v1/auth/antiforgery');
    return this.#antiforgery;
  }
}

function key(applicationDefinitionId: string, workspaceClass: WorkspaceClass): string {
  return `${applicationDefinitionId}:${workspaceClass}`;
}

function path(applicationDefinitionId: string, workspaceClass: WorkspaceClass): string {
  return `/api/v1/application-execution-preferences/${encodeURIComponent(applicationDefinitionId)}/current`
    + `?workspaceClass=${encodeURIComponent(workspaceClass)}`;
}
