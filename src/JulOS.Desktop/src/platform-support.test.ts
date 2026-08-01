import assert from 'node:assert/strict';
import { test } from 'node:test';

import { findMissingPlatformFeatures, requiredPlatformFeatures } from './platform-support.js';

const supported = {
  hasCustomElements: true,
  hasShadowDom: true,
  hasCssCustomProperties: true,
} as const;

test('a fully supported browser reports nothing missing', () => {
  assert.deepEqual(findMissingPlatformFeatures(supported), []);
});

test('each unsupported feature is reported by name', () => {
  assert.deepEqual(findMissingPlatformFeatures({ ...supported, hasCustomElements: false }), [
    'customElements',
  ]);
  assert.deepEqual(findMissingPlatformFeatures({ ...supported, hasShadowDom: false }), ['shadowDom']);
  assert.deepEqual(findMissingPlatformFeatures({ ...supported, hasCssCustomProperties: false }), [
    'cssCustomProperties',
  ]);
});

test('an unsupported browser reports every missing feature, not only the first', () => {
  const missing = findMissingPlatformFeatures({
    hasCustomElements: false,
    hasShadowDom: false,
    hasCssCustomProperties: false,
  });

  assert.deepEqual(missing, [...requiredPlatformFeatures]);
});
