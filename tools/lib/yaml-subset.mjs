// CAT-001: a parser for exactly the YAML subset `julos-compose-v1` permits.
//
// docs/APPLICATION_CATALOG.md section 4.2 closes the document grammar: UTF-8 YAML 1.2
// core schema, one document, no custom tags, no anchors, no aliases and no merge keys.
// A general YAML library would accept all of those and the validator would then have to
// detect and reject them afterwards. Parsing only the permitted subset means an
// unsupported construct fails at the point it appears, with its line number, and it also
// avoids adding a dependency for a grammar this narrow.

/** Maximum document size from section 4.2. */
export const maximumDocumentBytes = 1024 * 1024;

export class YamlSubsetError extends Error {
  constructor(message, line) {
    super(line === undefined ? message : `line ${line}: ${message}`);
    this.name = 'YamlSubsetError';
    this.line = line ?? null;
  }
}

/**
 * Parses the permitted subset into plain JavaScript values.
 *
 * Mappings become objects, sequences become arrays, and scalars become strings, numbers,
 * booleans or null following the YAML 1.2 core schema.
 */
export function parseYamlSubset(text) {
  if (typeof text !== 'string') {
    throw new YamlSubsetError('the document must be text');
  }
  if (Buffer.byteLength(text, 'utf8') > maximumDocumentBytes) {
    throw new YamlSubsetError('the document exceeds 1 MiB');
  }

  const lines = [];
  const raw = text.replace(/^\uFEFF/, '').split(/\r?\n/);

  for (const [index, line] of raw.entries()) {
    const number = index + 1;

    if (/^\s*$/.test(line) || /^\s*#/.test(line)) {
      continue;
    }
    if (/^---\s*$/.test(line) || /^\.\.\.\s*$/.test(line)) {
      // A single document may carry the optional start/end markers; a second document
      // body is rejected below by the document counter.
      lines.push({ number, indent: 0, content: '---' });
      continue;
    }
    if (line.includes('\t')) {
      throw new YamlSubsetError('tabs are not permitted for indentation', number);
    }

    const indent = line.length - line.trimStart().length;
    lines.push({ number, indent, content: stripComment(line.trim()) });
  }

  const documents = lines.filter((line) => line.content === '---').length;
  const body = lines.filter((line) => line.content !== '---');
  if (documents > 1) {
    throw new YamlSubsetError('only one document is permitted');
  }
  if (body.length === 0) {
    return {};
  }

  const { value, next } = parseBlock(body, 0, body[0].indent);
  if (next < body.length) {
    throw new YamlSubsetError('unexpected content after the document', body[next].number);
  }
  return value;
}

function stripComment(content) {
  // A '#' only starts a comment when it follows whitespace and is outside a quoted scalar.
  let quote = null;
  for (let index = 0; index < content.length; index += 1) {
    const character = content[index];
    if (quote !== null) {
      if (character === quote) quote = null;
      continue;
    }
    if (character === '"' || character === '\'') {
      quote = character;
      continue;
    }
    if (character === '#' && (index === 0 || /\s/.test(content[index - 1]))) {
      return content.slice(0, index).trimEnd();
    }
  }
  return content;
}

function parseBlock(lines, start, indent) {
  if (lines[start].content.startsWith('- ') || lines[start].content === '-') {
    return parseSequence(lines, start, indent);
  }
  return parseMapping(lines, start, indent);
}

function parseMapping(lines, start, indent) {
  const value = {};
  let index = start;

  while (index < lines.length && lines[index].indent >= indent) {
    const line = lines[index];
    if (line.indent > indent) {
      throw new YamlSubsetError('unexpected indentation', line.number);
    }

    const separator = findKeySeparator(line.content, line.number);
    const key = parseKey(line.content.slice(0, separator), line.number);
    const rest = line.content.slice(separator + 1).trim();

    if (Object.hasOwn(value, key)) {
      throw new YamlSubsetError(`duplicate key '${key}'`, line.number);
    }

    if (rest.length > 0) {
      value[key] = parseScalar(rest, line.number);
      index += 1;
      continue;
    }

    const childIndex = index + 1;
    if (childIndex >= lines.length || lines[childIndex].indent <= indent) {
      value[key] = null;
      index += 1;
      continue;
    }

    const child = parseBlock(lines, childIndex, lines[childIndex].indent);
    value[key] = child.value;
    index = child.next;
  }

  return { value, next: index };
}

function parseSequence(lines, start, indent) {
  const value = [];
  let index = start;

  while (index < lines.length && lines[index].indent === indent) {
    const line = lines[index];
    if (!line.content.startsWith('- ') && line.content !== '-') {
      break;
    }

    const rest = line.content === '-' ? '' : line.content.slice(2).trim();

    if (rest.length === 0) {
      const childIndex = index + 1;
      if (childIndex >= lines.length || lines[childIndex].indent <= indent) {
        value.push(null);
        index += 1;
        continue;
      }
      const child = parseBlock(lines, childIndex, lines[childIndex].indent);
      value.push(child.value);
      index = child.next;
      continue;
    }

    // An inline mapping entry: "- key: value" starts a mapping whose indent is the
    // column the key begins at.
    const separator = tryFindKeySeparator(rest);
    if (separator >= 0) {
      const nested = [{ number: line.number, indent: indent + 2, content: rest }];
      let lookahead = index + 1;
      while (lookahead < lines.length && lines[lookahead].indent > indent) {
        nested.push(lines[lookahead]);
        lookahead += 1;
      }
      const child = parseMapping(nested, 0, indent + 2);
      value.push(child.value);
      index = lookahead;
      continue;
    }

    value.push(parseScalar(rest, line.number));
    index += 1;
  }

  return { value, next: index };
}

