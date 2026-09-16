// HCON-001: validates the committed Host Connector contract fixtures against the committed
// schemas, and asserts the prohibitions from docs/HOST_CONNECTOR.md that no schema can
// express — that no contract introduces a generic command, shell, raw TCP destination or
// Docker API payload.

import { readFile } from 'node:fs/promises';
import { join } from 'node:path';

import { findUnsupportedKeywords, validate } from './json-schema.mjs';
import { repositoryRoot } from './repository.mjs';

const schemaRoot = join(repositoryRoot, 'schemas');
const fixtureRoot = join(repositoryRoot, 'tests', 'fixtures', 'host-connector');

/**
 * Each fixture, the schema it belongs to, and whether it must be accepted or rejected.
 * A rejected fixture also names the substring its rejection has to mention, so a fixture
 * cannot pass by failing for an unrelated reason.
 */
const fixtures = [
  { file: 'request-minimum.json', schema: 'host-connector-request.v1', accept: true },
  { file: 'request-complete.json', schema: 'host-connector-request.v1', accept: true },
  { file: 'request-malformed.json', schema: 'host-connector-request.v1', accept: false, mentions: 'MessageId' },
  {
    file: 'request-unsupported-major.json',
    schema: 'host-connector-request.v1',
    accept: false,
    mentions: 'ContractVersion',
  },
  { file: 'result-succeeded.json', schema: 'host-connector-result.v1', accept: true },
  { file: 'result-exact-retry.json', schema: 'host-connector-result.v1', accept: true },
  { file: 'result-conflict.json', schema: 'host-connector-result.v1', accept: true },
  { file: 'result-outcome-unknown.json', schema: 'host-connector-result.v1', accept: true },
  { file: 'result-malformed.json', schema: 'host-connector-result.v1', accept: false, mentions: 'Result' },
  { file: 'enrollment-minimum.json', schema: 'host-connector-enrollment.v1', accept: true },
  { file: 'journal-result-ready.json', schema: 'host-connector-journal.v1', accept: true },
];

/**
 * The committed schemas this validator loads, derived from the fixtures rather than listed
 * again. Reported to `schema-coverage`, which fails when `schemas/` holds anything nobody
 * loads.
 */
export const enforcedSchemas = Object.freeze([...new Set(fixtures.map((entry) => entry.schema))]);

/** Capability shapes docs/HOST_CONNECTOR.md prohibits outright. */
const prohibitedCapabilities = [
  'host.command',
  'shell.execute',
  'host.shell',
  'tcp.proxy',
  'docker.raw',
  'docker.api',
];

/** Field names that would reintroduce a free-form command or destination. */
const prohibitedFields = [
  'command',
  'commandline',
  'shell',
  'script',
  'argv',
  'exec',
  'host',
  'port',
  'socket',
  'dockerendpoint',
];

export async function validateHostConnectorContracts() {
  const errors = [];
  const schemas = new Map();

  for (const name of enforcedSchemas) {
    try {
      const text = await readFile(join(schemaRoot, `${name}.schema.json`), 'utf8');
      const schema = JSON.parse(text.replace(/^﻿/, ''));
      schemas.set(name, schema);

      const unsupported = findUnsupportedKeywords(schema, name);
      errors.push(...unsupported);
    } catch (error) {
      errors.push(`${name}.schema.json: ${error.message}`);
    }
  }

  for (const entry of fixtures) {
    const schema = schemas.get(entry.schema);
    if (!schema) continue;

    let value;
    try {
      const text = await readFile(join(fixtureRoot, entry.file), 'utf8');
      value = JSON.parse(text.replace(/^﻿/, ''));
    } catch (error) {
      errors.push(`${entry.file}: invalid JSON (${error.message})`);
      continue;
    }

    const failures = validate(value, schema);

    if (entry.accept && failures.length > 0) {
      errors.push(`${entry.file}: expected to validate but failed:\n    ${failures.join('\n    ')}`);
    }

    if (!entry.accept) {
      if (failures.length === 0) {
        errors.push(`${entry.file}: expected rejection but the fixture validated`);
      } else if (entry.mentions && !failures.some((failure) => failure.includes(entry.mentions))) {
        errors.push(
          `${entry.file}: rejected, but no error mentioned '${entry.mentions}':\n    ${failures.join('\n    ')}`,
        );
      }
    }

    errors.push(...findProhibited(value, entry.file));
  }

  // The exact-retry fixture must be byte-identical to the result it retries, and the
  // conflict fixture must not be: that is what makes the retry/conflict rule testable.
  const [succeeded, retry, conflict] = await Promise.all([
    readCanonical('result-succeeded.json'),
    readCanonical('result-exact-retry.json'),
    readCanonical('result-conflict.json'),
  ]);

  if (succeeded !== null && retry !== null && succeeded !== retry) {
    errors.push('result-exact-retry.json must canonicalize identically to result-succeeded.json');
  }
  if (succeeded !== null && conflict !== null && succeeded === conflict) {
    errors.push('result-conflict.json must differ from result-succeeded.json');
  }

  return errors;
}

/** Reports prohibited capability names and free-form command fields anywhere in a fixture. */
function findProhibited(value, source, path = 'root') {
  const errors = [];

  if (typeof value === 'string') {
    const lowered = value.toLowerCase();
    for (const capability of prohibitedCapabilities) {
      if (lowered === capability) {
        errors.push(`${source}: ${path} uses the prohibited capability '${capability}'`);
      }
    }
    return errors;
  }

  if (Array.isArray(value)) {
    for (const [index, entry] of value.entries()) {
      errors.push(...findProhibited(entry, source, `${path}[${index}]`));
    }
    return errors;
  }

  if (typeof value === 'object' && value !== null) {
    for (const [key, entry] of Object.entries(value)) {
      if (prohibitedFields.includes(key.toLowerCase())) {
        errors.push(`${source}: ${path}.${key} would reintroduce a free-form command or destination`);
      }
      errors.push(...findProhibited(entry, source, `${path}.${key}`));
    }
  }

  return errors;
}

async function readCanonical(file) {
  try {
    const text = await readFile(join(fixtureRoot, file), 'utf8');
    return JSON.stringify(JSON.parse(text.replace(/^﻿/, '')));
  } catch {
    return null;
  }
}
