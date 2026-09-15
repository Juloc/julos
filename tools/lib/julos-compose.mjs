// CAT-001: the closed `julos-compose-v1` value grammar from
// docs/APPLICATION_CATALOG.md sections 4.1 and 4.2, plus deterministic normalization.
//
// `julos-compose-v1` is the normative baseline, not "whatever the installed Compose
// version accepts". Every unsupported key, value shape and cross-field combination fails
// with `catalog.compose_feature_unsupported` or `catalog.definition_invalid`; nothing is
// silently ignored or passed through as executable extension data.

import { createHash } from 'node:crypto';

import { parseYamlSubset, YamlSubsetError } from './yaml-subset.mjs';

export const composeErrorCodes = Object.freeze({
  unsupported: 'catalog.compose_feature_unsupported',
  invalid: 'catalog.definition_invalid',
});

const topLevelKeys = new Set(['name', 'services', 'volumes', 'networks', 'x-julos']);

const serviceKeys = new Set([
  'image', 'entrypoint', 'command', 'environment', 'ports', 'expose', 'volumes',
  'networks', 'depends_on', 'restart', 'healthcheck', 'labels', 'user', 'working_dir',
  'read_only', 'tmpfs', 'init', 'stop_grace_period', 'cap_add', 'cap_drop',
  'security_opt', 'devices', 'privileged', 'network_mode', 'pid', 'ipc', 'hostname',
]);

/** Keys that are rejected outright, with the reason the specification gives. */
const rejectedKeys = new Map([
  ['build', 'images are pulled, never built from a catalog'],
  ['extends', 'cross-file inheritance is not supported'],
  ['include', 'cross-file inclusion is not supported'],
  ['develop', 'development watch mode is not supported'],
  ['deploy', 'Swarm deployment is not supported'],
  ['profiles', 'implicitly selected profiles are not supported'],
  ['container_name', 'container names are owned by JulOS'],
  ['env_file', 'environment files from catalog paths are not supported'],
  ['configs', 'configs sourced from host files are not supported'],
  ['secrets', 'secrets sourced from host files are not supported; use Secret Bindings'],
  ['version', 'the obsolete top-level version key is not supported'],
]);

const nameRule = /^[A-Za-z0-9][A-Za-z0-9_.-]{0,62}$/;
const durationRule = /^(?:\d+(?:ns|us|ms|s|m|h))+$/;
const parameterToken = /\{\{julos:param:([a-z][a-z0-9_-]{0,63})\}\}/g;

/** One right that must be acknowledged before apply and may be denied by host policy. */
function right(service, field, value) {
  return { service, field, value: String(value) };
}

/**
 * Validates and normalizes a `julos-compose-v1` document.
 *
 * Returns `{ errors, plan, criticalRights }`. `plan` is the deterministic typed
 * deployment plan; it is `null` when any error was found.
 */
