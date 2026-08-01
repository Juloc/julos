// Repository locations and file enumeration shared by the repository tools.

import { readdir } from 'node:fs/promises';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

/** Absolute path of the repository root. */
export const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** Directories that hold generated or external content and are never inspected. */
const ignoredDirectories = new Set(['.git', 'bin', 'obj', 'node_modules', 'dist', 'build', 'artifacts']);

/** Yields every committed-looking file below `directory`, as an absolute path. */
export async function* walkFiles(directory = repositoryRoot) {
  const entries = await readdir(directory, { withFileTypes: true });

  for (const entry of entries) {
    const path = join(directory, entry.name);

    if (entry.isDirectory()) {
      if (!ignoredDirectories.has(entry.name)) {
        yield* walkFiles(path);
      }
    } else if (entry.isFile()) {
      yield path;
    }
  }
}

/** Returns a path relative to the repository root, using forward slashes. */
export function toRepositoryPath(absolutePath) {
  return relative(repositoryRoot, absolutePath).split(sep).join('/');
}
