import {
  WindowLaunchCoordinator,
  type ApplicationInstancePolicy,
  type WindowLaunchResult,
} from './window-taskbar.js';
import type { UsableArea, WindowBounds } from './window-store.js';

export type LauncherEntryKind = 'application' | 'target' | 'command';

export interface LauncherApplication {
  readonly applicationId: string;
  readonly title: string;
  readonly description: string;
  readonly keywords: readonly string[];
  readonly instancePolicy: ApplicationInstancePolicy;
  readonly defaultBounds: WindowBounds;
  readonly requiredPermissions: readonly string[];
}

export interface LauncherTarget {
  readonly targetId: string;
  readonly applicationId: string;
  readonly title: string;
  readonly description: string;
  readonly keywords: readonly string[];
  readonly state: 'approved' | 'discovered' | 'ignored';
  readonly requiredPermissions: readonly string[];
}

export interface LauncherCommand {
  readonly commandId: string;
  readonly title: string;
  readonly description: string;
  readonly keywords: readonly string[];
  readonly requiredPermissions: readonly string[];
  readonly execute: () => void | Promise<void>;
}

export interface LauncherSearchResult {
  readonly kind: LauncherEntryKind;
  readonly id: string;
  readonly title: string;
  readonly description: string;
  readonly applicationId: string | null;
  readonly targetId: string | null;
  readonly score: number;
}

export interface LauncherCatalog {
  readonly applications: readonly LauncherApplication[];
  readonly targets: readonly LauncherTarget[];
  readonly commands: readonly LauncherCommand[];
}

interface IndexedEntry extends LauncherSearchResult {
  readonly normalizedTitle: string;
  readonly normalizedDescription: string;
  readonly tokens: readonly string[];
  readonly requiredPermissions: readonly string[];
}

/**
 * Immutable, permission-filtered launcher search index. Search never mutates the catalog
 * and command execution resolves by stable identity rather than rendered text.
 */
export class LauncherIndex {
  readonly #applications = new Map<string, LauncherApplication>();
  readonly #targets = new Map<string, LauncherTarget>();
  readonly #commands = new Map<string, LauncherCommand>();
  readonly #entries: readonly IndexedEntry[];
  readonly #permissions: ReadonlySet<string>;