export function validateJulosCompose(text, options = {}) {
  const declaredParameters = new Set(options.parameters ?? []);
  const errors = [];
  const criticalRights = [];

  let document;
  try {
    document = parseYamlSubset(text);
  } catch (error) {
    if (error instanceof YamlSubsetError) {
      return { errors: [`${composeErrorCodes.invalid}: ${error.message}`], plan: null, criticalRights };
    }
    throw error;
  }

  if (!isRecord(document)) {
    return { errors: [`${composeErrorCodes.invalid}: the document root must be a mapping`], plan: null, criticalRights };
  }

  for (const key of Object.keys(document)) {
    if (rejectedKeys.has(key)) {
      errors.push(`${composeErrorCodes.unsupported}: '${key}' is not supported (${rejectedKeys.get(key)})`);
    } else if (!topLevelKeys.has(key)) {
      errors.push(`${composeErrorCodes.unsupported}: unknown top-level key '${key}'`);
    }
  }

  checkInterpolation(document, errors, declaredParameters);

  if (document.name !== undefined && document.name !== null && !nameRule.test(String(document.name))) {
    errors.push(`${composeErrorCodes.invalid}: 'name' must match ${nameRule}`);
  }

  const services = document.services;
  if (!isRecord(services)) {
    errors.push(`${composeErrorCodes.invalid}: 'services' must be a mapping`);
    return { errors, plan: null, criticalRights };
  }

  const serviceNames = Object.keys(services);
  if (serviceNames.length < 1 || serviceNames.length > 64) {
    errors.push(`${composeErrorCodes.invalid}: a document declares 1 to 64 services`);
  }

  const declaredVolumes = collectTopLevel(document.volumes, 'volumes', errors);
  const declaredNetworks = collectTopLevel(document.networks, 'networks', errors);

  const normalizedServices = {};
  const publishedPorts = new Set();

  for (const name of serviceNames.slice().sort()) {
    if (!nameRule.test(name)) {
      errors.push(`${composeErrorCodes.invalid}: service name '${name}' must match ${nameRule}`);
      continue;
    }
    normalizedServices[name] = normalizeService(
      name,
      services[name],
      { serviceNames, declaredVolumes, declaredNetworks, publishedPorts },
      errors,
      criticalRights,
    );
  }

  if (errors.length > 0) {
    return { errors, plan: null, criticalRights };
  }

  const plan = {
    schema: 'julos-compose-v1',
    name: document.name === undefined || document.name === null ? null : String(document.name),
    services: normalizedServices,
    volumes: declaredVolumes,
    networks: declaredNetworks,
    // x-julos is presentation and lifecycle metadata. It is carried through verbatim and
    // is never executable.
    metadata: document['x-julos'] ?? null,
  };

  return { errors, plan, criticalRights };
}

/** The canonical digest of a plan: RFC 8785-style ordered JSON, lowercase SHA-256. */
export function planDigest(plan) {
  return createHash('sha256').update(canonicalJson(plan)).digest('hex');
}

/** Deterministic JSON with object keys sorted, so reparsing produces the same bytes. */
export function canonicalJson(value) {
  if (value === null || typeof value !== 'object') {
    return JSON.stringify(value ?? null);
  }
  if (Array.isArray(value)) {
    return `[${value.map(canonicalJson).join(',')}]`;
  }
  const entries = Object.keys(value)
    .sort()
    .map((key) => `${JSON.stringify(key)}:${canonicalJson(value[key])}`);
  return `{${entries.join(',')}}`;
}

