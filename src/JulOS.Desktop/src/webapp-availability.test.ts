import assert from 'node:assert/strict';
import { test } from 'node:test';

import { isDynamicWebAppBrowserAvailable } from './webapp-availability.js';

test('dynamic web-app browser is available only with enabled proxy configuration', () => {
  assert.equal(
    isDynamicWebAppBrowserAvailable({ enabled: true, proxyZone: 'p.os.juloc.de' }),
    true,
  );
  assert.equal(
    isDynamicWebAppBrowserAvailable({ enabled: false, proxyZone: 'p.os.juloc.de' }),
    false,
  );
  assert.equal(
    isDynamicWebAppBrowserAvailable({ enabled: true, proxyZone: '   ' }),
    false,
  );
});
