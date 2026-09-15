// A deliberately small JSON Schema validator covering exactly the keywords the committed
// JulOS contract schemas use. Keeping the schema files authoritative avoids maintaining a
// second hand-written copy of the same rules that could drift from them.
//
// Supported: type, const, enum, required, additionalProperties, properties, items,
// pattern, minLength, maxLength, minimum, maximum, format (uuid, date-time), allOf,
// if/then. An unsupported keyword is reported rather than ignored, so a schema can never
// appear to be enforced when it is not.

const supportedKeywords = new Set([
  '$schema', '$id', 'title', 'description',
  'type', 'const', 'enum', 'required', 'additionalProperties', 'properties', 'items',
  'pattern', 'minLength', 'maxLength', 'minimum', 'maximum', 'format',
  'allOf', 'if', 'then',
]);

const formats = {
  uuid: /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/,
  'date-time': /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/,
};

/** Reports schema keywords this validator does not implement. */
export function findUnsupportedKeywords(schema, path = 'schema') {
  const unsupported = [];
  if (!isRecord(schema)) return unsupported;

  for (const key of Object.keys(schema)) {
    if (!supportedKeywords.has(key)) {
      unsupported.push(`${path}: unsupported schema keyword '${key}'`);
    }
  }

  for (const [key, value] of Object.entries(schema.properties ?? {})) {
    unsupported.push(...findUnsupportedKeywords(value, `${path}.properties.${key}`));
  }
  for (const [index, value] of (schema.allOf ?? []).entries()) {
    unsupported.push(...findUnsupportedKeywords(value, `${path}.allOf[${index}]`));
  }
  if (schema.if) unsupported.push(...findUnsupportedKeywords(schema.if, `${path}.if`));
  if (schema.then) unsupported.push(...findUnsupportedKeywords(schema.then, `${path}.then`));
  if (schema.items) unsupported.push(...findUnsupportedKeywords(schema.items, `${path}.items`));

  return unsupported;
}

/** Validates `value` against `schema` and returns human-readable errors. */
export function validate(value, schema, path = '') {
  const errors = [];
  check(value, schema, path || 'root', errors);
  return errors;
}

/** Returns true when `value` satisfies `schema`, used for `if` branches. */
function matches(value, schema) {
  return check(value, schema, 'probe', []) === 0;
}

function check(value, schema, path, errors) {
  const before = errors.length;

  if (!isRecord(schema)) return 0;

  if (schema.type !== undefined && !matchesType(value, schema.type)) {
    errors.push(`${path}: expected type ${asList(schema.type)}, got ${describe(value)}`);
    return errors.length - before;
  }

  if (schema.const !== undefined && value !== schema.const) {
    errors.push(`${path}: expected ${JSON.stringify(schema.const)}, got ${JSON.stringify(value)}`);
  }

  if (schema.enum !== undefined && !schema.enum.includes(value)) {
    errors.push(`${path}: '${String(value)}' is not one of ${schema.enum.join(', ')}`);
  }

  if (typeof value === 'string') {
    if (schema.pattern !== undefined && !new RegExp(schema.pattern, 'u').test(value)) {
      errors.push(`${path}: '${truncate(value)}' does not match ${schema.pattern}`);
    }
    if (schema.minLength !== undefined && value.length < schema.minLength) {
      errors.push(`${path}: shorter than ${schema.minLength} characters`);
    }
    if (schema.maxLength !== undefined && value.length > schema.maxLength) {
      errors.push(`${path}: longer than ${schema.maxLength} characters`);
    }
    if (schema.format !== undefined && formats[schema.format] && !formats[schema.format].test(value)) {
      errors.push(`${path}: '${truncate(value)}' is not a valid ${schema.format}`);
    }
  }

  if (typeof value === 'number') {
    if (schema.minimum !== undefined && value < schema.minimum) {
      errors.push(`${path}: below minimum ${schema.minimum}`);
    }
    if (schema.maximum !== undefined && value > schema.maximum) {
      errors.push(`${path}: above maximum ${schema.maximum}`);
    }
  }

  if (isRecord(value)) {
    for (const required of schema.required ?? []) {
      if (!Object.hasOwn(value, required)) {
        errors.push(`${path}: missing required field '${required}'`);
      }
    }

    const properties = schema.properties ?? {};
    if (schema.additionalProperties === false) {
      for (const key of Object.keys(value)) {
        if (!Object.hasOwn(properties, key)) {
          errors.push(`${path}: unexpected field '${key}'`);
        }
      }
    }

    for (const [key, propertySchema] of Object.entries(properties)) {
      if (Object.hasOwn(value, key)) {
        check(value[key], propertySchema, `${path}.${key}`, errors);
      }
    }
  }

  if (Array.isArray(value) && schema.items) {
    for (const [index, entry] of value.entries()) {
      check(entry, schema.items, `${path}[${index}]`, errors);
    }
  }

  for (const branch of schema.allOf ?? []) {
    if (branch.if && branch.then) {
      if (matches(value, branch.if)) check(value, branch.then, path, errors);
      continue;
    }
    check(value, branch, path, errors);
  }

  if (schema.if && schema.then && matches(value, schema.if)) {
    check(value, schema.then, path, errors);
  }

  return errors.length - before;
}

function matchesType(value, type) {
  const types = Array.isArray(type) ? type : [type];
  return types.some((candidate) => {
    switch (candidate) {
      case 'object': return isRecord(value);
      case 'array': return Array.isArray(value);
      case 'string': return typeof value === 'string';
      case 'integer': return Number.isInteger(value);
      case 'number': return typeof value === 'number';
      case 'boolean': return typeof value === 'boolean';
      case 'null': return value === null;
      default: return false;
    }
  });
}

function isRecord(value) {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function describe(value) {
  if (value === null) return 'null';
  return Array.isArray(value) ? 'array' : typeof value;
}

function asList(type) {
  return Array.isArray(type) ? type.join(' or ') : type;
}

function truncate(value) {
  return value.length > 60 ? `${value.slice(0, 57)}...` : value;
}