function normalizeService(name, service, context, errors, criticalRights) {
  if (!isRecord(service)) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' must be a mapping`);
    return {};
  }

  for (const key of Object.keys(service)) {
    if (rejectedKeys.has(key)) {
      errors.push(
        `${composeErrorCodes.unsupported}: service '${name}' uses '${key}' (${rejectedKeys.get(key)})`,
      );
    } else if (!serviceKeys.has(key)) {
      errors.push(`${composeErrorCodes.unsupported}: service '${name}' has unknown key '${key}'`);
    }
  }

  const normalized = {};

  if (typeof service.image !== 'string' || service.image.trim().length === 0) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' requires a non-empty image`);
  } else {
    normalized.image = service.image.trim();
  }

  normalized.entrypoint = normalizeStringList(service.entrypoint, `${name}.entrypoint`, errors);
  normalized.command = normalizeStringList(service.command, `${name}.command`, errors);
  normalized.environment = normalizeKeyValue(service.environment, `${name}.environment`, errors);
  normalized.labels = normalizeKeyValue(service.labels, `${name}.labels`, errors, true);
  normalized.ports = normalizePorts(name, service.ports, context.publishedPorts, errors);
  normalized.expose = normalizeExpose(name, service.expose, errors);
  normalized.volumes = normalizeVolumes(name, service.volumes, context.declaredVolumes, errors, criticalRights);
  normalized.networks = normalizeNetworks(name, service.networks, context.declaredNetworks, errors);
  normalized.dependsOn = normalizeDependsOn(name, service.depends_on, context.serviceNames, errors);
  normalized.restart = normalizeRestart(name, service.restart, errors);
  normalized.healthcheck = normalizeHealthcheck(name, service.healthcheck, errors);

  normalized.user = normalizeOptionalString(service.user, `${name}.user`, errors);
  normalized.hostname = normalizeOptionalString(service.hostname, `${name}.hostname`, errors);
  normalized.workingDir = normalizeOptionalString(service.working_dir, `${name}.working_dir`, errors);
  if (normalized.workingDir !== null && !normalized.workingDir.startsWith('/')) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' working_dir must be absolute`);
  }

  normalized.readOnly = normalizeBoolean(service.read_only, `${name}.read_only`, errors);
  normalized.init = normalizeBoolean(service.init, `${name}.init`, errors);
  normalized.privileged = normalizeBoolean(service.privileged, `${name}.privileged`, errors);
  if (normalized.privileged === true) {
    criticalRights.push(right(name, 'privileged', true));
  }

  normalized.tmpfs = normalizeTmpfs(name, service.tmpfs, errors);
  normalized.stopGracePeriod = normalizeDuration(service.stop_grace_period, `${name}.stop_grace_period`, errors);

  normalized.capAdd = normalizeCapabilities(name, service.cap_add, errors);
  for (const capability of normalized.capAdd) {
    criticalRights.push(right(name, 'cap_add', capability));
  }
  normalized.capDrop = normalizeCapabilities(name, service.cap_drop, errors);

  normalized.securityOpt = normalizeSecurityOpt(name, service.security_opt, errors, criticalRights);
  normalized.devices = normalizeDevices(name, service.devices, errors, criticalRights);

  normalized.networkMode = normalizeNamespace(
    name, service.network_mode, 'network_mode', ['bridge', 'none', 'host'], context.serviceNames, errors, criticalRights,
  );
  normalized.pid = normalizeNamespace(
    name, service.pid, 'pid', ['private', 'host'], context.serviceNames, errors, criticalRights,
  );
  normalized.ipc = normalizeNamespace(
    name, service.ipc, 'ipc', ['private', 'host'], context.serviceNames, errors, criticalRights,
  );

  if (normalized.networkMode === 'host' && normalized.ports.length > 0) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' cannot publish ports with network_mode host`);
  }

  return normalized;
}

function normalizeStringList(value, field, errors) {
  if (value === undefined || value === null) return null;
  if (typeof value === 'string') return [value];
  if (Array.isArray(value) && value.every((entry) => typeof entry === 'string')) {
    return value.slice();
  }
  errors.push(`${composeErrorCodes.invalid}: ${field} must be a string or an array of strings`);
  return null;
}

function normalizeKeyValue(value, field, errors, forbidReserved = false) {
  if (value === undefined || value === null) return {};

  const result = {};
  const assign = (key, entryValue) => {
    if (forbidReserved && key.toLowerCase().startsWith('com.juloc.julos.')) {
      errors.push(`${composeErrorCodes.invalid}: ${field} must not use the reserved 'com.juloc.julos.*' namespace`);
      return;
    }
    result[key] = entryValue;
  };

  if (Array.isArray(value)) {
    for (const entry of value) {
      if (typeof entry !== 'string' || !entry.includes('=')) {
        // A bare key reads the host environment, which is why it is rejected.
        errors.push(`${composeErrorCodes.invalid}: ${field} array entries must be 'KEY=value'`);
        continue;
      }
      const index = entry.indexOf('=');
      assign(entry.slice(0, index), entry.slice(index + 1));
    }
    return sortKeys(result);
  }

  if (!isRecord(value)) {
    errors.push(`${composeErrorCodes.invalid}: ${field} must be a mapping or a 'KEY=value' array`);
    return {};
  }

  for (const [key, entry] of Object.entries(value)) {
    if (entry === null) {
      errors.push(`${composeErrorCodes.invalid}: ${field}.${key} must not be null, which reads the host environment`);
      continue;
    }
    assign(key, typeof entry === 'string' ? entry : String(entry));
  }
  return sortKeys(result);
}

