import { readFile } from 'node:fs/promises';

const semanticVersion = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/;
const packageIdentifier = /^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9-]*)+$/;
const identifier = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;
const stableKey = /^[a-z][a-z0-9._-]{0,63}$/;
const resourceKey = /^[a-z][a-z0-9_.-]{0,127}$/;
const permissionName = /^[a-z][a-z0-9._:-]{0,127}$/;
const imageDigest = /^[^@\s]+@sha256:[a-f0-9]{64}$/;
const sha256 = /^[0-9a-f]{64}$/;
const customElement = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)+$/;

const rootFields = [
  'SchemaVersion',
  'PackageId',
  'Version',
  'PublisherId',
  'DisplayNameKey',
  'DescriptionKey',
  'Runtime',
  'Permissions',
  'Applications',
  'Widgets',
  'Capabilities',
  'Migrations',
  'Frontend',
];

export async function readAndValidatePackageManifest(path) {
  let manifest;
  try {
    const text = (await readFile(path, 'utf8')).replace(/^\uFEFF/, '');
    manifest = JSON.parse(text);
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

  checkExactFields(value, rootFields, 'root', source, errors);
  if (value.SchemaVersion !== '1') {
    errors.push(`${source}: unsupported SchemaVersion '${String(value.SchemaVersion)}'; supported: 1`);
  }
  checkString(value.PackageId, 'PackageId', source, errors, packageIdentifier, 200);
  checkString(value.Version, 'Version', source, errors, semanticVersion, 100);
  checkString(value.PublisherId, 'PublisherId', source, errors, identifier, 128);
  checkString(value.DisplayNameKey, 'DisplayNameKey', source, errors, resourceKey, 128);
  checkString(value.DescriptionKey, 'DescriptionKey', source, errors, resourceKey, 128);
  validateRuntime(value.Runtime, source, errors);
  validateUniqueStrings(value.Permissions, 'Permissions', source, errors, permissionName, true);
  validateApplications(value.Applications, source, errors);
  validateWidgets(value.Widgets, source, errors);
  validateCapabilities(value.Capabilities, source, errors);
  validateMigrations(value.Migrations, source, errors);
  validateFrontend(value.Frontend, source, errors);
  validateFrontendBindings(value, source, errors);
  return errors;
}

function validateRuntime(value, source, errors) {
  if (!isRecord(value)) {
    errors.push(`${source}: Runtime must be an object`);
    return;
  }
  checkExactFields(
    value,
    ['Kind', 'Image', 'EntryPoint', 'MemoryLimitMegabytes', 'CpuLimit', 'StartupTimeoutSeconds', 'NetworkAccess'],
    'Runtime',
    source,
    errors,
  );
  if (!['none', 'container', 'process'].includes(value.Kind)) {
    errors.push(`${source}: Runtime.Kind is invalid`);
  }
  if (value.Image !== null && typeof value.Image !== 'string') {
    errors.push(`${source}: Runtime.Image must be a string or null`);
  }
  if (value.EntryPoint !== null && typeof value.EntryPoint !== 'string') {
    errors.push(`${source}: Runtime.EntryPoint must be a string or null`);
  }
  if (value.Kind === 'container' && (typeof value.Image !== 'string' || !imageDigest.test(value.Image))) {
    errors.push(`${source}: Runtime.Image must be pinned by sha256 digest for container runtimes`);
  }
  if (value.Kind === 'process' && (typeof value.EntryPoint !== 'string' || value.EntryPoint.trim().length === 0)) {
    errors.push(`${source}: Runtime.EntryPoint is required for process runtimes`);
  }
  checkInteger(value.MemoryLimitMegabytes, 'Runtime.MemoryLimitMegabytes', source, errors, 16, 32768);
  checkNumber(value.CpuLimit, 'Runtime.CpuLimit', source, errors, 0, 32);
  checkInteger(value.StartupTimeoutSeconds, 'Runtime.StartupTimeoutSeconds', source, errors, 1, 300);
  if (typeof value.NetworkAccess !== 'boolean') {
    errors.push(`${source}: Runtime.NetworkAccess must be boolean`);
  }
}

function validateApplications(value, source, errors) {
  validateObjectArray(value, 'Applications', source, errors, (application, index) => {
    const field = `Applications[${index}]`;
    checkExactFields(
      application,
      ['StableKey', 'DisplayNameKey', 'InstancePolicy', 'DefaultWidth', 'DefaultHeight', 'MinimumWidth', 'MinimumHeight', 'Viewports', 'ElementName'],
      field,
      source,
      errors,
    );
    checkString(application.StableKey, `${field}.StableKey`, source, errors, stableKey, 64);
    checkString(application.DisplayNameKey, `${field}.DisplayNameKey`, source, errors, resourceKey, 128);
    checkString(application.ElementName, `${field}.ElementName`, source, errors, customElement, 200);
    if (!['single-instance-per-user', 'single-instance-per-target', 'multiple-instances'].includes(application.InstancePolicy)) {
      errors.push(`${source}: ${field}.InstancePolicy is invalid`);
    }
    checkInteger(application.DefaultWidth, `${field}.DefaultWidth`, source, errors, 120, 16384);
    checkInteger(application.DefaultHeight, `${field}.DefaultHeight`, source, errors, 120, 16384);
    checkInteger(application.MinimumWidth, `${field}.MinimumWidth`, source, errors, 120, 16384);
    checkInteger(application.MinimumHeight, `${field}.MinimumHeight`, source, errors, 120, 16384);
    if (Number.isInteger(application.DefaultWidth) && Number.isInteger(application.MinimumWidth)
        && application.DefaultWidth < application.MinimumWidth) {
      errors.push(`${source}: ${field}.DefaultWidth is smaller than MinimumWidth`);
    }
    if (Number.isInteger(application.DefaultHeight) && Number.isInteger(application.MinimumHeight)
        && application.DefaultHeight < application.MinimumHeight) {
      errors.push(`${source}: ${field}.DefaultHeight is smaller than MinimumHeight`);
    }
    validateEnumArray(application.Viewports, `${field}.Viewports`, ['desktop', 'tablet', 'mobile'], source, errors, true);
  }, 'StableKey');
}

function validateWidgets(value, source, errors) {
  validateObjectArray(value, 'Widgets', source, errors, (widget, index) => {
    const field = `Widgets[${index}]`;
    checkExactFields(widget, ['StableKey', 'DisplayNameKey', 'ElementName', 'Sizes', 'DefaultSize'], field, source, errors);
    checkString(widget.StableKey, `${field}.StableKey`, source, errors, stableKey, 64);
    checkString(widget.DisplayNameKey, `${field}.DisplayNameKey`, source, errors, resourceKey, 128);
    checkString(widget.ElementName, `${field}.ElementName`, source, errors, customElement, 200);
    validateEnumArray(widget.Sizes, `${field}.Sizes`, ['small', 'medium', 'wide', 'large'], source, errors, true);
    if (!Array.isArray(widget.Sizes) || !widget.Sizes.includes(widget.DefaultSize)) {
      errors.push(`${source}: ${field}.DefaultSize must be one of Sizes`);
    }
  }, 'StableKey');
}

function validateCapabilities(value, source, errors) {
  validateObjectArray(value, 'Capabilities', source, errors, (capability, index) => {
    const field = `Capabilities[${index}]`;
    checkExactFields(capability, ['Name', 'Direction', 'ContractVersion', 'Required'], field, source, errors);
    checkString(capability.Name, `${field}.Name`, source, errors, permissionName, 128);
    if (!['provides', 'requires'].includes(capability.Direction)) {
      errors.push(`${source}: ${field}.Direction is invalid`);
    }
    checkString(capability.ContractVersion, `${field}.ContractVersion`, source, errors, semanticVersion, 100);
    if (typeof capability.Required !== 'boolean') {
      errors.push(`${source}: ${field}.Required must be boolean`);
    }
  }, (item) => `${item.Direction}:${item.Name}`);
}

function validateMigrations(value, source, errors) {
  validateObjectArray(value, 'Migrations', source, errors, (migration, index) => {
    const field = `Migrations[${index}]`;
    checkExactFields(migration, ['MigrationId', 'Resource', 'Reversible', 'Digest'], field, source, errors);
    checkString(migration.MigrationId, `${field}.MigrationId`, source, errors, identifier, 128);
    if (!['core-registration', 'package-database', 'runtime-data'].includes(migration.Resource)) {
      errors.push(`${source}: ${field}.Resource is invalid`);
    }
    if (typeof migration.Reversible !== 'boolean') {
      errors.push(`${source}: ${field}.Reversible must be boolean`);
    }
    checkString(migration.Digest, `${field}.Digest`, source, errors, sha256, 64);
  }, 'MigrationId');
}

function validateFrontend(value, source, errors) {
  if (value === null) {
    return;
  }
  if (!isRecord(value)) {
    errors.push(`${source}: Frontend must be an object or null`);
    return;
  }
  checkExactFields(value, ['ModulePath', 'Sha256', 'ExportedElements'], 'Frontend', source, errors);
  if (typeof value.ModulePath !== 'string'
      || !value.ModulePath.startsWith('frontend/')
      || !value.ModulePath.endsWith('.js')
      || value.ModulePath.split('/').some((segment) => segment === '' || segment === '.' || segment === '..')) {
    errors.push(`${source}: Frontend.ModulePath is invalid`);
  }
  checkString(value.Sha256, 'Frontend.Sha256', source, errors, sha256, 64);
  validateUniqueStrings(value.ExportedElements, 'Frontend.ExportedElements', source, errors, customElement, true);
}

function validateFrontendBindings(value, source, errors) {
  const applications = Array.isArray(value.Applications) ? value.Applications : [];
  const widgets = Array.isArray(value.Widgets) ? value.Widgets : [];
  const surfaceElements = [
    ...applications.map((item) => item?.ElementName).filter((item) => typeof item === 'string'),
    ...widgets.map((item) => item?.ElementName).filter((item) => typeof item === 'string'),
  ];
  const seen = new Set();
  for (const element of surfaceElements) {
    if (seen.has(element)) {
      errors.push(`${source}: application and widget surfaces contain duplicate element '${element}'`);
    }
    seen.add(element);
  }

  if (surfaceElements.length === 0) {
    return;
  }
  if (!isRecord(value.Frontend)) {
    errors.push(`${source}: Frontend is required when applications or widgets are declared`);
    return;
  }
  const exported = Array.isArray(value.Frontend.ExportedElements) ? value.Frontend.ExportedElements : [];
  for (const element of surfaceElements) {
    if (!exported.includes(element)) {
      errors.push(`${source}: Frontend.ExportedElements does not include declared surface '${element}'`);
    }
  }
}

function validateObjectArray(value, field, source, errors, validateItem, identityField) {
  if (!Array.isArray(value)) {
    errors.push(`${source}: ${field} must be an array`);
    return;
  }
  const seen = new Set();
  value.forEach((item, index) => {
    if (!isRecord(item)) {
      errors.push(`${source}: ${field}[${index}] must be an object`);
      return;
    }
    validateItem(item, index);
    const key = typeof identityField === 'function' ? identityField(item) : item[identityField];
    if (seen.has(key)) {
      errors.push(`${source}: ${field} contains duplicate identity '${String(key)}'`);
    }
    seen.add(key);
  });
}

function validateUniqueStrings(value, field, source, errors, pattern, requireNonEmpty = false) {
  if (!Array.isArray(value) || (requireNonEmpty && value.length === 0)) {
    errors.push(`${source}: ${field} must be${requireNonEmpty ? ' a non-empty' : ' an'} array`);
    return;
  }
  const seen = new Set();
  value.forEach((item, index) => {
    checkString(item, `${field}[${index}]`, source, errors, pattern, 512);
    if (seen.has(item)) {
      errors.push(`${source}: ${field} contains duplicate '${String(item)}'`);
    }
    seen.add(item);
  });
}

function validateEnumArray(value, field, allowed, source, errors, requireNonEmpty) {
  if (!Array.isArray(value) || (requireNonEmpty && value.length === 0)) {
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

function checkString(value, field, source, errors, pattern, maximumLength) {
  if (typeof value !== 'string'
      || value.length === 0
      || value.trim() !== value
      || value.length > maximumLength
      || !pattern.test(value)) {
    errors.push(`${source}: ${field} is invalid`);
  }
}

function checkInteger(value, field, source, errors, minimum, maximum) {
  if (!Number.isInteger(value) || value < minimum || value > maximum) {
    errors.push(`${source}: ${field} must be an integer from ${minimum} through ${maximum}`);
  }
}

function checkNumber(value, field, source, errors, exclusiveMinimum, maximum) {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= exclusiveMinimum || value > maximum) {
    errors.push(`${source}: ${field} must be greater than ${exclusiveMinimum} and at most ${maximum}`);
  }
}

function isRecord(value) {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
