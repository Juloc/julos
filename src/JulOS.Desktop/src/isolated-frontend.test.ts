import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  isolatedFrontendDocument,
  isolationErrorCodes,
  readIsolatedRequest,
  type IsolatedGrants,
} from './isolated-frontend.js';

const grants: IsolatedGrants = {
  packageId: 'de.juloc.example',
  capabilities: ['host.metrics.read'],
};

test('a declared capability request is accepted', () => {
  const result = readIsolatedRequest(
    { kind: 'capability', requestId: '1', capability: 'host.metrics.read', operation: 'latest', payload: {} },
    grants,
  );

  assert.ok('request' in result);
  assert.equal(result.request.kind, 'capability');
  assert.equal(result.request.capability, 'host.metrics.read');
});

test('a capability the manifest does not declare is refused', () => {
  const result = readIsolatedRequest(
    { kind: 'capability', requestId: '1', capability: 'secrets.read', operation: 'latest' },
    grants,
  );

  assert.ok('rejection' in result);
  assert.equal(result.rejection.code, isolationErrorCodes.notGranted);
  assert.equal(
    result.rejection.packageId,
    'de.juloc.example',
    'A refusal names the package so it is attributable.',
  );
});

test('anything the bridge does not recognise is refused rather than guessed', () => {
  for (const message of [
    null,
    'capability',
    42,
    {},
    { kind: 'capability' },
    { kind: 'capability', requestId: '' },
    { kind: 'capability', requestId: '1' },
    { kind: 'capability', requestId: '1', capability: 'host.metrics.read' },
    { kind: 'open-application', requestId: '1' },
    { kind: 'open-application', requestId: '1', applicationId: '' },
    { kind: 'evaluate', requestId: '1' },
    { kind: 'capability', requestId: 'x'.repeat(129), capability: 'host.metrics.read', operation: 'latest' },
  ]) {
    const result = readIsolatedRequest(message, grants);
    assert.ok('rejection' in result, `${JSON.stringify(message)} should have been refused`);
  }
});

test('an application open request carries only identities', () => {
  const result = readIsolatedRequest(
    { kind: 'open-application', requestId: '7', applicationId: 'app-1', targetId: 'target-1', extra: 'ignored' },
    grants,
  );

  assert.ok('request' in result);
  assert.deepEqual(result.request, {
    kind: 'open-application',
    requestId: '7',
    applicationId: 'app-1',
    targetId: 'target-1',
  });
});

test('a package with no declared capabilities can invoke nothing', () => {
  const result = readIsolatedRequest(
    { kind: 'capability', requestId: '1', capability: 'host.metrics.read', operation: 'latest' },
    { packageId: 'de.juloc.example', capabilities: [] },
  );

  assert.ok('rejection' in result);
  assert.equal(result.rejection.code, isolationErrorCodes.notGranted);
});

test('the isolated document grants the frame nothing it could reach JulOS with', () => {
  const document = isolatedFrontendDocument('/packages/example/frontend/app.js', 'de.juloc.example');

  assert.match(document, /default-src 'none'/u);
  assert.match(document, /connect-src 'none'/u, 'The frame cannot open its own connections.');
  assert.ok(
    !document.includes('allow-same-origin'),
    'A frame that can drop its own sandbox is not a sandbox.',
  );
  assert.ok(!document.includes('document.cookie'), 'Nothing in the frame reads a cookie.');
});

test('a module URL cannot break out of the content policy', () => {
  const document = isolatedFrontendDocument("/packages/x.js'; script-src *", 'de.juloc.example');

  const policy = /content="([^"]+)"/u.exec(document)?.[1] ?? '';
  assert.ok(!policy.includes('script-src *'), 'A crafted URL must not widen the policy.');
  assert.match(
    policy,
    /script-src 'unsafe-inline' https?:\/\/[^ ;]+;/u,
    'Only an origin reaches the policy, never a path.',
  );
  assert.ok(!policy.includes('/packages/'), 'The crafted path never reaches the policy at all.');
});
