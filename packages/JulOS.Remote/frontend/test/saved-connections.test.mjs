import assert from 'node:assert/strict';
import test from 'node:test';

import {
  decodeRemoteLaunchTarget,
  encodeRemoteLaunchTarget,
  parseTarget,
} from '../remote.source.js';

test('saved Remote launch targets contain settings and opaque secret references but no password', () => {
  const target = {
    protocol: 'rdp',
    host: 'windows.home.arpa',
    port: 3389,
    userName: 'julian',
    domain: 'HOME',
    secretReferenceId: '11111111-1111-4111-8111-111111111111',
  };
  const identity = encodeRemoteLaunchTarget(target);
  assert.match(identity, /^remote:v1:/u);
  assert.equal(identity.includes('password'), false);
  assert.deepEqual(decodeRemoteLaunchTarget(identity), target);
});

test('saved Remote targets may intentionally require password entry on launch', () => {
  const identity = encodeRemoteLaunchTarget({
    protocol: 'ssh',
    host: 'debian.home.arpa',
    port: 22,
    userName: 'admin',
    domain: '',
    secretReferenceId: null,
  });
  assert.equal(decodeRemoteLaunchTarget(identity).secretReferenceId, null);
});

test('target parser applies protocol defaults and preserves explicit ports', () => {
  assert.deepEqual(parseTarget('server.home.arpa', 'rdp'), { host: 'server.home.arpa', port: 3389 });
  assert.deepEqual(parseTarget('server.home.arpa:2222', 'ssh'), { host: 'server.home.arpa', port: 2222 });
  assert.deepEqual(parseTarget('[2001:db8::10]:5901', 'vnc'), { host: '2001:db8::10', port: 5901 });
});
