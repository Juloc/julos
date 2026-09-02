import assert from 'node:assert/strict';
import { test } from 'node:test';

import { nextSelectionIndex } from './launcher-selection.js';

test('moves down and up within range', () => {
  assert.equal(nextSelectionIndex(0, 3, 1), 1);
  assert.equal(nextSelectionIndex(1, 3, 1), 2);
  assert.equal(nextSelectionIndex(2, 3, -1), 1);
});

test('wraps around at both ends', () => {
  assert.equal(nextSelectionIndex(2, 3, 1), 0);
  assert.equal(nextSelectionIndex(0, 3, -1), 2);
});

test('handles empty and single-item lists', () => {
  assert.equal(nextSelectionIndex(0, 0, 1), 0);
  assert.equal(nextSelectionIndex(0, 1, 1), 0);
  assert.equal(nextSelectionIndex(0, 1, -1), 0);
});

test('clamps an out-of-range current index before moving', () => {
  assert.equal(nextSelectionIndex(5, 3, 1), 0);
  // -1 clamps modularly to the last index (2), then +1 wraps back to 0.
  assert.equal(nextSelectionIndex(-1, 3, 1), 0);
  assert.equal(nextSelectionIndex(-1, 3, -1), 1);
});