function findKeySeparator(content, line) {
  const index = tryFindKeySeparator(content);
  if (index < 0) {
    throw new YamlSubsetError('expected a "key: value" mapping entry', line);
  }
  return index;
}

/** Finds the ':' that separates a mapping key, ignoring colons inside quotes. */
function tryFindKeySeparator(content) {
  let quote = null;
  for (let index = 0; index < content.length; index += 1) {
    const character = content[index];
    if (quote !== null) {
      if (character === quote) quote = null;
      continue;
    }
    if (character === '"' || character === '\'') {
      quote = character;
      continue;
    }
    if (character === ':' && (index + 1 === content.length || /\s/.test(content[index + 1]))) {
      return index;
    }
  }
  return -1;
}

function parseKey(text, line) {
  const key = text.trim();
  if (key.length === 0) {
    throw new YamlSubsetError('a mapping key must not be empty', line);
  }
  if (key === '<<') {
    throw new YamlSubsetError('merge keys are not permitted', line);
  }
  if (key.startsWith('&') || key.startsWith('*') || key.startsWith('!')) {
    throw new YamlSubsetError('anchors, aliases and tags are not permitted', line);
  }
  return unquote(key, line);
}

function parseScalar(text, line) {
  if (text.startsWith('&') || text.startsWith('*') || text.startsWith('!')) {
    throw new YamlSubsetError('anchors, aliases and tags are not permitted', line);
  }
  if (text.startsWith('|') || text.startsWith('>')) {
    throw new YamlSubsetError('block scalars are not permitted', line);
  }

  if (text.startsWith('[') || text.startsWith('{')) {
    return parseFlow(text, line);
  }

  if (text.startsWith('"') || text.startsWith('\'')) {
    return unquote(text, line);
  }

  // YAML 1.2 core schema resolution.
  if (text === 'null' || text === '~') return null;
  if (text === 'true') return true;
  if (text === 'false') return false;
  if (/^-?(?:0|[1-9][0-9]*)$/.test(text)) return Number(text);
  if (/^-?(?:0|[1-9][0-9]*)\.[0-9]+$/.test(text)) return Number(text);

  return text;
}

/** Parses flow sequences and mappings, which Compose files use for short forms. */
function parseFlow(text, line) {
  let index = 0;

  const skipSpace = () => {
    while (index < text.length && /\s/.test(text[index])) index += 1;
  };

  const readValue = () => {
    skipSpace();
    const character = text[index];
    if (character === '[') return readSequence();
    if (character === '{') return readMapping();

    const start = index;
    let quote = null;
    while (index < text.length) {
      const current = text[index];
      if (quote !== null) {
        if (current === quote) quote = null;
      } else if (current === '"' || current === '\'') {
        quote = current;
      } else if (current === ',' || current === ']' || current === '}') {
        break;
      }
      index += 1;
    }
    return parseScalar(text.slice(start, index).trim(), line);
  };

  const readSequence = () => {
    index += 1; // '['
    const items = [];
    skipSpace();
    if (text[index] === ']') {
      index += 1;
      return items;
    }
    for (;;) {
      items.push(readValue());
      skipSpace();
      if (text[index] === ',') {
        index += 1;
        continue;
      }
      if (text[index] === ']') {
        index += 1;
        return items;
      }
      throw new YamlSubsetError('malformed flow sequence', line);
    }
  };

  const readMapping = () => {
    index += 1; // '{'
    const entries = {};
    skipSpace();
    if (text[index] === '}') {
      index += 1;
      return entries;
    }
    for (;;) {
      skipSpace();
      const keyStart = index;
      while (index < text.length && text[index] !== ':') index += 1;
      const key = parseKey(text.slice(keyStart, index), line);
      index += 1; // ':'
      entries[key] = readValue();
      skipSpace();
      if (text[index] === ',') {
        index += 1;
        continue;
      }
      if (text[index] === '}') {
        index += 1;
        return entries;
      }
      throw new YamlSubsetError('malformed flow mapping', line);
    }
  };

  const value = readValue();
  skipSpace();
  if (index < text.length) {
    throw new YamlSubsetError('unexpected content after a flow collection', line);
  }
  return value;
}

function unquote(text, line) {
  if (text.startsWith('"') && text.endsWith('"') && text.length >= 2) {
    return text.slice(1, -1).replace(/\\(["\\nrt])/g, (_, escaped) => ({
      '"': '"', '\\': '\\', n: '\n', r: '\r', t: '\t',
    })[escaped]);
  }
  if (text.startsWith('\'') && text.endsWith('\'') && text.length >= 2) {
    return text.slice(1, -1).replaceAll('\'\'', '\'');
  }
  if (text.includes('"') || text.includes('\'')) {
    throw new YamlSubsetError('unbalanced quotes', line);
  }
  return text;
}
