import assert from 'node:assert/strict';
import { test } from 'node:test';

import { classifyHomeIndicatorGesture } from './home-gesture.js';

test('small travel in any direction is a tap', () => {
  assert.equal(classifyHomeIndicatorGesture(0, 0), 'tap');
  assert.equal(classifyHomeIndicatorGesture(13, -13), 'tap');
  assert.equal(classifyHomeIndicatorGesture(-10, 8), 'tap');
});

test('horizontal swipes switch applications by direction', () => {
  assert.equal(classifyHomeIndicatorGesture(-80, 4), 'switch-next');
  assert.equal(classifyHomeIndicatorGesture(80, -4), 'switch-previous');
});

test('vertical swipes reveal or hide the dock', () => {
  assert.equal(classifyHomeIndicatorGesture(5, -60), 'reveal');
  assert.equal(classifyHomeIndicatorGesture(-5, 60), 'hide');
});

test('the larger axis wins when travel is diagonal', () => {
  assert.equal(classifyHomeIndicatorGesture(-60, -20), 'switch-next');
  assert.equal(classifyHomeIndicatorGesture(-20, -60), 'reveal');
});

test('the tap threshold is configurable', () => {
  assert.equal(classifyHomeIndicatorGesture(20, 0), 'switch-previous');
  assert.equal(classifyHomeIndicatorGesture(20, 0, 40), 'tap');
});