function normalizePorts(name, value, publishedPorts, errors) {
  if (value === undefined || value === null) return [];
  if (!Array.isArray(value)) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' ports must be an array`);
    return [];
  }

  const ports = [];
  for (const entry of value) {
    const port = typeof entry === 'string' || typeof entry === 'number'
      ? parseShortPort(name, String(entry), errors)
      : parseLongPort(name, entry, errors);
    if (port === null) continue;

    const key = `${port.hostIp ?? ''}:${port.published}/${port.protocol}`;
    if (publishedPorts.has(key)) {
      errors.push(`${composeErrorCodes.invalid}: published port ${port.published}/${port.protocol} is declared twice`);
    }
    publishedPorts.add(key);
    ports.push(port);
  }

  return ports.sort((left, righthand) => left.published - righthand.published);
}

function parseShortPort(name, entry, errors) {
  const match = /^(?:(\d{1,3}(?:\.\d{1,3}){3}):)?(\d+):(\d+)(?:\/(tcp|udp))?$/.exec(entry);
  if (match === null) {
    errors.push(
      `${composeErrorCodes.unsupported}: service '${name}' port '${entry}' is not '[IP:]PUBLISHED:TARGET[/tcp|udp]'; ranges and random published ports are rejected`,
    );
    return null;
  }
  return buildPort(name, match[1] ?? null, Number(match[2]), Number(match[3]), match[4] ?? 'tcp', 'host', errors);
}

const longPortKeys = new Set(['target', 'published', 'host_ip', 'protocol', 'name', 'app_protocol', 'mode']);

function parseLongPort(name, entry, errors) {
  if (!isRecord(entry)) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' has an invalid port entry`);
    return null;
  }
  for (const key of Object.keys(entry)) {
    if (!longPortKeys.has(key)) {
      errors.push(`${composeErrorCodes.unsupported}: service '${name}' port has unknown key '${key}'`);
    }
  }
  if (entry.mode !== undefined && entry.mode !== 'host') {
    errors.push(`${composeErrorCodes.unsupported}: service '${name}' port mode must be 'host'`);
  }
  return buildPort(
    name,
    entry.host_ip ?? null,
    Number(entry.published),
    Number(entry.target),
    entry.protocol ?? 'tcp',
    'host',
    errors,
  );
}

function buildPort(name, hostIp, published, target, protocol, mode, errors) {
  for (const [label, port] of [['published', published], ['target', target]]) {
    if (!Number.isInteger(port) || port < 1 || port > 65535) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' ${label} port must be 1-65535`);
      return null;
    }
  }
  if (protocol !== 'tcp' && protocol !== 'udp') {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' port protocol must be tcp or udp`);
    return null;
  }
  return { hostIp: hostIp === null ? null : String(hostIp), published, target, protocol, mode };
}

function normalizeExpose(name, value, errors) {
  if (value === undefined || value === null) return [];
  if (!Array.isArray(value)) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' expose must be an array`);
    return [];
  }

  const exposed = [];
  for (const entry of value) {
    const match = /^(\d+)(?:\/(tcp|udp))?$/.exec(String(entry));
    if (match === null) {
      errors.push(`${composeErrorCodes.unsupported}: service '${name}' expose '${entry}' must be a single port; ranges are rejected`);
      continue;
    }
    const port = Number(match[1]);
    if (port < 1 || port > 65535) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' expose port must be 1-65535`);
      continue;
    }
    exposed.push({ target: port, protocol: match[2] ?? 'tcp' });
  }
  return exposed.sort((left, righthand) => left.target - righthand.target);
}

