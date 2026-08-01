// The repository encoding policy from decision D012, in executable form.
//
// General repository text is UTF-8 with a byte order mark and CRLF. Text a Unix
// runtime executes or parses is UTF-8 with LF and no mark. Files that a tool
// rewrites itself are excluded, because the policy cannot survive that tool.

import { readFile, writeFile } from 'node:fs/promises';
import { basename, extname } from 'node:path';

import { toRepositoryPath, walkFiles } from './repository.mjs';

const byteOrderMark = Buffer.from([0xef, 0xbb, 0xbf]);

const bomCrlfExtensions = new Set([
  '.md', '.txt', '.cs', '.csproj', '.slnx', '.props', '.targets', '.json',
  '.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs', '.css', '.scss', '.html', '.ps1',
]);

const plainLfExtensions = new Set(['.sh', '.bash', '.yml', '.yaml']);

/** Files whose owning tool rewrites them and does not preserve the general policy. */
const exemptFileNames = new Set([
  'package.json',
  'package-lock.json',
  '.gitattributes',
  '.gitignore',
  '.editorconfig',
]);

/** Returns the required form of a file, or null when the policy does not cover it. */
export function requiredForm(absolutePath) {
  const name = basename(absolutePath);

  if (exemptFileNames.has(name)) {
    return null;
  }

  if (name === 'Dockerfile' || name.startsWith('Dockerfile.')) {
    return 'plain-lf';
  }

  const extension = extname(name).toLowerCase();

  if (bomCrlfExtensions.has(extension)) {
    return 'bom-crlf';
  }

  return plainLfExtensions.has(extension) ? 'plain-lf' : null;
}

/** Rewrites `content` into the required form and returns the bytes. */
function toRequiredBytes(content, form) {
  let normalised = content.replaceAll('\r\n', '\n');

  // insert_final_newline is part of the same .editorconfig policy.
  if (!normalised.endsWith('\n')) {
    normalised += '\n';
  }

  if (form === 'plain-lf') {
    return Buffer.from(normalised, 'utf8');
  }

  return Buffer.concat([byteOrderMark, Buffer.from(normalised.replaceAll('\n', '\r\n'), 'utf8')]);
}

/** Reads a file and returns its required bytes plus whether it already matches. */
async function inspect(absolutePath, form) {
  const actual = await readFile(absolutePath);

  if (actual.length === 0) {
    return { actual, required: actual, matches: true };
  }

  const hasMark = actual.subarray(0, 3).equals(byteOrderMark);
  const text = actual.subarray(hasMark ? 3 : 0).toString('utf8');
  const required = toRequiredBytes(text, form);

  return { actual, required, matches: actual.equals(required) };
}

/** Returns the repository-relative paths of every file that violates the policy. */
export async function findViolations() {
  const violations = [];

  for await (const absolutePath of walkFiles()) {
    const form = requiredForm(absolutePath);

    if (form === null) {
      continue;
    }

    const { matches } = await inspect(absolutePath, form);

    if (!matches) {
      violations.push(`${toRepositoryPath(absolutePath)} (expected ${form})`);
    }
  }

  return violations;
}

/** Rewrites every violating file and returns the repository-relative paths that changed. */
export async function fixViolations() {
  const fixed = [];

  for await (const absolutePath of walkFiles()) {
    const form = requiredForm(absolutePath);

    if (form === null) {
      continue;
    }

    const { required, matches } = await inspect(absolutePath, form);

    if (!matches) {
      await writeFile(absolutePath, required);
      fixed.push(toRepositoryPath(absolutePath));
    }
  }

  return fixed;
}
