import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import test from 'node:test';

interface HostMetricsModule {
  readonly snapshotStatusText: (
    snapshot: { readonly state?: string },
    language: 'en' | 'de',
  ) => string;
  readonly snapshotErrorText: (language: 'en' | 'de') => string;
  readonly cpuWidgetSummary: (
    snapshot: {
      readonly state?: string;
      readonly metrics?: ReadonlyArray<{
        readonly name: string;
        readonly value: number | null;
      }>;
    },
    language: 'en' | 'de',
  ) => { readonly value: string; readonly label: string };
}

async function loadModule(): Promise<HostMetricsModule> {
  const modulePath = path.resolve(
    process.cwd(),
    '../../packages/JulOS.HostMetrics/frontend/host-metrics.js',
  );
  return await import(pathToFileURL(modulePath).href) as HostMetricsModule;
}

test('Host Metrics application exposes live, stale, offline and error states', async () => {
  const module = await loadModule();

  assert.equal(module.snapshotStatusText({ state: 'live' }, 'en'), 'Current');
  assert.equal(module.snapshotStatusText({ state: 'stale' }, 'en'), 'Stale observation');
  assert.equal(module.snapshotStatusText({ state: 'offline' }, 'en'), 'Agent offline');
  assert.equal(
    module.snapshotStatusText({ state: 'unexpected' }, 'en'),
    module.snapshotErrorText('en'));
});

test('Host Metrics widget preserves unknown values instead of rendering zero', async () => {
  const module = await loadModule();

  assert.deepEqual(
    module.cpuWidgetSummary({
      state: 'live',
      metrics: [{ name: 'host.cpu.utilization', value: null }],
    }, 'en'),
    { value: '—', label: 'Current' });
  assert.deepEqual(
    module.cpuWidgetSummary({
      state: 'stale',
      metrics: [{ name: 'host.cpu.utilization', value: 0.42 }],
    }, 'en'),
    { value: '42%', label: 'CPU · Stale observation' });
});