const longVolumeKeys = new Set(['type', 'source', 'target', 'read_only', 'bind', 'volume', 'tmpfs']);

function normalizeVolumes(name, value, declaredVolumes, errors, criticalRights) {
  if (value === undefined || value === null) return [];
  if (!Array.isArray(value)) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' volumes must be an array`);
    return [];
  }

  const mounts = [];
  const targets = new Set();

  for (const entry of value) {
    const mount = typeof entry === 'string'
      ? parseShortVolume(name, entry, errors)
      : parseLongVolume(name, entry, errors);
    if (mount === null) continue;

    if (mount.type === 'volume' && !declaredVolumes.some((volume) => volume.key === mount.source)) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' uses undeclared volume '${mount.source}'`);
    }
    if (mount.type === 'bind') {
      criticalRights.push(right(name, 'volumes', `${mount.source}:${mount.target}`));
    }
    if (targets.has(mount.target)) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' mounts '${mount.target}' twice`);
    }
    targets.add(mount.target);
    mounts.push(mount);
  }

  return mounts.sort((left, righthand) => left.target.localeCompare(righthand.target));
}

function parseShortVolume(name, entry, errors) {
  const parts = entry.split(':');
  if (parts.length < 2 || parts.length > 3) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' volume '${entry}' is malformed`);
    return null;
  }
  const [source, target, mode] = parts;
  if (!target.startsWith('/')) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' volume target must be absolute`);
    return null;
  }
  if (mode !== undefined && mode !== 'ro' && mode !== 'rw') {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' volume mode must be ro or rw`);
    return null;
  }
  if (source.startsWith('.') || source.length === 0) {
    errors.push(`${composeErrorCodes.unsupported}: service '${name}' relative and anonymous volume sources are rejected`);
    return null;
  }
  return {
    type: source.startsWith('/') ? 'bind' : 'volume',
    source,
    target,
    readOnly: mode === 'ro',
  };
}

function parseLongVolume(name, entry, errors) {
  if (!isRecord(entry)) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' has an invalid volume entry`);
    return null;
  }
  for (const key of Object.keys(entry)) {
    if (!longVolumeKeys.has(key)) {
      errors.push(`${composeErrorCodes.unsupported}: service '${name}' volume has unknown key '${key}'`);
    }
  }
  const type = entry.type;
  if (type !== 'bind' && type !== 'volume' && type !== 'tmpfs') {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' volume type must be bind, volume or tmpfs`);
    return null;
  }
  const target = String(entry.target ?? '');
  if (!target.startsWith('/')) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' volume target must be absolute`);
    return null;
  }
  if (type === 'bind' && !String(entry.source ?? '').startsWith('/')) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' bind source must be an absolute host path`);
    return null;
  }
  return {
    type,
    source: entry.source === undefined ? null : String(entry.source),
    target,
    readOnly: entry.read_only === true,
  };
}

function normalizeNetworks(name, value, declaredNetworks, errors) {
  if (value === undefined || value === null) return [];

  const attach = [];
  if (Array.isArray(value)) {
    for (const entry of value) {
      attach.push({ network: String(entry), aliases: [] });
    }
  } else if (isRecord(value)) {
    for (const [network, settings] of Object.entries(value)) {
      if (settings !== null && !isRecord(settings)) {
        errors.push(`${composeErrorCodes.invalid}: service '${name}' network '${network}' must be a mapping`);
        continue;
      }
      for (const key of Object.keys(settings ?? {})) {
        if (key !== 'aliases') {
          errors.push(
            `${composeErrorCodes.unsupported}: service '${name}' network '${network}' key '${key}' is rejected in v1`,
          );
        }
      }
      attach.push({ network, aliases: (settings?.aliases ?? []).map(String).sort() });
    }
  } else {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' networks must be an array or mapping`);
    return [];
  }

  for (const entry of attach) {
    if (!declaredNetworks.some((network) => network.key === entry.network)) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' uses undeclared network '${entry.network}'`);
    }
  }

  return attach.sort((left, righthand) => left.network.localeCompare(righthand.network));
}

