import { readFile } from 'node:fs/promises';

const semanticVersion = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?$/;
const packageIdentifier = /^[a-z0-9]+(?:[.-][a-z0-9]+)+$/;
const dottedIdentifier = /^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)+$/;
const imageDigest = /^[^@\s]+@sha256:[a-f0-9]{64}$/;
const integrity = /^sha256-[A-Za-z0-9+/=]+$/;
const customElement = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)+$/;

const requiredFields = [
  'schemaVersion',
  'id',
  'name',
  'publisher',
  'version',
  'minimumCoreVersion',
  'maximumCoreVersion',
  'worker',
  'capabilitiesProvided',
  'capabilitiesRequired',
  'permissions',
  'applications',
  'widgets',
  'settings',
  'migrations',
  'runtimeProfiles',
  'dependencies',
];

const allowedFields = new Set(requiredFields);

export async function readAndValidatePackageManifest(path) {
  let manifest;
  try {
    manifest = JSON.parse(await readFile(path, 'utf8'));
  } catch (error) {
    return [`${path}: invalid JSON (${error.message})`];
  }

  return validatePackageManifest(manifest, path);
}

export function validatePackageManifest(value, source = 'manifest') {
  const errors = [];
  if (!isRecord(value)) {
    return [`${source}: root must be an object`];
  }

  for (const field of requiredFields) {
    if (!(field in value)) {
      errors.push(`${source}: missing required field '${field}'`);
    }
  }
  for (const field of Object.keys(value)) {
    if (!allowedFields.has(field)) {
      errors.push(`${source}: unknown field '${field}'`);
    }
  }

  if (value.schemaVersion !== 1) {
    errors.push(`${source}: unsupported schemaVersion '${String(value.schemaVersion)}'; supported: 1`);
  }
  checkString(value.id, 'id', source, errors, packageIdentifier, 200);
  checkString(value.name, 'name', source, errors, null, 100);
  checkString(value.publisher, 'publisher', source, errors, null, 100);
  checkVersion(value.version, 'version', source, errors);
  checkVersion(value.minimumCoreVersion, 'minimumCoreVersion', source, errors);
  if (value.maximumCoreVersion !== null) {
    checkVersion(value.maximumCoreVersion, 'maximumCoreVersion', source, errors);
  }

  validateWorker(value.worker, source, errors);
  validateCapabilities(value.capabilitiesProvided, false, source, errors);
  validateCapabilities(value.capabilitiesRequired, true, source, errors);
  validateUniqueStrings(value.permissions, 'permissions', source, errors, dottedIdentifier);
  validateFrontendComponents(value.applications, 'applications', true, source, errors);
  validateFrontendComponents(value.widgets, 'widgets', false, source, errors);
  validateObjectArray(value.settings, 'settings', source, errors);
  validateObjectArray(value.migrations, 'migrations', source, errors);
  validateRuntimeProfiles(value.runtimeProfiles, source, errors);
  validateDependencies(value.dependencies, source, errors);

  return errors;
}

function validateWorker(value, source, errors) {
  if (value === null) {
    return;
  }
  if (!isRecord(value)) {
    errors.push(`${source}: worker must be an object or null`);
    return;
  }
  checkExactFields(value, ['image', 'cpuLimit', 'memoryLimitMb'], 'worker', source, errors);
  checkString(value.image, 'worker.image', source, errors, imageDigest, 500);
  checkPositiveNumber(value.cpuLimit, 'worker.cpuLimit', source, errors, 64);
  checkInteger(value.memoryLimitMb, 'worker.memoryLimitMb', source, errors, 16, 262144);
}

