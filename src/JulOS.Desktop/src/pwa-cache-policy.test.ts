import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  cachePolicyFor,
  mayReloadImmediately,
  updateMessages,
  type CacheablePathRequest,
} from './pwa-cache-policy.js';

function request(overrides: Partial<CacheablePathRequest> = {}): CacheablePathRequest {
  return { path: '/scripts/main.a1b2c3.js', versioned: true, authenticated: false, ...overrides };
}

test('a versioned immutable shell asset may be cached', () => {
  assert.equal(cachePolicyFor(request()).decision, 'cache');
  assert.equal(cachePolicyFor(request({ path: '/styles/shell.9f8e.css' })).decision, 'cache');
});

test('a shell asset without a version identity is not cached', () => {
  assert.equal(cachePolicyFor(request({ versioned: false })).decision, 'never-cache');
});

test('authenticated API traffic is never cached', () => {
  for (const path of [
    '/api/v1/profile',
    '/api/v1/auth/status',
    '/api/v1/operations',
    '/api/v1/remote/sessions',
  ]) {
    assert.equal(
      cachePolicyFor(request({ path, versioned: false })).decision,
      'never-cache',
      `${path} must never be cached`,
    );
  }
});

test('realtime, proxied application and worker traffic is never cached', () => {
  assert.equal(cachePolicyFor(request({ path: '/hubs/events' })).decision, 'never-cache');
  assert.equal(cachePolicyFor(request({ path: '/webapps/encoded-host/' })).decision, 'never-cache');
  assert.equal(cachePolicyFor(request({ path: '/sw.js' })).decision, 'never-cache');
});

test('an authenticated response is never cached even under an immutable prefix', () => {
  const result = cachePolicyFor(request({ authenticated: true }));

  assert.equal(result.decision, 'never-cache');
  assert.match(result.reason, /authenticated/);
});

test('the disconnected document and the manifest may be cached', () => {
  assert.equal(cachePolicyFor(request({ path: '/offline.html', versioned: false })).decision, 'cache');
  assert.equal(
    cachePolicyFor(request({ path: '/manifest.webmanifest', versioned: false })).decision,
    'cache',
  );
});

test('an unknown path is denied by default', () => {
  const result = cachePolicyFor(request({ path: '/something/new', versioned: true }));

  assert.equal(result.decision, 'never-cache');
  assert.match(result.reason, /deny by default/);
});

test('only clean and fresh clients may reload without flushing', () => {
  assert.equal(mayReloadImmediately('clean'), true);
  assert.equal(mayReloadImmediately('fresh'), true);
  assert.equal(mayReloadImmediately('dirty'), false);
  assert.equal(mayReloadImmediately('conflict'), false);
});

test('the update handshake message names are stable', () => {
  assert.deepEqual(updateMessages, {
    updateReady: 'JULOS_UPDATE_READY',
    updateStatus: 'JULOS_UPDATE_STATUS',
    activateUpdate: 'JULOS_ACTIVATE_UPDATE',
    clientReadyToReload: 'JULOS_CLIENT_READY_TO_RELOAD',
  });
});