const dependsOnConditions = new Set(['service_started', 'service_healthy', 'service_completed_successfully']);

function normalizeDependsOn(name, value, serviceNames, errors) {
  if (value === undefined || value === null) return [];

  const dependencies = [];
  if (Array.isArray(value)) {
    for (const entry of value) {
      dependencies.push({ service: String(entry), condition: 'service_started', restart: false, required: true });
    }
  } else if (isRecord(value)) {
    for (const [service, settings] of Object.entries(value)) {
      if (!isRecord(settings)) {
        errors.push(`${composeErrorCodes.invalid}: service '${name}' depends_on '${service}' must be a mapping`);
        continue;
      }
      for (const key of Object.keys(settings)) {
        if (key !== 'condition' && key !== 'restart' && key !== 'required') {
          errors.push(`${composeErrorCodes.unsupported}: service '${name}' depends_on has unknown key '${key}'`);
        }
      }
      const condition = settings.condition ?? 'service_started';
      if (!dependsOnConditions.has(condition)) {
        errors.push(`${composeErrorCodes.invalid}: service '${name}' depends_on condition '${condition}' is not supported`);
        continue;
      }
      dependencies.push({
        service,
        condition,
        restart: settings.restart === true,
        required: settings.required !== false,
      });
    }
  } else {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' depends_on must be an array or mapping`);
    return [];
  }

  for (const dependency of dependencies) {
    if (!serviceNames.includes(dependency.service)) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' depends on undeclared service '${dependency.service}'`);
    }
    if (dependency.service === name) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' depends on itself`);
    }
  }

  return dependencies.sort((left, righthand) => left.service.localeCompare(righthand.service));
}

function normalizeRestart(name, value, errors) {
  if (value === undefined || value === null) return null;
  const restart = String(value);
  if (['no', 'always', 'unless-stopped'].includes(restart)) return restart;
  const match = /^on-failure(?::(\d+))?$/.exec(restart);
  if (match !== null && (match[1] === undefined || (Number(match[1]) >= 1 && Number(match[1]) <= 100))) {
    return restart;
  }
  errors.push(`${composeErrorCodes.invalid}: service '${name}' restart '${restart}' is not supported`);
  return null;
}

const healthcheckKeys = new Set(['test', 'interval', 'timeout', 'start_period', 'start_interval', 'retries', 'disable']);

function normalizeHealthcheck(name, value, errors) {
  if (value === undefined || value === null) return null;
  if (!isRecord(value)) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' healthcheck must be a mapping`);
    return null;
  }
  for (const key of Object.keys(value)) {
    if (!healthcheckKeys.has(key)) {
      errors.push(`${composeErrorCodes.unsupported}: service '${name}' healthcheck has unknown key '${key}'`);
    }
  }

  const test = value.test;
  if (test !== undefined && test !== 'NONE') {
    if (!Array.isArray(test) || (test[0] !== 'CMD' && test[0] !== 'CMD-SHELL')) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' healthcheck test must be NONE or begin with CMD or CMD-SHELL`);
    }
  }

  for (const key of ['interval', 'timeout', 'start_period', 'start_interval']) {
    if (value[key] !== undefined) {
      normalizeDuration(value[key], `${name}.healthcheck.${key}`, errors);
    }
  }

  if (value.retries !== undefined) {
    const retries = Number(value.retries);
    if (!Number.isInteger(retries) || retries < 1 || retries > 100) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' healthcheck retries must be 1-100`);
    }
  }

  return {
    test: test === undefined ? null : (Array.isArray(test) ? test.slice() : String(test)),
    interval: value.interval === undefined ? null : String(value.interval),
    timeout: value.timeout === undefined ? null : String(value.timeout),
    startPeriod: value.start_period === undefined ? null : String(value.start_period),
    startInterval: value.start_interval === undefined ? null : String(value.start_interval),
    retries: value.retries === undefined ? null : Number(value.retries),
    disable: value.disable === true,
  };
}

