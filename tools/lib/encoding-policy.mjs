// The repository encoding policy from decision D012, in executable form.
//
// General repository text is UTF-8 with a byte order mark and CRLF. Text a Unix
// runtime executes or parses is UTF-8 with LF and no mark. Files that a tool
// rewrites itself are excluded, because the policy cannot survive that tool.

import { spawnSync } from 'node:child_process';
import { readFile, writeFile, mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { basename, extname, join } from 'node:path';

import { repositoryRoot, toRepositoryPath, walkFiles } from './repository.mjs';

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

/**
 * Returns the extensions whose line ending git does not pin.
 *
 * Without an explicit `eol` attribute git checks a file out with the platform's
 * line ending, so the same commit satisfies this policy on Windows and violates
 * it on Linux. The policy and `.gitattributes` therefore have to agree.
 */
export async function findUnpinnedExtensions() {
  const attributes = await readFile(join(repositoryRoot, '.gitattributes'), 'utf8');

  const pinned = new Map();

  for (const line of attributes.split('\n')) {
    const [pattern, ...rest] = line.trim().split(/\s+/);
    const eol = rest.find((attribute) => attribute.startsWith('eol='));

    if (pattern?.startsWith('*.') === true && eol !== undefined) {
      pinned.set(pattern.slice(1).toLowerCase(), eol.slice('eol='.length));
    }
  }

  const missing = [];

  for (const [extensions, required] of [
    [bomCrlfExtensions, 'crlf'],
    [plainLfExtensions, 'lf'],
  ]) {
    for (const extension of extensions) {
      if (pinned.get(extension) !== required) {
        missing.push(`${extension} (.gitattributes must pin it to eol=${required})`);
      }
    }
  }

  return missing;
}

/**
 * Returns tracked files whose committed blobs bypass Git's configured clean
 * conversion. This catches files written through APIs that do not apply
 * `.gitattributes`, which would otherwise appear dirty after a tool rewrites
 * them with the same logical content.
 */
async function findNonCanonicalBlobs() {
  const temporaryDirectory = await mkdtemp(join(tmpdir(), 'julos-index-'));
  const indexPath = join(temporaryDirectory, 'index');
  const environment = { ...process.env, GIT_INDEX_FILE: indexPath };
  const runGit = (arguments_) => spawnSync('git', arguments_, {
    cwd: repositoryRoot,
    env: environment,
    encoding: 'utf8',
  });

  try {
    const readTree = runGit(['read-tree', 'HEAD']);
    if (readTree.error || readTree.status !== 0) {
      return [`git read-tree failed during blob-normalization validation: ${readTree.stderr?.trim() ?? readTree.error?.message ?? 'unknown error'}`];
    }

    const renormalize = runGit(['add', '--renormalize', '.']);
    if (renormalize.error || renormalize.status !== 0) {
      return [`git add --renormalize failed during blob-normalization validation: ${renormalize.stderr?.trim() ?? renormalize.error?.message ?? 'unknown error'}`];
    }

    const difference = runGit(['diff', '--cached', '--name-only', 'HEAD', '--']);
    if (difference.error || difference.status !== 0) {
      return [`git diff failed during blob-normalization validation: ${difference.stderr?.trim() ?? difference.error?.message ?? 'unknown error'}`];
    }

    return difference.stdout
      .split(/\r?\n/)
      .filter((path) => path.length > 0)
      .map((path) => `${path} (committed blob is not normalized through .gitattributes)`);
  } finally {
    await rm(temporaryDirectory, { recursive: true, force: true });
  }
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

  violations.push(...await findNonCanonicalBlobs());
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
