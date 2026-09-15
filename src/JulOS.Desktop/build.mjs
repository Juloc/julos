// Copies the static Desktop assets next to the compiled ES modules and publishes the
// pinned Apache Guacamole browser client used by interactive package displays.

import { createHash } from 'node:crypto';
import { cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { inflateRawSync } from 'node:zlib';

import { renderIcon, renderMaskableIcon } from './raster-icons.mjs';

const projectDirectory = dirname(fileURLToPath(import.meta.url));
const staticDirectory = join(projectDirectory, 'static');
const outputDirectory = join(projectDirectory, 'dist');
const versionFile = join(projectDirectory, '..', '..', 'VERSION');
const buildIdPlaceholder = '__JULOS_BUILD_ID__';
const vendorDirectory = join(outputDirectory, 'vendor');
const artifactUrl = 'https://repo.maven.apache.org/maven2/org/apache/guacamole/guacamole-common-js/1.6.0/guacamole-common-js-1.6.0.zip';
const artifactSha256 = '718cde229cfa601c52ddc201afe3f3ed951b8b756957387776d0c460786f0448';
const libraryPath = 'guacamole-common-js/all.min.js';
const librarySha256 = 'cc89f710ecc544477dbe6bfea453fab752dafa1b1ab9770f523676e7b744b44a';

await mkdir(outputDirectory, { recursive: true });
await cp(staticDirectory, outputDirectory, { recursive: true });
await mkdir(vendorDirectory, { recursive: true });

// The service worker's cache name must change with every release. A constant name would
// make the activate handler's "delete every cache that is not mine" step a no-op forever,
// so a cache-first immutable asset would be served from the previous build indefinitely.
const buildId = (await readFile(versionFile, 'utf8')).replace(/^﻿/, '').trim();
const serviceWorkerSource = join(outputDirectory, 'service-worker.js');
const serviceWorker = await readFile(serviceWorkerSource, 'utf8');
if (!serviceWorker.includes(buildIdPlaceholder)) {
  throw new Error(
    `The service worker must contain ${buildIdPlaceholder} so the build can stamp its cache version.`,
  );
}
await writeFile(
  serviceWorkerSource,
  serviceWorker.replaceAll(buildIdPlaceholder, buildId),
  'utf8',
);

// iOS Safari ignores the manifest icon list and installs the PNG from
// <link rel="apple-touch-icon">, so raster icons are required for installability. They
// are rasterized from the same shapes as the SVGs rather than committed as binaries that
// could drift from them.
const iconDirectory = join(outputDirectory, 'icons');
await mkdir(iconDirectory, { recursive: true });
for (const size of [180, 192, 512]) {
  await writeFile(join(iconDirectory, `julos-${size}.png`), renderIcon(size));
}
await writeFile(join(iconDirectory, 'julos-maskable-512.png'), renderMaskableIcon(512));

const response = await fetch(artifactUrl, {
  headers: { Accept: 'application/zip' },
  redirect: 'follow',
});
if (!response.ok) {
  throw new Error(`Apache Guacamole download failed with status ${response.status}.`);
}

const archive = new Uint8Array(await response.arrayBuffer());
verifyDigest('Apache Guacamole archive', archive, artifactSha256);
const library = readZipEntry(archive, libraryPath);
verifyDigest('Apache Guacamole browser library', library, librarySha256);
await writeFile(join(vendorDirectory, 'guacamole-common-js-1.6.0.js'), library);

console.log(`Copied static assets and verified Guacamole client to ${outputDirectory}`);

function verifyDigest(label, bytes, expected) {
  const actual = createHash('sha256').update(bytes).digest('hex');
  if (actual !== expected) {
    throw new Error(`${label} digest mismatch: expected ${expected}, got ${actual}.`);
  }
}

function readZipEntry(bytes, wantedName) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const eocdOffset = findEndOfCentralDirectory(view);
  const entryCount = view.getUint16(eocdOffset + 10, true);
  let offset = view.getUint32(eocdOffset + 16, true);

  for (let index = 0; index < entryCount; index += 1) {
    if (view.getUint32(offset, true) !== 0x02014b50) {
      throw new Error('Apache Guacamole ZIP central directory is invalid.');
    }
    const method = view.getUint16(offset + 10, true);
    const compressedSize = view.getUint32(offset + 20, true);
    const uncompressedSize = view.getUint32(offset + 24, true);
    const nameLength = view.getUint16(offset + 28, true);
    const extraLength = view.getUint16(offset + 30, true);
    const commentLength = view.getUint16(offset + 32, true);
    const localOffset = view.getUint32(offset + 42, true);
    const name = new TextDecoder().decode(bytes.subarray(offset + 46, offset + 46 + nameLength));

    if (name === wantedName) {
      if (view.getUint32(localOffset, true) !== 0x04034b50) {
        throw new Error('Apache Guacamole ZIP local header is invalid.');
      }
      const localNameLength = view.getUint16(localOffset + 26, true);
      const localExtraLength = view.getUint16(localOffset + 28, true);
      const dataOffset = localOffset + 30 + localNameLength + localExtraLength;
      const compressed = bytes.subarray(dataOffset, dataOffset + compressedSize);
      const output = method === 0
        ? Buffer.from(compressed)
        : method === 8
          ? inflateRawSync(compressed)
          : null;
      if (output === null || output.byteLength !== uncompressedSize) {
        throw new Error(`Apache Guacamole ZIP entry '${wantedName}' has an unsupported encoding.`);
      }
      return new Uint8Array(output.buffer, output.byteOffset, output.byteLength);
    }

    offset += 46 + nameLength + extraLength + commentLength;
  }

  throw new Error(`Apache Guacamole ZIP does not contain '${wantedName}'.`);
}

function findEndOfCentralDirectory(view) {
  const minimumOffset = Math.max(0, view.byteLength - 65557);
  for (let offset = view.byteLength - 22; offset >= minimumOffset; offset -= 1) {
    if (view.getUint32(offset, true) === 0x06054b50) {
      return offset;
    }
  }
  throw new Error('Apache Guacamole ZIP end-of-central-directory record is missing.');
}
