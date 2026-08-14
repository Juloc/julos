import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  buildIframeSrc,
  encodeProxyHost,
  navigate,
  parseAddressInput,
  type NavigationState,
} from './webapp-browser.js';

test('parseAddressInput adds https to a bare host and splits the path', () => {
  assert.deepEqual(parseAddressInput('unifi.local:8443/dash?x=1'), {
    origin: 'https://unifi.local:8443',
    pathQuery: '/dash?x=1',
  });
  assert.deepEqual(parseAddressInput('http://nas.local'), {
    origin: 'http://nas.local',
    pathQuery: '/',
  });
});

test('parseAddressInput rejects empty and non-http addresses', () => {
  assert.throws(() => parseAddressInput('   '));
  assert.throws(() => parseAddressInput('ftp://x'));
  assert.throws(() => parseAddressInput('javascript:alert(1)'));
});

test('encodeProxyHost matches the shared golden vector (cross-checks WebAppOriginCodec)', () => {
  assert.equal(
    encodeProxyHost('https://192.168.1.10:8443', 'p.localtest.me'),
    'waaeytsmroge3dqlrrfyytaorygq2dg.p.localtest.me',
  );
});

test('encodeProxyHost has the expected shape and canonicalizes the default port', () => {
  assert.match(encodeProxyHost('https://grafana.lan:3000', 'p.localtest.me'), /^wa[a-z2-7]+\.p\.localtest\.me$/u);
  assert.equal(
    encodeProxyHost('http://nas.local', 'p.localtest.me'),
    encodeProxyHost('http://nas.local:80', 'p.localtest.me'),
  );
});

test('buildIframeSrc uses the shell protocol and preserves the path', () => {
  (globalThis as { location?: { protocol: string } }).location = { protocol: 'http:' };
  assert.equal(buildIframeSrc('wax.p.localtest.me', '/dash?x=1'), 'http://wax.p.localtest.me/dash?x=1');
  assert.equal(buildIframeSrc('wax.p.localtest.me', ''), 'http://wax.p.localtest.me/');
});

test('navigate models forward-truncating history', () => {
  let state: NavigationState = { entries: [], index: -1 };
  state = navigate(state, { type: 'open', url: 'a' });
  state = navigate(state, { type: 'open', url: 'b' });
  assert.deepEqual(state, { entries: ['a', 'b'], index: 1 });

  state = navigate(state, { type: 'back' });
  assert.equal(state.index, 0);

  state = navigate(state, { type: 'open', url: 'c' });
  assert.deepEqual(state, { entries: ['a', 'c'], index: 1 });

  state = navigate(state, { type: 'forward' });
  assert.equal(state.index, 1);
  assert.deepEqual(navigate(state, { type: 'reload' }), state);
});
