export interface LauncherEntry {
  readonly id: string;
  readonly kind: 'application' | 'target' | 'command';
  readonly title: string;
  readonly subtitle?: string;
  readonly keywords?: readonly string[];
  readonly requiredPermissions?: readonly string[];
  readonly execute?: () => void | Promise<void>;
}

export interface LauncherSearchResult {
  readonly entry: LauncherEntry;
  readonly score: number;
}

export class LauncherAuthorizationError extends Error {
  public readonly entryId: string;

  public constructor(entryId: string) {
    super(`Launcher entry '${entryId}' is not permitted.`);
    this.name = 'LauncherAuthorizationError';
    this.entryId = entryId;
  }
}

interface IndexedEntry {
  readonly entry: LauncherEntry;
  readonly normalized: string;
}

export class LauncherCatalog {
  readonly #entries: readonly IndexedEntry[];

  public constructor(entries: readonly LauncherEntry[]) {
    const identifiers = new Set<string>();
    this.#entries = entries.map((entry) => {
      validateEntry(entry, identifiers);
      identifiers.add(entry.id);
      return {
        entry,
        normalized: normalize([
          entry.title,
          entry.subtitle ?? '',
          ...(entry.keywords ?? []),
        ].join(' ')),
      };
    });
  }

  public search(
    query: string,
    grantedPermissions: ReadonlySet<string>,
    limit = 50,
  ): readonly LauncherSearchResult[] {
    if (!Number.isInteger(limit) || limit <= 0 || limit > 200) {
      throw new RangeError('Launcher search limit must be between 1 and 200.');
    }

    const terms = normalize(query).split(' ').filter((term) => term.length > 0);
    return this.#entries
      .filter(({ entry }) => isAuthorized(entry, grantedPermissions))
      .map(({ entry, normalized }) => ({ entry, score: scoreEntry(entry, normalized, terms) }))
      .filter((result) => terms.length === 0 || result.score > 0)
      .sort((left, right) => right.score - left.score || left.entry.title.localeCompare(right.entry.title))
      .slice(0, limit);
  }

  public async execute(entryId: string, grantedPermissions: ReadonlySet<string>): Promise<void> {
    const indexed = this.#entries.find(({ entry }) => entry.id === entryId);
    if (indexed === undefined) {
      throw new Error(`Launcher entry '${entryId}' does not exist.`);
    }

    if (!isAuthorized(indexed.entry, grantedPermissions)) {
      throw new LauncherAuthorizationError(entryId);
    }

    if (indexed.entry.execute === undefined) {
      throw new Error(`Launcher entry '${entryId}' has no executable action.`);
    }

    await indexed.entry.execute();
  }
}

function scoreEntry(entry: LauncherEntry, normalized: string, terms: readonly string[]): number {
  if (terms.length === 0) {
    return entry.kind === 'application' ? 30 : entry.kind === 'target' ? 20 : 10;
  }

  let score = 0;
  const normalizedTitle = normalize(entry.title);
  for (const term of terms) {
    if (!normalized.includes(term)) {
      return 0;
    }

    score += normalizedTitle === term ? 100 : normalizedTitle.startsWith(term) ? 60 : 20;
  }

  return score + (entry.kind === 'application' ? 3 : entry.kind === 'target' ? 2 : 1);
}

function isAuthorized(entry: LauncherEntry, grantedPermissions: ReadonlySet<string>): boolean {
  return (entry.requiredPermissions ?? []).every((permission) => grantedPermissions.has(permission));
}

function normalize(value: string): string {
  return value.normalize('NFKD').toLocaleLowerCase('en-US').replace(/\p{Diacritic}/gu, '').trim();
}

function validateEntry(entry: LauncherEntry, identifiers: ReadonlySet<string>): void {
  if (entry.id.trim().length === 0 || entry.id !== entry.id.trim() || identifiers.has(entry.id)) {
    throw new TypeError(`Launcher entry identifier '${entry.id}' is invalid or duplicated.`);
  }

  if (entry.title.trim().length === 0 || entry.title !== entry.title.trim()) {
    throw new TypeError(`Launcher entry '${entry.id}' has an invalid title.`);
  }

  for (const permission of entry.requiredPermissions ?? []) {
    if (permission.trim().length === 0 || permission !== permission.trim()) {
      throw new TypeError(`Launcher entry '${entry.id}' has an invalid permission.`);
    }
  }
}
