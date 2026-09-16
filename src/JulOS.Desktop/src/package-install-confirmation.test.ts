import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  PackageManagerStore,
  type PackageInstallPreviewView,
} from './package-manager.js';

test('an artifact is previewed before it is installed, and both calls carry one operation', async () => {
  const server = fakeServer();
  const store = new PackageManagerStore(server.fetch);

  const installed = await store.install(uploadRequest(), () => true);

  assert.equal(installed, true);
  assert.deepEqual(
    server.calls.map((call) => call.path),
    ['/api/v1/auth/antiforgery', '/api/v1/packages/previews', '/api/v1/packages/install', '/api/v1/packages/', '/api/v1/packages/catalog'],
  );
  const [previewCall, installCall] = [server.calls[1], server.calls[2]];
  assert.equal(
    previewCall?.form?.get('OperationKey'),
    installCall?.form?.get('OperationKey'),
    'the acknowledgement is bound to the operation, so both calls must name the same one',
  );
  assert.equal(installCall?.form?.get('AcknowledgementDigest'), 'digest-from-preview');
});

test('declining the confirmation installs nothing', async () => {
  const server = fakeServer();
  const store = new PackageManagerStore(server.fetch);

  const installed = await store.install(uploadRequest(), () => false);

  assert.equal(installed, false);
  assert.equal(
    server.calls.some((call) => call.path === '/api/v1/packages/install'),
    false,
  );
});

test('a package that needs no acknowledgement is not turned into a question', async () => {
  const server = fakeServer({ acknowledgementRequired: false });
  const store = new PackageManagerStore(server.fetch);
  let asked = false;

  const installed = await store.install(uploadRequest(), () => {
    asked = true;
    return false;
  });

  assert.equal(installed, true);
  assert.equal(asked, false, 'a confirmation nobody needs teaches clicking through the ones that matter');
});

test('an unsigned upload is accepted without a signature or a publisher', async () => {
  const server = fakeServer();
  const store = new PackageManagerStore(server.fetch);

  await store.install(
    { artifact: file('package.zip'), signature: null, publisherId: '', publisherKeyId: '' },
    () => true,
  );

  const form = server.calls.find((call) => call.path === '/api/v1/packages/previews')?.form;
  assert.equal(form?.has('Signature'), false);
  assert.equal(form?.has('PublisherId'), false);
});

test('half a signature claim is refused before it reaches the server', async () => {
  const server = fakeServer();
  const store = new PackageManagerStore(server.fetch);

  await assert.rejects(
    () => store.install(
      { artifact: file('package.zip'), signature: null, publisherId: 'juloc', publisherKeyId: 'key' },
      () => true,
    ),
    /signature file and its publisher identity/,
  );
  assert.equal(server.calls.length, 0);
});

test('an official package is confirmed the same way an uploaded one is', async () => {
  const server = fakeServer();
  const store = new PackageManagerStore(server.fetch);

  const installed = await store.installOfficial('de.juloc.example', () => true);

  assert.equal(installed, true);
  assert.deepEqual(
    server.calls.map((call) => call.path).slice(0, 3),
    [
      '/api/v1/auth/antiforgery',
      '/api/v1/packages/catalog/de.juloc.example/previews',
      '/api/v1/packages/catalog/de.juloc.example/install',
    ],
  );
  assert.equal(
    (server.calls[2]?.body as { acknowledgementDigest?: string } | undefined)?.acknowledgementDigest,
    'digest-from-preview',
  );
});

function uploadRequest(): {
  artifact: File;
  signature: File | null;
  publisherId: string;
  publisherKeyId: string;
} {
  return {
    artifact: file('package.zip'),
    signature: file('package.zip.sig'),
    publisherId: 'juloc',
    publisherKeyId: 'release-2026',
  };
}

function file(name: string): File {
  return new File([new Uint8Array([1, 2, 3])], name);
}

function preview(overrides: Partial<PackageInstallPreviewView>): PackageInstallPreviewView {
  return {
    packageId: 'de.juloc.example',
    version: '1.0.0',
    artifactDigest: 'a'.repeat(64),
    signatureState: 'unsigned',
    publisherId: null,
    keyId: null,
    publicKeyFingerprint: null,
    permissions: ['core.system.version.read'],
    runtimeKind: 'none',
    networkAccess: false,
    criticalRightsDigest: 'b'.repeat(64),
    warnings: ['package.warning.isolated_runtime', 'package.warning.unsigned'],
    requiresIsolation: true,
    acknowledgementRequired: true,
    acknowledgementDigest: 'digest-from-preview',
    alreadyInstalled: false,
    ...overrides,
  };
}

interface RecordedCall {
  readonly path: string;
  readonly form: FormData | null;
  readonly body: unknown;
}

function fakeServer(overrides: Partial<PackageInstallPreviewView> = {}): {
  fetch: typeof fetch;
  calls: RecordedCall[];
} {
  const calls: RecordedCall[] = [];

  const implementation: typeof fetch = async (input, init) => {
    const path = new URL(String(input), 'https://localhost').pathname;
    const body = init?.body;
    calls.push({
      path,
      form: body instanceof FormData ? body : null,
      body: typeof body === 'string' ? JSON.parse(body) : null,
    });

    if (path === '/api/v1/auth/antiforgery') {
      return json({ headerName: 'X-JulOS-Antiforgery', token: 'token' });
    }

    if (path.endsWith('/previews')) {
      return json(preview(overrides));
    }

    if (path === '/api/v1/packages/') {
      return json([]);
    }

    if (path === '/api/v1/packages/catalog') {
      return json([]);
    }

    return json({
      installationId: '00000000-0000-0000-0000-000000000001',
      packageId: 'de.juloc.example',
      version: '1.0.0',
      state: 'installed',
      revision: 1,
      faultCode: null,
      faultDetail: null,
      faultedAtUtc: null,
      configurationRequired: true,
      workerHealthy: false,
      artifactDigest: 'a'.repeat(64),
    });
  };

  return { fetch: implementation, calls };
}

function json(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  });
}
