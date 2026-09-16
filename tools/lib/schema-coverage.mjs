// A committed schema that no validator loads is not a contract, it is decoration: nothing
// compares it to the implementation, so it drifts silently and eventually contradicts the
// thing it claims to define. That is exactly what happened to the retired
// `package-manifest.v1.schema.json`, which declared itself the published package manifest
// contract while `additionalProperties: false` and a missing `ElementName` would have made
// it reject every manifest in `packages/`. See decision D047.
//
// This check compares what `schemas/` holds against what the validators actually load, in
// both directions, so neither half can be changed alone.

import { readdir } from 'node:fs/promises';
import { join } from 'node:path';

import { enforcedSchemas as catalogSchemas } from './catalog.mjs';
import { enforcedSchemas as hostConnectorSchemas } from './host-connector-contracts.mjs';
import { repositoryRoot } from './repository.mjs';

const schemaDirectory = join(repositoryRoot, 'schemas');
const suffix = '.schema.json';

/** Every schema name a validation stage loads and validates a document against. */
export const enforcedSchemas = Object.freeze([
  ...catalogSchemas,
  ...hostConnectorSchemas,
]);

/** Reports schemas that nothing enforces and enforced schemas that are not committed. */
export async function findSchemaCoverageErrors() {
  const errors = [];

  const committed = (await readdir(schemaDirectory))
    .filter((name) => name.endsWith(suffix))
    .map((name) => name.slice(0, -suffix.length));

  const enforced = new Set(enforcedSchemas);
  if (enforced.size === 0) {
    return ['no validator reports an enforced schema, so this check would pass vacuously'];
  }

  for (const name of committed) {
    if (!enforced.has(name)) {
      errors.push(
        `schemas/${name}${suffix} is committed but no validator loads it. `
          + 'Wire it into a validation stage or delete it; an unenforced schema drifts from '
          + 'the implementation without anything noticing.',
      );
    }
  }

  const committedNames = new Set(committed);
  for (const name of enforced) {
    if (!committedNames.has(name)) {
      errors.push(`a validator enforces '${name}' but schemas/${name}${suffix} does not exist`);
    }
  }

  return errors;
}
