import assert from 'node:assert/strict';
import { test } from 'node:test';

import type { ShellApiClient, WebAppSummary } from './shell-api.js';
import { WebAppCatalog, webAppTitle } from './web-app-catalog.js';

function catalogWith(
  targets: readonly WebAppSummary[] | (() => Promise<readonly WebAppSummary[]>),
): { catalog: WebAppCatalog; failures: unknown[] } {
  const failures: unknown[] = [];
  const api = {
    readWebApps: typeof targets === 'function' ? targets : (): Promise<readonly WebAppSummary[]> =>
      Promise.resolve(targets),
  } as unknown as ShellApiClient;
  const catalog = new WebAppCatalog({ api, onFailure: (error) => failures.push(error) });
  return { catalog, failures };
}

test('webAppTitle capitalizes the first host label', () => {
  assert.equal(webAppTitle('unifi.os.juloc.de'), 'Unifi');
  assert.equal(webAppTitle('plex'), 'Plex');
});

test('refresh builds one embedded application per target', async () => {
  const { catalog } = catalogWith([{ host: 'unifi.os.juloc.de' }, { host: 'plex.os.juloc.de' }]);

  await catalog.refresh();

  const apps = catalog.applications();
  assert.equal(apps.length, 2);
  const unifi = apps[0]!;
  assert.equal(unifi.applicationDefinitionId, 'julos.webapp:unifi.os.juloc.de');
  assert.equal(unifi.packageId, 'julos.webapp');
  assert.equal(unifi.stableKey, 'unifi.os.juloc.de');
  assert.equal(unifi.displayNameKey, 'Unifi');
  assert.equal(unifi.elementName, '');
  assert.equal(unifi.instancePolicy, 'multiple-instances');
  assert.ok(catalog.isWebApp('julos.webapp:unifi.os.juloc.de'));
  assert.equal(catalog.hostFor('julos.webapp:unifi.os.juloc.de'), 'unifi.os.juloc.de');
});

test('isWebApp and hostFor reject unknown ids', () => {
  const { catalog } = catalogWith([]);

  assert.equal(catalog.isWebApp('core.settings'), false);
  assert.equal(catalog.hostFor('core.settings'), null);
});

test('refresh failure clears the catalog and reports the error', async () => {
  const error = new Error('boom');
  const { catalog, failures } = catalogWith(() => Promise.reject(error));

  await catalog.refresh();

  assert.equal(catalog.applications().length, 0);
  assert.deepEqual(failures, [error]);
});
