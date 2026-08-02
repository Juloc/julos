import assert from 'node:assert/strict';
import { test } from 'node:test';

import { LauncherAuthorizationError, LauncherCatalog, type LauncherEntry } from './launcher.js';

test('unauthorized entries are neither searchable nor executable', async () => {
  let executionCount = 0;
  const catalog = new LauncherCatalog([
    {
      id: 'docker.stop',
      kind: 'command',
      title: 'Stop container',
      requiredPermissions: ['docker.control'],
      execute: () => {
        executionCount += 1;
      },
    },
  ]);

  assert.deepEqual(catalog.search('stop', new Set()), []);
  await assert.rejects(
    catalog.execute('docker.stop', new Set()),
    (error: unknown) => error instanceof LauncherAuthorizationError,
  );
  assert.equal(executionCount, 0);
});

test('authorized commands execute exactly once', async () => {
  let executionCount = 0;
  const catalog = new LauncherCatalog([
    {
      id: 'docker.stop',
      kind: 'command',
      title: 'Stop container',
      requiredPermissions: ['docker.control'],
      execute: () => {
        executionCount += 1;
      },
    },
  ]);
  const permissions = new Set(['docker.control']);

  assert.equal(catalog.search('stop', permissions).length, 1);
  await catalog.execute('docker.stop', permissions);
  assert.equal(executionCount, 1);
});

test('search ranks exact title and prefix matches ahead of keyword matches', () => {
  const catalog = new LauncherCatalog([
    { id: 'app.files', kind: 'application', title: 'Files', keywords: ['storage'] },
    { id: 'app.file-browser', kind: 'application', title: 'File Browser' },
    { id: 'command.storage', kind: 'command', title: 'Open storage overview', keywords: ['files'] },
  ]);

  assert.deepEqual(
    catalog.search('files', new Set()).map((result) => result.entry.id),
    ['app.files', 'command.storage'],
  );
  assert.equal(catalog.search('file', new Set())[0]?.entry.id, 'app.files');
});

test('a catalog of one thousand applications remains fully searchable', () => {
  const entries: LauncherEntry[] = Array.from({ length: 1000 }, (_, index) => ({
    id: `app.${index}`,
    kind: 'application',
    title: `Application ${index.toString().padStart(4, '0')}`,
    keywords: [`resource-${index}`],
  }));
  const catalog = new LauncherCatalog(entries);

  const startedAt = performance.now();
  const results = catalog.search('resource-997', new Set());
  const elapsed = performance.now() - startedAt;

  assert.equal(results[0]?.entry.id, 'app.997');
  assert.ok(elapsed < 100, `1000-entry search took ${elapsed.toFixed(1)} ms.`);
});

test('duplicate identifiers are rejected at catalog construction', () => {
  assert.throws(
    () => new LauncherCatalog([
      { id: 'duplicate', kind: 'application', title: 'One' },
      { id: 'duplicate', kind: 'command', title: 'Two' },
    ]),
    /duplicated/,
  );
});
