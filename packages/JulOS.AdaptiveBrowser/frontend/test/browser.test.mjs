import assert from 'node:assert/strict';
import test from 'node:test';

import { normalizeUrl, resolveExecutionMode } from '../browser.js';

test('normalizeUrl adds https and rejects non-web schemes', () => {
  assert.equal(normalizeUrl('example.org'), 'https://example.org/');
  assert.equal(normalizeUrl('http://example.org/path'), 'http://example.org/path');
  assert.throws(() => normalizeUrl('file:///etc/passwd'));
});

test('explicit execution preference is authoritative', () => {
  assert.equal(resolveExecutionMode('device', 'https://example.org/'), 'device');
  assert.equal(resolveExecutionMode('server', 'https://example.org/'), 'server');
});

test('automatic mode uses server for arbitrary external sites', () => {
  assert.equal(resolveExecutionMode('auto', 'https://example.org/'), 'server');
});
