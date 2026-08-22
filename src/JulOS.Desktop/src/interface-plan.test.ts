import assert from 'node:assert/strict';
import { test } from 'node:test';
import { JSDOM } from 'jsdom';
import {
  canStartDesktopEditMode,
  classifyInterfaceViewport,
  desktopEditLongPressMs,
  desktopEditMovementTolerance,
  InterfacePlanController,
} from './interface-plan.js';

test('interface viewport keeps one shell model across desktop, tablet and mobile', () => {
  assert.equal(classifyInterfaceViewport(1440), 'desktop');
  assert.equal(classifyInterfaceViewport(1100), 'desktop');
  assert.equal(classifyInterfaceViewport(900), 'tablet');
  assert.equal(classifyInterfaceViewport(720), 'tablet');
  assert.equal(classifyInterfaceViewport(390), 'mobile');
  assert.equal(classifyInterfaceViewport(Number.NaN), 'desktop');
});

test('desktop edit long press uses deliberate touch-friendly thresholds', () => {
  assert.ok(desktopEditLongPressMs >= 450);
  assert.ok(desktopEditMovementTolerance >= 8);
});

test('edit mode does not start from interactive shell controls or windows', () => {
  const dom = new JSDOM('<div class="desktop-content"><div id="blank"></div><button id="button"></button><div class="desktop-window"><span id="inside"></span></div><div class="authentication-card"><span id="auth"></span></div></div>');
  const document = dom.window.document;
  assert.equal(canStartDesktopEditMode(document.querySelector('#blank')), true);
  assert.equal(canStartDesktopEditMode(document.querySelector('#button')), false);
  assert.equal(canStartDesktopEditMode(document.querySelector('#inside')), false);
  assert.equal(canStartDesktopEditMode(document.querySelector('#auth')), false);
  dom.window.close();
});

test('controller injects one stylesheet and exposes explicit edit mode with a done action', () => {
  const dom = new JSDOM('<!doctype html><html lang="en"><body><div id="host"></div></body></html>', { url: 'https://julos.test/' });
  const host = dom.window.document.querySelector<HTMLElement>('#host');
  assert.ok(host);
  const root = host.attachShadow({ mode: 'open' });
  root.innerHTML = '<main id="desktop-root"><section class="desktop-content"></section></main>';

  const controller = new InterfacePlanController(host);
  controller.connect();
  controller.connect();
  assert.equal(root.querySelectorAll('link[data-julos-interface-plan]').length, 1);
  assert.equal(root.querySelectorAll('.desktop-edit-toolbar').length, 1);

  controller.enterEditMode();
  assert.equal(root.querySelector<HTMLElement>('#desktop-root')?.dataset['editMode'], 'true');
  assert.equal(root.querySelector<HTMLElement>('.desktop-edit-toolbar')?.hidden, false);

  dom.window.document.documentElement.lang = 'de';
  controller.exitEditMode();
  controller.enterEditMode();
  assert.equal(root.querySelector<HTMLElement>('.desktop-edit-label')?.textContent, 'Desktop bearbeiten');
  assert.equal(root.querySelector<HTMLButtonElement>('.desktop-edit-done')?.textContent, 'Fertig');

  root.querySelector<HTMLButtonElement>('.desktop-edit-done')?.click();
  assert.equal(root.querySelector<HTMLElement>('#desktop-root')?.dataset['editMode'], undefined);
  assert.equal(root.querySelector<HTMLElement>('.desktop-edit-toolbar')?.hidden, true);

  controller.disconnect();
  dom.window.close();
});