  public constructor(catalog: LauncherCatalog, grantedPermissions: Iterable<string>) {
    this.#permissions = new Set(grantedPermissions);

    for (const application of catalog.applications) {
      ensureUnique(this.#applications, application.applicationId, 'application');
      this.#applications.set(application.applicationId, application);
    }

    for (const target of catalog.targets) {
      ensureUnique(this.#targets, target.targetId, 'target');
      if (!this.#applications.has(target.applicationId)) {
        throw new LauncherError(
          'launcher.application_missing',
          `Target '${target.targetId}' references an unknown application.`,
        );
      }
      this.#targets.set(target.targetId, target);
    }

    for (const command of catalog.commands) {
      ensureUnique(this.#commands, command.commandId, 'command');
      this.#commands.set(command.commandId, command);
    }

    this.#entries = [
      ...catalog.applications.map((application) => indexApplication(application)),
      ...catalog.targets
        .filter((target) => target.state === 'approved')
        .map((target) => indexTarget(target)),
      ...catalog.commands.map((command) => indexCommand(command)),
    ];
  }

  public search(query: string, limit = 40): readonly LauncherSearchResult[] {
    if (!Number.isInteger(limit) || limit < 1 || limit > 200) {
      throw new LauncherError('launcher.limit_invalid', 'Search limit must be between 1 and 200.');
    }

    const normalizedQuery = normalize(query);
    const queryTokens = tokenize(normalizedQuery);
    return this.#entries
      .filter((entry) => hasPermissions(this.#permissions, entry.requiredPermissions))
      .map((entry) => ({ entry, score: score(entry, normalizedQuery, queryTokens) }))
      .filter((candidate) => normalizedQuery.length === 0 || candidate.score > 0)
      .sort((left, right) =>
        right.score - left.score
        || kindOrder(left.entry.kind) - kindOrder(right.entry.kind)
        || left.entry.title.localeCompare(right.entry.title),
      )
      .slice(0, limit)
      .map(({ entry, score: resultScore }) => ({
        kind: entry.kind,
        id: entry.id,
        title: entry.title,
        description: entry.description,
        applicationId: entry.applicationId,
        targetId: entry.targetId,
        score: resultScore,
      }));
  }

  public launch(
    result: LauncherSearchResult,
    coordinator: WindowLaunchCoordinator,
    usableArea: UsableArea,
  ): WindowLaunchResult {
    if (result.kind === 'command') {
      throw new LauncherError('launcher.not_launchable', 'Commands execute through executeCommand.');
    }

    const applicationId = result.applicationId ?? result.id;
    const application = this.#applications.get(applicationId);
    if (application === undefined || !hasPermissions(this.#permissions, application.requiredPermissions)) {
      throw new LauncherError('launcher.not_authorized', 'The application is not available to this user.');
    }

    const target = result.targetId === null ? null : this.#targets.get(result.targetId);
    if (target !== null && target !== undefined) {
      if (target.state !== 'approved' || !hasPermissions(this.#permissions, target.requiredPermissions)) {
        throw new LauncherError('launcher.target_not_approved', 'The launch target is not approved.');
      }
    }

    return coordinator.launch({
      applicationId: application.applicationId,
      launchTargetId: target?.targetId ?? null,
      title: target?.title ?? application.title,
      bounds: application.defaultBounds,
      instancePolicy: application.instancePolicy,
    }, usableArea);
  }

  public async executeCommand(commandId: string): Promise<void> {
    const command = this.#commands.get(commandId);
    if (command === undefined) {
      throw new LauncherError('launcher.command_missing', 'The command does not exist.');
    }
    if (!hasPermissions(this.#permissions, command.requiredPermissions)) {
      throw new LauncherError('launcher.not_authorized', 'The command is not authorized.');
    }

    await command.execute();
  }
}

export class LauncherError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = 'LauncherError';
    this.code = code;
  }
}

function indexApplication(application: LauncherApplication): IndexedEntry {
  return indexEntry(
    'application',
    application.applicationId,
    application.title,
    application.description,
    application.applicationId,
    null,
    application.keywords,
    application.requiredPermissions,
  );
}

function indexTarget(target: LauncherTarget): IndexedEntry {
  return indexEntry(
    'target',
    target.targetId,
    target.title,
    target.description,
    target.applicationId,
    target.targetId,
    target.keywords,
    target.requiredPermissions,
  );
}

function indexCommand(command: LauncherCommand): IndexedEntry {
  return indexEntry(
    'command',
    command.commandId,
    command.title,
    command.description,
    null,
    null,
    command.keywords,
    command.requiredPermissions,
  );
}

function indexEntry(
  kind: LauncherEntryKind,
  id: string,
  title: string,
  description: string,
  applicationId: string | null,
  targetId: string | null,
  keywords: readonly string[],
  requiredPermissions: readonly string[],
): IndexedEntry {
  validateText(id, 'id');
  validateText(title, 'title');
  const normalizedTitle = normalize(title);
  const normalizedDescription = normalize(description);
  const tokens = tokenize([title, description, ...keywords].join(' '));
  return {
    kind,
    id,
    title,
    description,
    applicationId,
    targetId,
    score: 0,
    normalizedTitle,
    normalizedDescription,
    tokens,
    requiredPermissions,
  };
}

function score(entry: IndexedEntry, query: string, queryTokens: readonly string[]): number {
  if (query.length === 0) {
    return entry.kind === 'application' ? 30 : entry.kind === 'target' ? 20 : 10;
  }

  let result = 0;
  if (entry.normalizedTitle === query) {
    result += 1000;
  } else if (entry.normalizedTitle.startsWith(query)) {
    result += 600;
  } else if (entry.normalizedTitle.includes(query)) {
    result += 350;
  }
  if (entry.id.toLowerCase() === query) {
    result += 800;
  }
  if (entry.normalizedDescription.includes(query)) {
    result += 80;
  }
  for (const token of queryTokens) {
    if (entry.tokens.some((candidate) => candidate === token)) {
      result += 120;
    } else if (entry.tokens.some((candidate) => candidate.startsWith(token))) {
      result += 60;
    } else if (entry.tokens.some((candidate) => candidate.includes(token))) {
      result += 20;
    } else {
      return 0;
    }
  }
  return result;
}

function hasPermissions(granted: ReadonlySet<string>, required: readonly string[]): boolean {
  return required.every((permission) => granted.has(permission));
}

function normalize(value: string): string {
  return value
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/gu, '')
    .toLocaleLowerCase()
    .trim();
}

function tokenize(value: string): readonly string[] {
  return normalize(value).split(/[^\p{L}\p{N}._-]+/u).filter((token) => token.length > 0);
}

function kindOrder(kind: LauncherEntryKind): number {
  return kind === 'application' ? 0 : kind === 'target' ? 1 : 2;
}

function ensureUnique<T>(map: ReadonlyMap<string, T>, id: string, kind: string): void {
  validateText(id, `${kind} identifier`);
  if (map.has(id)) {
    throw new LauncherError('launcher.duplicate_identity', `Duplicate ${kind} identifier '${id}'.`);
  }
}

function validateText(value: string, name: string): void {
  if (value.trim().length === 0 || value !== value.trim() || value.length > 512) {
    throw new LauncherError('launcher.value_invalid', `Launcher ${name} is invalid.`);
  }
}
