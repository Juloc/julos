// CAT-001: catalog index, application manifest, key set and bundle validation from
// docs/APPLICATION_CATALOG.md sections 3 and 4.
//
// Validation is deliberately paranoid about the bundle itself: an entry path that escapes
// the source root, a symlink, an oversized file or a digest mismatch fails before the file
// is parsed, because parsing is the first thing that acts on untrusted catalog content.

import { createHash } from 'node:crypto';
import { lstat, readFile, readdir } from 'node:fs/promises';
import { join, posix } from 'node:path';

import { canonicalJson, planDigest, validateJulosCompose } from './julos-compose.mjs';
import { findUnsupportedKeywords, validate } from './json-schema.mjs';
import { repositoryRoot } from './repository.mjs';

export const catalogErrorCodes = Object.freeze({
  schemaUnsupported: 'catalog.schema_unsupported',
  definitionInvalid: 'catalog.definition_invalid',
  integrityMismatch: 'catalog.integrity_mismatch',
});

/** Maximum size of any single file a catalog entry references. */
export const maximumBundleFileBytes = 2 * 1024 * 1024;

const schemaDirectory = join(repositoryRoot, 'schemas');

/**
 * The committed schemas this validator loads, named once so every use below and the
 * `schema-coverage` stage read the same values.
 */
const schemaNames = Object.freeze({
  index: 'app-catalog-index.v1',
  manifest: 'app-manifest.v1',
  keySet: 'app-catalog-keyset.v1',
});

/** Reported to `schema-coverage`, which fails when `schemas/` holds anything nobody loads. */
export const enforcedSchemas = Object.freeze(Object.values(schemaNames));

/** Loads a committed schema and reports any keyword the validator cannot enforce. */
async function loadSchema(name, errors) {
  const text = await readFile(join(schemaDirectory, `${name}.schema.json`), 'utf8');
  const schema = JSON.parse(text.replace(/^﻿/, ''));
  errors.push(...findUnsupportedKeywords(schema, name));
  return schema;
}

/**
 * Validates one catalog bundle rooted at `root`.
 *
 * Returns the errors found and, for each valid entry, its definition and plan digests so
 * a caller can assert that parse/canonicalize/reparse is stable.
 */
export async function validateCatalogBundle(root) {
  const errors = [];
  const entries = [];

  const indexSchema = await loadSchema(schemaNames.index, errors);
  const manifestSchema = await loadSchema(schemaNames.manifest, errors);
  const keySetSchema = await loadSchema(schemaNames.keySet, errors);

  const indexPath = join(root, 'catalog.json');
  const index = await readJson(indexPath, errors, 'catalog.json');
  if (index === null) {
    return { errors, entries };
  }

  if (index.schema !== schemaNames.index) {
    errors.push(`${catalogErrorCodes.schemaUnsupported}: catalog.json declares '${String(index.schema)}'`);
    return { errors, entries };
  }

  errors.push(...prefix(validate(index, indexSchema), 'catalog.json'));

  if (index.keySet) {
    const keySetErrors = await validateBundleFile(root, index.keySet.path, index.keySet.sha256, 'keySet');
    errors.push(...keySetErrors);
    if (keySetErrors.length === 0) {
      const keySet = await readJson(join(root, index.keySet.path), errors, index.keySet.path);
      if (keySet !== null) {
        errors.push(...prefix(validate(keySet, keySetSchema), index.keySet.path));
      }
    }
  }

  const seen = new Set();

  for (const entry of index.entries ?? []) {
    const identity = `${entry.appId}@${entry.version}`;
    if (seen.has(identity)) {
      // A duplicate (appId, version) fails the complete refresh transaction.
      errors.push(`${catalogErrorCodes.definitionInvalid}: duplicate catalog entry '${identity}'`);
      continue;
    }
    seen.add(identity);

    const fileErrors = await validateBundleFile(root, entry.path, entry.sha256, identity);
    errors.push(...fileErrors);
    if (fileErrors.length > 0) {
      continue;
    }

    const manifest = await readJson(join(root, entry.path), errors, entry.path);
    if (manifest === null) continue;

    if (manifest.schema !== schemaNames.manifest) {
      errors.push(`${catalogErrorCodes.schemaUnsupported}: ${entry.path} declares '${String(manifest.schema)}'`);
      continue;
    }

    const manifestErrors = [
      ...prefix(validate(manifest, manifestSchema), entry.path),
      ...checkUnknownOrdinaryFields(manifest, manifestSchema, entry.path),
      ...checkIdentity(manifest, entry),
    ];

    const composeResult = await validateCompose(root, entry, manifest);
    manifestErrors.push(...composeResult.errors);

    errors.push(...manifestErrors);
    if (manifestErrors.length > 0) continue;

    entries.push({
      identity,
      definitionDigest: createHash('sha256').update(canonicalJson(manifest)).digest('hex'),
      planDigest: composeResult.plan === null ? null : planDigest(composeResult.plan),
      plan: composeResult.plan,
      manifest,
      criticalRights: composeResult.criticalRights,
    });
  }

  return { errors, entries };
}

