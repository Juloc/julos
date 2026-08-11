import assert from 'node:assert/strict';
import test from 'node:test';

import {
  escapeHtml,
  nextActiveTabId,
  normalizeUrl,
  tabTitle,
  toCreateProfileRequest,
  toSessionRequest,
  validateCreatedProfile,
  validateNetworkProfileList,
  validateProfileList,
  validateSession,
} from '../browser.js';

test('a temporary selection produces a temporary session request', () => {
  assert.deepEqual(toSessionRequest(null, 'https://example.test/'), {
    initialUrl: 'https://example.test/',
    profileMode: 'temporary',
    profileId: null,
  });
});

test('a retained selection carries its stored mode and identity', () => {
  assert.deepEqual(
    toSessionRequest({ profileId: 'p-1', mode: 'persistent' }, 'https://example.test/'),
    { initialUrl: 'https://example.test/', profileMode: 'persistent', profileId: 'p-1' },
  );
  assert.deepEqual(
    toSessionRequest({ profileId: 'p-2', mode: 'application' }, 'https://example.test/'),
    { initialUrl: 'https://example.test/', profileMode: 'application', profileId: 'p-2' },
  );
});

test('an invalid profile selection is rejected', () => {
  assert.throws(() => toSessionRequest({ profileId: 'p-1', mode: 'temporary' }, 'https://example.test/'));
  assert.throws(() => toSessionRequest({ mode: 'persistent' }, 'https://example.test/'));
});

test('the profile list keeps only well-formed retained profiles', () => {
  const profiles = validateProfileList({
    profiles: [
      { profileId: 'p-1', displayName: 'Work', mode: 'persistent', networkProfileKey: 'lan', revision: 1 },
      { profileId: 'p-2', displayName: 'Shop', mode: 'application', networkProfileKey: 'lan', revision: 3 },
    ],
  });
  assert.deepEqual(profiles, [
    { profileId: 'p-1', displayName: 'Work', mode: 'persistent' },
    { profileId: 'p-2', displayName: 'Shop', mode: 'application' },
  ]);
});

test('a malformed profile list is rejected', () => {
  assert.throws(() => validateProfileList({}));
  assert.throws(() => validateProfileList({ profiles: [{ profileId: 'p-1', displayName: 'x', mode: 'temporary' }] }));
  assert.throws(() => validateProfileList({ profiles: [{ displayName: 'x', mode: 'persistent' }] }));
});

test('profile names are escaped before they enter option markup', () => {
  assert.equal(
    escapeHtml('<img src=x onerror="alert(1)">&\'"'),
    '&lt;img src=x onerror=&quot;alert(1)&quot;&gt;&amp;&#39;&quot;',
  );
});

test('the address is normalized and rejects non-HTTP schemes', () => {
  assert.equal(normalizeUrl('  https://example.test/path  '), 'https://example.test/path');
  assert.throws(() => normalizeUrl('file:///etc/passwd'));
});

test('a session response must carry an identity, state and revision', () => {
  assert.equal(
    validateSession({ sessionId: 's-1', state: 'connected', revision: 2 }).sessionId,
    's-1',
  );
  assert.throws(() => validateSession({ sessionId: 's-1', state: 'connected', revision: 0 }));
  assert.throws(() => validateSession(null));
});

test('a create-profile request trims the name and requires a network', () => {
  assert.deepEqual(toCreateProfileRequest('  Work  ', 'lan'), {
    displayName: 'Work',
    mode: 'persistent',
    networkProfileKey: 'lan',
  });
  assert.throws(() => toCreateProfileRequest('', 'lan'));
  assert.throws(() => toCreateProfileRequest('Work', ''));
  assert.throws(() => toCreateProfileRequest('x'.repeat(97), 'lan'));
});

test('the network profile list keeps only well-formed entries', () => {
  assert.deepEqual(
    validateNetworkProfileList({
      networkProfiles: [{ key: 'lan', runtimeNetwork: 'julos-lan', hasProxy: false, revision: 1 }],
    }),
    [{ key: 'lan', runtimeNetwork: 'julos-lan' }],
  );
  assert.throws(() => validateNetworkProfileList({}));
  assert.throws(() => validateNetworkProfileList({ networkProfiles: [{ key: 'lan' }] }));
});

test('a created profile response must carry an identity, name and retained mode', () => {
  assert.deepEqual(
    validateCreatedProfile({ profileId: 'p-1', displayName: 'Work', mode: 'persistent', revision: 1 }),
    { profileId: 'p-1', displayName: 'Work', mode: 'persistent' },
  );
  assert.throws(() => validateCreatedProfile({ profileId: 'p-1', displayName: 'Work', mode: 'temporary' }));
  assert.throws(() => validateCreatedProfile(null));
});

test('a tab title is the host name, with a fallback for a blank tab', () => {
  assert.equal(tabTitle('https://example.test/path', 'New tab'), 'example.test');
  assert.equal(tabTitle('', 'New tab'), 'New tab');
  assert.equal(tabTitle('not a url', 'New tab'), 'New tab');
});

test('closing a tab keeps the active one, or selects a neighbour when the active closes', () => {
  const tabs = [{ id: 'a' }, { id: 'b' }, { id: 'c' }];
  // Closing a non-active tab leaves the active tab active.
  assert.equal(nextActiveTabId(tabs, 'a', 'b'), 'b');
  // Closing the active middle tab selects the tab that shifts into its slot.
  assert.equal(nextActiveTabId(tabs, 'b', 'b'), 'c');
  // Closing the active last tab selects the new last tab.
  assert.equal(nextActiveTabId(tabs, 'c', 'c'), 'b');
  // Closing the only tab selects nothing.
  assert.equal(nextActiveTabId([{ id: 'a' }], 'a', 'a'), null);
});
