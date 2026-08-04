import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { unzipSync } from 'fflate';

const root = dirname(fileURLToPath(import.meta.url));
const artifactUrl = 'https://repo.maven.apache.org/maven2/org/apache/guacamole/guacamole-common-js/1.6.0/guacamole-common-js-1.6.0.zip';
const artifactSha256 = '718cde229cfa601c52ddc201afe3f3ed951b8b756957387776d0c460786f0448';
const libraryPath = 'guacamole-common-js/all.min.js';
const librarySha256 = 'cc89f710ecc544477dbe6bfea453fab752dafa1b1ab9770f523676e7b744b44a';

const response = await fetch(artifactUrl, {
  headers: { Accept: 'application/zip' },
  redirect: 'follow',
});
if (!response.ok) {
  throw new Error(`Apache Guacamole download failed with status ${response.status}.`);
}

const archive = new Uint8Array(await response.arrayBuffer());
verifyDigest('Apache Guacamole archive', archive, artifactSha256);
const entries = unzipSync(archive);
const library = entries[libraryPath];
if (library === undefined) {
  throw new Error(`Apache Guacamole archive does not contain '${libraryPath}'.`);
}
verifyDigest('Apache Guacamole browser library', library, librarySha256);

const sourcePath = resolve(root, 'remote.source.js');
const outputPath = resolve(root, 'remote.js');
const manifestPath = resolve(root, '..', 'manifest.json');
const source = stripBom(await readFile(sourcePath, 'utf8'));
const libraryText = new TextDecoder().decode(library);
const output = `${libraryText.trimEnd()}\n${source.trim()}\n`;
await writeFile(outputPath, output, 'utf8');

const manifestText = stripBom(await readFile(manifestPath, 'utf8'));
const manifest = JSON.parse(manifestText);
if (manifest.Frontend === null || typeof manifest.Frontend !== 'object') {
  throw new Error('Remote package manifest has no Frontend section.');
}
manifest.Frontend.Sha256 = digest(new TextEncoder().encode(output));
await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');

function verifyDigest(label, bytes, expected) {
  const actual = digest(bytes);
  if (actual !== expected) {
    throw new Error(`${label} digest mismatch: expected ${expected}, got ${actual}.`);
  }
}

function digest(bytes) {
  return createHash('sha256').update(bytes).digest('hex');
}

function stripBom(value) {
  return value.codePointAt(0) === 0xfeff ? value.slice(1) : value;
}
