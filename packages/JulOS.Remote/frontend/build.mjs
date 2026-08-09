import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const sourcePath = resolve(root, 'remote.source.js');
const outputPath = resolve(root, 'remote.js');
const manifestPath = resolve(root, '..', 'manifest.json');
const source = stripBom(await readFile(sourcePath, 'utf8'));
const outputBytes = canonicalBytes(`${source.trim()}\n`);
await writeFile(outputPath, outputBytes);

const manifestText = stripBom(await readFile(manifestPath, 'utf8'));
const manifest = JSON.parse(manifestText);
if (manifest.Frontend === null || typeof manifest.Frontend !== 'object') {
  throw new Error('Remote package manifest has no Frontend section.');
}
manifest.Frontend.Sha256 = digest(outputBytes);
await writeFile(manifestPath, canonicalBytes(`${JSON.stringify(manifest, null, 2)}\n`));

function digest(bytes) {
  return createHash('sha256').update(bytes).digest('hex');
}

function canonicalBytes(value) {
  const normalized = stripBom(value).replace(/\r?\n/gu, '\r\n');
  return Buffer.from(`\uFEFF${normalized}`, 'utf8');
}

function stripBom(value) {
  return value.codePointAt(0) === 0xfeff ? value.slice(1) : value;
}