async function validateCompose(root, entry, manifest) {
  const option = (manifest.deliveryOptions ?? []).find((delivery) => delivery.kind === 'docker-compose');
  if (option === undefined) {
    return { errors: [], plan: null, criticalRights: [] };
  }

  const versionDirectory = posix.dirname(entry.path);
  const composePath = posix.join(versionDirectory, option.composePath ?? '');
  const fileErrors = await validateBundleFile(root, composePath, option.composeSha256, `${entry.appId} compose`);
  if (fileErrors.length > 0) {
    return { errors: fileErrors, plan: null, criticalRights: [] };
  }

  const text = await readFile(join(root, composePath), 'utf8');
  const parameters = (manifest.parameters ?? []).map((parameter) => parameter.key);
  const result = validateJulosCompose(text, { parameters });

  return {
    errors: prefix(result.errors, composePath),
    plan: result.plan,
    criticalRights: result.criticalRights,
  };
}

/**
 * Applies the bundle rules: relative normalized path below the source root, a regular
 * file rather than a symlink, within the size limit, and matching its declared digest.
 */
async function validateBundleFile(root, path, expectedSha256, label) {
  const errors = [];

  if (typeof path !== 'string' || path.length === 0) {
    return [`${catalogErrorCodes.definitionInvalid}: ${label} has no path`];
  }
  if (path.startsWith('/') || /^[A-Za-z]:/.test(path) || path.includes('\\')) {
    return [`${catalogErrorCodes.definitionInvalid}: ${label} path '${path}' must be relative with forward slashes`];
  }

  const normalized = posix.normalize(path);
  if (normalized.startsWith('..') || normalized.split('/').includes('..')) {
    return [`${catalogErrorCodes.definitionInvalid}: ${label} path '${path}' escapes the source root`];
  }

  const absolute = join(root, normalized);
  let stats;
  try {
    stats = await lstat(absolute);
  } catch {
    return [`${catalogErrorCodes.definitionInvalid}: ${label} path '${path}' does not exist`];
  }

  if (stats.isSymbolicLink()) {
    return [`${catalogErrorCodes.definitionInvalid}: ${label} path '${path}' is a symbolic link`];
  }
  if (!stats.isFile()) {
    return [`${catalogErrorCodes.definitionInvalid}: ${label} path '${path}' is not a regular file`];
  }
  if (stats.size > maximumBundleFileBytes) {
    return [`${catalogErrorCodes.definitionInvalid}: ${label} path '${path}' exceeds ${maximumBundleFileBytes} bytes`];
  }

  const bytes = await readFile(absolute);
  const actual = createHash('sha256').update(bytes).digest('hex');
  if (typeof expectedSha256 === 'string' && actual !== expectedSha256) {
    errors.push(
      `${catalogErrorCodes.integrityMismatch}: ${label} path '${path}' has digest ${actual}, expected ${expectedSha256}`,
    );
  }

  return errors;
}

/**
 * Unknown ordinary fields fail; unknown `x-` keys are inert metadata and are preserved.
 *
 * The schema cannot express this on its own, because `additionalProperties: false` would
 * also reject the `x-` keys the specification requires to be kept.
 */
function checkUnknownOrdinaryFields(manifest, schema, source) {
  const errors = [];
  const known = new Set(Object.keys(schema.properties ?? {}));

  for (const key of Object.keys(manifest)) {
    if (known.has(key) || key.startsWith('x-')) continue;
    errors.push(`${catalogErrorCodes.definitionInvalid}: ${source} has unknown field '${key}'`);
  }

  const deliverySchema = schema.$defs?.deliveryOption ?? {};
  const deliveryKnown = new Set(Object.keys(deliverySchema.properties ?? {}));
  for (const [index, option] of (manifest.deliveryOptions ?? []).entries()) {
    for (const key of Object.keys(option ?? {})) {
      if (deliveryKnown.has(key) || key.startsWith('x-')) continue;
      errors.push(`${catalogErrorCodes.definitionInvalid}: ${source} deliveryOptions[${index}] has unknown field '${key}'`);
    }
  }

  return errors;
}

/** The index entry and the manifest must agree; the manifest is authoritative for AppId. */
function checkIdentity(manifest, entry) {
  const errors = [];
  if (manifest.appId !== entry.appId) {
    errors.push(
      `${catalogErrorCodes.definitionInvalid}: ${entry.path} declares appId '${manifest.appId}' but the index says '${entry.appId}'`,
    );
  }
  if (manifest.version !== entry.version) {
    errors.push(
      `${catalogErrorCodes.definitionInvalid}: ${entry.path} declares version '${manifest.version}' but the index says '${entry.version}'`,
    );
  }
  return errors;
}

async function readJson(path, errors, label) {
  try {
    const text = await readFile(path, 'utf8');
    return JSON.parse(text.replace(/^﻿/, ''));
  } catch (error) {
    errors.push(`${catalogErrorCodes.definitionInvalid}: ${label} is not readable JSON (${error.message})`);
    return null;
  }
}

function prefix(errors, source) {
  return errors.map((error) => (error.includes(': ') ? `${source}: ${error}` : `${source}: ${error}`));
}

/** Lists the catalog bundles committed as fixtures. */
export async function findCatalogFixtures(fixtureRoot) {
  const names = await readdir(fixtureRoot, { withFileTypes: true });
  return names.filter((entry) => entry.isDirectory()).map((entry) => join(fixtureRoot, entry.name));
}
