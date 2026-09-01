import assert from 'node:assert/strict';
import test from 'node:test';

import {
  normalizeUrl,
  resolveExecutionMode,
  validateDisplayResponse,
  validateSessionResponse,
} from '../browser.js';

test('normalizeUrl adds https and rejects unsafe URLs', () => {
  assert.equal(normalizeUrl('example.org'), 'https://example.org/');
  assert.equal(normalizeUrl('http://example.org/path'), 'http://example.org/path');
  assert.throws(() => normalizeUrl('file:///etc/passwd'));
  assert.throws(() => normalizeUrl('https://user:secret@example.org/'));
  assert.throws(() => normalizeUrl(''));
});

test('explicit execution preference is authoritative', () => {
  assert.equal(resolveExecutionMode('device', 'https://example.org/'), 'device');
  assert.equal(resolveExecutionMode('server', 'https://example.org/'), 'server');
});

test('automatic mode uses server for arbitrary external sites', () => {
  assert.equal(resolveExecutionMode('auto', 'https://example.org/'), 'server');
  assert.throws(() => resolveExecutionMode('invalid', 'https://example.org/'));
});

test('session response accepts only bounded known lifecycle data', () => {
  // JulOS issues UUIDv7 session identifiers (version nibble 7), so the response
  // validator must accept them, not only UUIDv1-5.
  const valid = {
    sessionId: '11111111-2222-7333-8444-555555555555',
    state: 'connected',
    revision: 3,
    display: { endpoint: '/api/v1/remote/display/11111111-2222-7333-8444-555555555555' },
  };
  assert.equal(validateSessionResponse(valid), valid);
  assert.throws(() => validateSessionResponse({ ...valid, state: 'owned' }));
  assert.throws(() => validateSessionResponse({ ...valid, sessionId: '../bad' }));
  assert.throws(() => validateSessionResponse({ ...valid, sessionId: '11111111-2222-8333-8444-555555555555' }));
  assert.throws(() => validateSessionResponse({ ...valid, revision: 0 }));
});

test('display response requires a bounded endpoint', () => {
  const valid = { endpoint: '/api/v1/remote/display/session' };
  assert.equal(validateDisplayResponse(valid), valid);
  assert.throws(() => validateDisplayResponse({ endpoint: '' }));
  assert.throws(() => validateDisplayResponse({ endpoint: 'x'.repeat(2049) }));
});
