// Validates that relative Markdown links point at files that exist.
//
// The documentation set cross-references heavily, so a renamed file must fail
// the build rather than leave a dead link in the authoritative specification.

import { access, readFile } from 'node:fs/promises';
import { dirname, extname, resolve } from 'node:path';

import { toRepositoryPath, walkFiles } from './repository.mjs';

const inlineLink = /\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g;

const externalPrefixes = ['http://', 'https://', 'mailto:', 'tel:', '//'];

/** Returns true when the target is not a path inside the repository. */
function isExternal(target) {
  return externalPrefixes.some((prefix) => target.startsWith(prefix)) || target.startsWith('#');
}

/** Returns the broken links found in every committed Markdown file. */
export async function findBrokenLinks() {
  const broken = [];

  for await (const absolutePath of walkFiles()) {
    if (extname(absolutePath).toLowerCase() !== '.md') {
      continue;
    }

    const source = toRepositoryPath(absolutePath);
    const lines = (await readFile(absolutePath, 'utf8')).split('\n');

    for (const [index, line] of lines.entries()) {
      for (const match of line.matchAll(inlineLink)) {
        const target = match[1];

        if (isExternal(target)) {
          continue;
        }

        const [path] = target.split('#');

        if (path === '') {
          continue;
        }

        const resolved = resolve(dirname(absolutePath), decodeURIComponent(path));

        try {
          await access(resolved);
        } catch {
          broken.push(`${source}:${index + 1} -> ${target}`);
        }
      }
    }
  }

  return broken;
}