function normalizeDuration(value, field, errors) {
  if (value === undefined || value === null) return null;
  const duration = String(value);
  if (!durationRule.test(duration)) {
    errors.push(`${composeErrorCodes.invalid}: ${field} must be a duration such as '30s' or '1m30s'`);
    return null;
  }
  return duration;
}

function normalizeTmpfs(name, value, errors) {
  if (value === undefined || value === null) return [];
  const entries = Array.isArray(value) ? value : [value];
  const paths = [];
  for (const entry of entries) {
    const text = String(entry);
    const [path] = text.split(':');
    if (!path.startsWith('/')) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' tmpfs path must be absolute`);
      continue;
    }
    paths.push(text);
  }
  return paths.sort();
}

function normalizeCapabilities(name, value, errors) {
  if (value === undefined || value === null) return [];
  if (!Array.isArray(value)) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' capability list must be an array`);
    return [];
  }
  const capabilities = [];
  for (const entry of value) {
    const capability = String(entry);
    if (!/^[A-Z][A-Z0-9_]*$/.test(capability)) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' capability '${capability}' must be an uppercase Linux capability`);
      continue;
    }
    capabilities.push(capability);
  }
  return capabilities.sort();
}

const allowedSecurityOpt = new Set(['no-new-privileges:true', 'seccomp=unconfined', 'apparmor=unconfined']);

function normalizeSecurityOpt(name, value, errors, criticalRights) {
  if (value === undefined || value === null) return [];
  if (!Array.isArray(value)) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' security_opt must be an array`);
    return [];
  }
  const options = [];
  for (const entry of value) {
    const option = String(entry);
    if (!allowedSecurityOpt.has(option)) {
      errors.push(`${composeErrorCodes.unsupported}: service '${name}' security_opt '${option}' is not supported`);
      continue;
    }
    if (option.endsWith('=unconfined')) {
      criticalRights.push(right(name, 'security_opt', option));
    }
    options.push(option);
  }
  return options.sort();
}

function normalizeDevices(name, value, errors, criticalRights) {
  if (value === undefined || value === null) return [];
  if (!Array.isArray(value)) {
    errors.push(`${composeErrorCodes.invalid}: service '${name}' devices must be an array`);
    return [];
  }
  const devices = [];
  for (const entry of value) {
    const device = String(entry);
    if (!/^\/[^:]+:\/[^:]+(?::[rwm]{1,3})?$/.test(device)) {
      errors.push(
        `${composeErrorCodes.unsupported}: service '${name}' device '${device}' must be 'ABSOLUTE_HOST:ABSOLUTE_CONTAINER[:rwm]'`,
      );
      continue;
    }
    criticalRights.push(right(name, 'devices', device));
    devices.push(device);
  }
  return devices.sort();
}

function normalizeNamespace(name, value, field, literals, serviceNames, errors, criticalRights) {
  if (value === undefined || value === null) return null;
  const mode = String(value);

  if (literals.includes(mode)) {
    if (mode === 'host') {
      criticalRights.push(right(name, field, mode));
    }
    return mode;
  }

  const match = /^service:(.+)$/.exec(mode);
  if (match !== null) {
    if (!serviceNames.includes(match[1])) {
      errors.push(`${composeErrorCodes.invalid}: service '${name}' ${field} references undeclared service '${match[1]}'`);
    }
    return mode;
  }

  errors.push(`${composeErrorCodes.unsupported}: service '${name}' ${field} '${mode}' is not supported`);
  return null;
}

const topLevelVolumeKeys = new Set(['name', 'external', 'labels']);
const topLevelNetworkKeys = new Set(['name', 'external', 'driver', 'internal', 'attachable', 'labels']);