function validateCapabilities(value, required, source, errors) {
  const name = required ? 'capabilitiesRequired' : 'capabilitiesProvided';
  if (!Array.isArray(value)) {
    errors.push(`${source}: ${name} must be an array`);
    return;
  }
  const seen = new Set();
  value.forEach((item, index) => {
    if (!isRecord(item)) {
      errors.push(`${source}: ${name}[${index}] must be an object`);
      return;
    }
    const fields = required ? ['name', 'version', 'optional'] : ['name', 'version'];
    checkExactFields(item, fields, `${name}[${index}]`, source, errors);
    checkString(item.name, `${name}[${index}].name`, source, errors, dottedIdentifier, 200);
    checkInteger(item.version, `${name}[${index}].version`, source, errors, 1, Number.MAX_SAFE_INTEGER);
    if (required && typeof item.optional !== 'boolean') {
      errors.push(`${source}: ${name}[${index}].optional must be boolean`);
    }
    const key = `${String(item.name)}:${String(item.version)}`;
    if (seen.has(key)) {
      errors.push(`${source}: ${name} contains duplicate '${key}'`);
    }
    seen.add(key);
  });
}

function validateFrontendComponents(value, name, application, source, errors) {
  if (!Array.isArray(value)) {
    errors.push(`${source}: ${name} must be an array`);
    return;
  }
  const seen = new Set();
  value.forEach((item, index) => {
    if (!isRecord(item)) {
      errors.push(`${source}: ${name}[${index}] must be an object`);
      return;
    }
    const fields = application
      ? ['key', 'module', 'integrity', 'customElement', 'localization', 'contractVersion', 'permissions', 'instancePolicy', 'minimumWidth', 'minimumHeight']
      : ['key', 'module', 'integrity', 'customElement', 'localization', 'contractVersion', 'permissions', 'sizes'];
    checkExactFields(item, fields, `${name}[${index}]`, source, errors);
    checkString(item.key, `${name}[${index}].key`, source, errors, dottedIdentifier, 200);
    checkString(item.module, `${name}[${index}].module`, source, errors, /^frontend\/.+\.js$/, 500);
    checkString(item.integrity, `${name}[${index}].integrity`, source, errors, integrity, 500);
    checkString(item.customElement, `${name}[${index}].customElement`, source, errors, customElement, 200);
    checkString(item.localization, `${name}[${index}].localization`, source, errors, /^frontend\/.+\.json$/, 500);
    checkInteger(item.contractVersion, `${name}[${index}].contractVersion`, source, errors, 1, Number.MAX_SAFE_INTEGER);
    validateUniqueStrings(item.permissions, `${name}[${index}].permissions`, source, errors, dottedIdentifier);
    if (application) {
      if (!['single-user', 'single-target', 'multiple'].includes(item.instancePolicy)) {
        errors.push(`${source}: ${name}[${index}].instancePolicy is invalid`);
      }
      checkInteger(item.minimumWidth, `${name}[${index}].minimumWidth`, source, errors, 240, 10000);
      checkInteger(item.minimumHeight, `${name}[${index}].minimumHeight`, source, errors, 160, 10000);
    } else {
      validateEnumArray(item.sizes, `${name}[${index}].sizes`, ['small', 'medium', 'wide', 'large'], source, errors);
    }
    if (seen.has(item.key)) {
      errors.push(`${source}: ${name} contains duplicate key '${String(item.key)}'`);
    }
    seen.add(item.key);
  });
}

function validateRuntimeProfiles(value, source, errors) {
  if (!Array.isArray(value)) {
    errors.push(`${source}: runtimeProfiles must be an array`);
    return;
  }
  const seen = new Set();
  value.forEach((item, index) => {
    if (!isRecord(item)) {
      errors.push(`${source}: runtimeProfiles[${index}] must be an object`);
      return;
    }
    checkExactFields(item, ['name', 'image', 'networks', 'volumes', 'cpuLimit', 'memoryLimitMb'], `runtimeProfiles[${index}]`, source, errors);
    checkString(item.name, `runtimeProfiles[${index}].name`, source, errors, dottedIdentifier, 200);
    checkString(item.image, `runtimeProfiles[${index}].image`, source, errors, imageDigest, 500);
    validateUniqueStrings(item.networks, `runtimeProfiles[${index}].networks`, source, errors, null);
    validateUniqueStrings(item.volumes, `runtimeProfiles[${index}].volumes`, source, errors, null);
    checkPositiveNumber(item.cpuLimit, `runtimeProfiles[${index}].cpuLimit`, source, errors, 64);
    checkInteger(item.memoryLimitMb, `runtimeProfiles[${index}].memoryLimitMb`, source, errors, 16, 262144);
    if (seen.has(item.name)) {
      errors.push(`${source}: runtimeProfiles contains duplicate '${String(item.name)}'`);
    }
    seen.add(item.name);
  });
}

