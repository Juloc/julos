import assert from 'node:assert/strict';
import { test } from 'node:test';

import { WidgetHostStore, WidgetOwnershipError, widgetObservationLabel } from './widget-host.js';

test('a package cannot edit or remove another package widget', () => {
  const store = new WidgetHostStore();
  store.register({ widgetId: 'docker.cpu', packageId: 'de.juloc.docker', size: 'small' });

  assert.throws(
    () => store.update('de.juloc.proxmox', 'docker.cpu', {
      status: 'live',
      observedAtUtc: '2026-08-02T20:00:00Z',
      value: 42,
    }),
    (error: unknown) => error instanceof WidgetOwnershipError,
  );
  assert.throws(
    () => store.remove('de.juloc.proxmox', 'docker.cpu'),
    (error: unknown) => error instanceof WidgetOwnershipError,
  );
});

test('live and stale widgets carry an observation time and visible age', () => {
  const store = new WidgetHostStore();
  store.register({ widgetId: 'host.cpu', packageId: 'de.juloc.host', size: 'medium' });
  const widget = store.update('de.juloc.host', 'host.cpu', {
    status: 'stale',
    observedAtUtc: '2026-08-02T20:00:00Z',
    value: { percent: 34 },
  });

  assert.equal(
    widgetObservationLabel(widget, '2026-08-02T20:01:15Z'),
    'stale; observed 75 seconds ago',
  );
});

test('every documented status remains explicit', () => {
  const store = new WidgetHostStore();
  const statuses = ['loading', 'live', 'stale', 'offline', 'unauthorized', 'error'] as const;

  for (const [index, status] of statuses.entries()) {
    const widgetId = `widget-${index}`;
    store.register({ widgetId, packageId: 'de.juloc.test', size: 'small' });
    store.update('de.juloc.test', widgetId, {
      status,
      observedAtUtc: status === 'live' || status === 'stale' ? '2026-08-02T20:00:00Z' : null,
      value: null,
    });
  }

  assert.deepEqual(store.widgets.map((widget) => widget.status), statuses);
});