function collectTopLevel(value, field, errors) {
  if (value === undefined || value === null) return [];
  if (!isRecord(value)) {
    errors.push(`${composeErrorCodes.invalid}: '${field}' must be a mapping`);
    return [];
  }

  const allowed = field === 'volumes' ? topLevelVolumeKeys : topLevelNetworkKeys;
  const entries = [];

  for (const key of Object.keys(value).sort()) {
    if (!nameRule.test(key)) {
      errors.push(`${composeErrorCodes.invalid}: ${field} name '${key}' must match ${nameRule}`);
      continue;
    }
    const settings = value[key];
    if (settings !== null && !isRecord(settings)) {
      errors.push(`${composeErrorCodes.invalid}: ${field}.${key} must be null or a mapping`);
      continue;
    }
    for (const setting of Object.keys(settings ?? {})) {
      if (!allowed.has(setting)) {
        errors.push(`${composeErrorCodes.unsupported}: ${field}.${key} has unknown key '${setting}'`);
      }
    }

    const external = settings?.external === true;
    if (external && settings?.name === undefined) {
      errors.push(`${composeErrorCodes.invalid}: external ${field}.${key} requires an explicit name`);
    }
    if (external && (settings?.driver !== undefined || settings?.internal !== undefined || settings?.attachable !== undefined)) {
      errors.push(`${composeErrorCodes.invalid}: external ${field}.${key} must not declare creation settings`);
    }
    if (field === 'networks' && settings?.driver !== undefined && settings.driver !== 'bridge') {
      errors.push(`${composeErrorCodes.unsupported}: networks.${key} driver '${settings.driver}' is not supported in v1`);
    }

    entries.push({ key, name: settings?.name === undefined ? null : String(settings.name), external });
  }

  if (entries.length > 128) {
    errors.push(`${composeErrorCodes.invalid}: at most 128 ${field} may be declared`);
  }

  return entries;
}

/**
 * Rejects host-environment interpolation and validates catalog parameter tokens.
 *
 * `$$` is one literal `$`; every other `$NAME` or `${...}` form reads the host
 * environment and is rejected. Ordinary parameters expand only from exact
 * `{{julos:param:<key>}}` tokens whose key is declared.
 */
function checkInterpolation(value, errors, declaredParameters, path = '') {
  if (typeof value === 'string') {
    const withoutEscapes = value.replaceAll('$$', '');
    if (/\$\{|\$[A-Za-z_]/.test(withoutEscapes)) {
      errors.push(
        `${composeErrorCodes.unsupported}: ${path || 'document'} uses host-environment interpolation; '$$' is the only accepted '$' form`,
      );
    }
    for (const match of value.matchAll(parameterToken)) {
      if (!declaredParameters.has(match[1])) {
        errors.push(
          `${composeErrorCodes.invalid}: ${path || 'document'} uses undeclared parameter '${match[1]}'`,
        );
      }
    }
    return;
  }
  if (Array.isArray(value)) {
    value.forEach((entry, index) => checkInterpolation(entry, errors, declaredParameters, `${path}[${index}]`));
    return;
  }
  if (isRecord(value)) {
    for (const [key, entry] of Object.entries(value)) {
      checkInterpolation(entry, errors, declaredParameters, path === '' ? key : `${path}.${key}`);
    }
  }
}

function normalizeOptionalString(value, field, errors) {
  if (value === undefined || value === null) return null;
  if (typeof value !== 'string') {
    errors.push(`${composeErrorCodes.invalid}: ${field} must be a string`);
    return null;
  }
  return value;
}

function normalizeBoolean(value, field, errors) {
  if (value === undefined || value === null) return null;
  if (typeof value !== 'boolean') {
    errors.push(`${composeErrorCodes.invalid}: ${field} must be a boolean`);
    return null;
  }
  return value;
}

function sortKeys(record) {
  return Object.fromEntries(Object.entries(record).sort(([left], [righthand]) => left.localeCompare(righthand)));
}

function isRecord(value) {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
