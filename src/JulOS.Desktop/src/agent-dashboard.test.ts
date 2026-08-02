import assert from 'node:assert/strict';
import { test } from 'node:test';

import { AgentDashboardStore, type AgentView } from './agent-dashboard.js';

const now = new Date('2026-08-02T22:00:00Z');

test('connectivity distinguishes online stale offline unknown and revoked', async () => {
  const agents: AgentView[] = [
    agent('online', '2026-08-02T21:59:30Z'),
    agent('stale', '2026-08-02T21:57:00Z'),
    agent('offline', '2026-08-02T21:40:00Z'),
    agent('unknown', null),
    { ...agent('revoked', '2026-08-02T21:59:50Z'), state: 'Revoked', revokedAtUtc: '2026-08-02T21:59:55Z' },
  ];
  const store = new AgentDashboardStore(jsonFetch(agents), () => now);

  const snapshot = await store.refresh();

  assert.deepEqual(snapshot.map((entry) => entry.connectivity), [
    'online',
    'stale',
    'offline',
    'unknown',
    'revoked',
  ]);
});

test('metric ranges are bounded before a request is sent', async () => {
  const store = new AgentDashboardStore(jsonFetch([]), () => now);

  assert.throws(
    () => store.readMetrics(
      '0198f5c1-a0f0-7000-8000-000000000101',
      new Date('2026-01-01T00:00:00Z'),
      now,
    ),
    RangeError,
  );
});

function agent(name: string, lastSeenAtUtc: string | null): AgentView {
  return {
    agentId: `0198f5c1-a0f0-7000-8000-00000000010${name.length % 9}`,
    name,
    machineIdentity: `machine-${name}`,
    operatingSystem: 'Linux',
    architecture: 'X64',
    version: '1.0.0',
    state: 'Online',
    enrolledAtUtc: '2026-08-01T00:00:00Z',
    lastSeenAtUtc,
    revokedAtUtc: null,
    revision: 1,
  };
}

function jsonFetch(value: unknown): typeof fetch {
  return async () => new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}
