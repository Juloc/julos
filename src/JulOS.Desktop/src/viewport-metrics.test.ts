import assert from 'node:assert/strict';
import { test } from 'node:test';

import { classifyWorkspace } from './workspace-contract.js';
import {
  applyViewportMetrics,
  readViewportMetrics,
  ViewportObserver,
  viewportCssVariables,
  type VisualViewportLike,
} from './viewport-metrics.js';

function visualViewport(height: number): VisualViewportLike & { fire: () => void } {
  const listeners: (() => void)[] = [];
  return {
    height,
    offsetTop: 0,
    addEventListener: (_type, listener) => listeners.push(listener),
    removeEventListener: (_type, listener) => {
      const index = listeners.indexOf(listener);
      if (index >= 0) {
        listeners.splice(index, 1);
      }
    },
    fire: () => listeners.forEach((listener) => listener()),
  };
}

function styleTarget(): { style: { setProperty(property: string, value: string): void }; values: Map<string, string> } {
  const values = new Map<string, string>();
  return { style: { setProperty: (property, value) => values.set(property, value) }, values };
}

test('without a visual viewport the keyboard inset is zero', () => {
  const metrics = readViewportMetrics(1024, 768, null);

  assert.equal(metrics.visualHeightCssPx, 768);
  assert.equal(metrics.keyboardInsetCssPx, 0);
  assert.equal(metrics.keyboardVisible, false);
});

test('an open software keyboard produces an inset', () => {
  const metrics = readViewportMetrics(393, 852, visualViewport(516));

  assert.equal(metrics.keyboardInsetCssPx, 336);
  assert.equal(metrics.keyboardVisible, true);
});

test('collapsing browser chrome is not treated as a keyboard', () => {
  const metrics = readViewportMetrics(393, 852, visualViewport(800));

  assert.equal(metrics.keyboardInsetCssPx, 52);
  assert.equal(metrics.keyboardVisible, false);
});

test('a software keyboard never changes workspace identity', () => {
  // The layout viewport is what classification reads, and the keyboard only shrinks the
  // visual viewport, so the workspace class is identical with and without a keyboard.
  const capabilities = {
    primaryPointerCoarse: true,
    anyPointerFine: false,
    screenMinimumDimensionCssPx: 834,
    layoutViewportWidthCssPx: 834,
  };
  const closed = readViewportMetrics(834, 1112, null);
  const open = readViewportMetrics(834, 1112, visualViewport(600));

  assert.equal(closed.layoutWidthCssPx, open.layoutWidthCssPx);
  assert.equal(
    classifyWorkspace({ ...capabilities, layoutViewportWidthCssPx: closed.layoutWidthCssPx }),
    classifyWorkspace({ ...capabilities, layoutViewportWidthCssPx: open.layoutWidthCssPx }),
  );
  assert.equal(open.keyboardVisible, true);
});

test('metrics are published as CSS custom properties', () => {
  const target = styleTarget();

  applyViewportMetrics(target, readViewportMetrics(393, 852, visualViewport(516)));

  assert.equal(target.values.get(viewportCssVariables.viewportHeight), '516px');
  assert.equal(target.values.get(viewportCssVariables.keyboardInset), '336px');
});

test('the observer republishes on a visual viewport change', () => {
  const target = styleTarget();
  const viewport = visualViewport(852);
  let height = 852;
  const observer = new ViewportObserver({
    target,
    layoutSize: () => ({ width: 393, height: 852 }),
    visualViewport: new Proxy(viewport, {
      get: (source, key) => (key === 'height' ? height : Reflect.get(source, key)),
    }),
  });

  observer.refresh();
  assert.equal(target.values.get(viewportCssVariables.keyboardInset), '0px');

  height = 516;
  viewport.fire();

  assert.equal(target.values.get(viewportCssVariables.keyboardInset), '336px');
  assert.equal(observer.metrics.keyboardVisible, true);

  observer.dispose();
});
