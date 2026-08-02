import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ObservabilityCenter, severityLabel } from './observability-center.js';

test('repeated problem observations update one item and resolved problems reopen', () => {
  const center = new ObservabilityCenter();
  const first = center.observeProblem({
    identity: 'docker.restart-loop:container-1',
    severity: 'error',
    title: 'Container restart loop',
    sourcePackage: 'de.juloc.docker',
    resourceId: 'container-1',
    observedAtUtc: '2026-08-02T20:00:00Z',
    deepLink: '/apps/docker/container-1',
  });
  center.setProblemState(first.identity, 'resolved');
  const repeated = center.observeProblem({
    identity: first.identity,
    severity: 'critical',
    title: 'Container restart loop',
    sourcePackage: 'de.juloc.docker',
    resourceId: 'container-1',
    observedAtUtc: '2026-08-02T20:05:00Z',
    deepLink: '/apps/docker/container-1',
  });

  assert.equal(center.problems.length, 1);
  assert.equal(repeated.observationCount, 2);
  assert.equal(repeated.state, 'active');
  assert.equal(repeated.severity, 'critical');
});

test('notification deduplication prevents repeated event spam', () => {
  const center = new ObservabilityCenter();
  const notification = {
    id: 'notification-1',
    deduplicationKey: 'package-installed:de.juloc.files:1.0.0',
    title: 'Package installed',
    body: 'Files 1.0.0 was installed.',
    observedAtUtc: '2026-08-02T20:00:00Z',
    deepLink: '/packages/de.juloc.files',
  } as const;

  center.observeNotification(notification);
  const repeated = center.observeNotification({
    ...notification,
    id: 'notification-2',
    observedAtUtc: '2026-08-02T20:01:00Z',
  });

  assert.equal(center.notifications.length, 1);
  assert.equal(repeated.id, 'notification-1');
  assert.equal(repeated.repeatCount, 2);
});

test('severity has explicit text semantics independent of color', () => {
  assert.deepEqual(
    ['information', 'warning', 'error', 'critical'].map((severity) =>
      severityLabel(severity as Parameters<typeof severityLabel>[0])),
    ['Information', 'Warning', 'Error', 'Critical'],
  );
});