function validateDependencies(value, source, errors) {
  if (!Array.isArray(value)) {
    errors.push(`${source}: dependencies must be an array`);
    return;
  }
  const seen = new Set();
  value.forEach((item, index) => {
    if (!isRecord(item)) {
      errors.push(`${source}: dependencies[${index}] must be an object`);
      return;
    }
    checkExactFields(item, ['id', 'minimumVersion', 'optional'], `dependencies[${index}]`, source, errors);
    checkString(item.id, `dependencies[${index}].id`, source, errors, dottedIdentifier, 200);
    checkVersion(item.minimumVersion, `dependencies[${index}].minimumVersion`, source, errors);
    if (typeof item.optional !== 'boolean') {
      errors.push(`${source}: dependencies[${index}].optional must be boolean`);
    }
    if (seen.has(item.id)) {
      errors.push(`${source}: dependencies contains duplicate '${String(item.id)}'`);
    }
    seen.add(item.id);
  });
}

function validateObjectArray(value, field, source, errors) {
  if (!Array.isArray(value) || value.some((item) => !isRecord(item))) {
    errors.push(`${source}: ${field} must be an array of objects`);
  }
}

function validateUniqueStrings(value, field, source, errors, pattern) {
  if (!Array.isArray(value)) {
    errors.push(`${source}: ${field} must be an array`);
    return;
  }
  const seen = new Set();
  value.forEach((item, index) => {
    checkString(item, `${field}[${index}]`, source, errors, pattern, 500);
    if (seen.has(item)) {
      errors.push(`${source}: ${field} contains duplicate '${String(item)}'`);
    }
    seen.add(item);
  });
}

function validateEnumArray(value, field, allowed, source, errors) {
  if (!Array.isArray(value) || value.length === 0) {
    errors.push(`${source}: ${field} must be a non-empty array`);
    return;
  }
  const seen = new Set();
  value.forEach((item) => {
    if (!allowed.includes(item)) {
      errors.push(`${source}: ${field} contains invalid value '${String(item)}'`);
    }
    if (seen.has(item)) {
      errors.push(`${source}: ${field} contains duplicate '${String(item)}'`);
    }
    seen.add(item);
  });
}

function checkExactFields(value, fields, field, source, errors) {
  for (const required of fields) {
    if (!(required in value)) {
      errors.push(`${source}: ${field} missing '${required}'`);
    }
  }
  for (const present of Object.keys(value)) {
    if (!fields.includes(present)) {
      errors.push(`${source}: ${field} has unknown field '${present}'`);
    }
  }
}

function checkVersion(value, field, source, errors) {
  checkString(value, field, source, errors, semanticVersion, 100);
}

function checkString(value, field, source, errors, pattern, maxLength) {
  if (typeof value !== 'string' || value.length === 0 || value.trim() !== value || value.length > maxLength || (pattern && !pattern.test(value))) {
    errors.push(`${source}: ${field} is invalid`);
  }
}

function checkPositiveNumber(value, field, source, errors, maximum) {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0 || value > maximum) {
    errors.push(`${source}: ${field} must be greater than zero and at most ${maximum}`);
  }
}

function checkInteger(value, field, source, errors, minimum, maximum) {
  if (!Number.isInteger(value) || value < minimum || value > maximum) {
    errors.push(`${source}: ${field} must be an integer from ${minimum} through ${maximum}`);
  }
}

function isRecord(value) {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
