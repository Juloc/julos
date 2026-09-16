import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  isCancellable,
  isSettled,
  OperationCenterStore,
  type OperationState,
  type OperationView,
} from './operation-center.js';

test('the first page is shown newest first', async () => {
  const server = fakeServer([operation('a', 'running'), operation('b', 'queued')]);
  const store = new OperationCenterStore(server.fetch);

  await store.refresh();

  assert.deepEqual(
    store.snapshot().operations.map((item) => item.operationId),
    ['a', 'b'],
  );
  assert.equal(store.snapshot().hasMore, false);
});

test('a further page appends without duplicating what is already shown', async () => {
  const server = fakeServer([operation('a', 'running'), operation('b', 'queued')], { pageSize: 1 });
  const store = new OperationCenterStore(server.fetch);
  await store.refresh();
  assert.equal(store.snapshot().hasMore, true);

  await store.loadMore();
  await store.loadMore();

  assert.deepEqual(
    store.snapshot().operations.map((item) => item.operationId),
    ['a', 'b'],
  );
  assert.equal(store.snapshot().hasMore, false);
});

test('changing the filter discards the cursor rather than continuing another question', async () => {
  const server = fakeServer([operation('a', 'running'), operation('b', 'failed')], { pageSize: 1 });
  const store = new OperationCenterStore(server.fetch);
  await store.refresh();

  await store.setFilter({ states: ['failed'], sourcePackageId: null });

  const lastRead = server.calls.filter((call) => call.startsWith('/api/v1/operations?')).at(-1) ?? '';
  assert.ok(lastRead.includes('states=failed'));
  assert.ok(!lastRead.includes('cursor='), 'A cursor belongs to the filters it was issued for.');
});

test('a repeated event does not duplicate or reorder an operation', async () => {
  const server = fakeServer([operation('a', 'running', 3)]);
  const store = new OperationCenterStore(server.fetch);
  await store.refresh();
  const readsBefore = server.calls.filter((call) => call === '/api/v1/operations/a').length;

  await store.applyChange('a', 3);
  await store.applyChange('a', 2);

  assert.equal(
    server.calls.filter((call) => call === '/api/v1/operations/a').length,
    readsBefore,
    'An event at or below the known revision needs no refetch at all.',
  );
  assert.equal(store.snapshot().operations.length, 1);
});

test('a newer event refetches authoritative state', async () => {
  const server = fakeServer([operation('a', 'running', 3)]);
  const store = new OperationCenterStore(server.fetch);
  await store.refresh();
  server.replace(operation('a', 'succeeded', 4));

  await store.applyChange('a', 4);

  assert.equal(store.snapshot().operations[0]?.state, 'succeeded');
  assert.equal(store.snapshot().operations.length, 1);
});

test('an operation that is no longer visible leaves the list', async () => {
  const server = fakeServer([operation('a', 'running', 3)]);
  const store = new OperationCenterStore(server.fetch);
  await store.refresh();
  server.remove('a');

  await store.applyChange('a', 4);

  assert.deepEqual(store.snapshot().operations, []);
});

test('an event for an operation the list has never seen adds it', async () => {
  const server = fakeServer([]);
  const store = new OperationCenterStore(server.fetch);
  await store.refresh();
  server.replace(operation('new', 'queued', 1));

  await store.applyChange('new', 1);

  assert.deepEqual(store.snapshot().operations.map((item) => item.operationId), ['new']);
});

test('cancellation is an explicit request that updates from the answer', async () => {
  const server = fakeServer([operation('a', 'running')]);
  const store = new OperationCenterStore(server.fetch);
  await store.refresh();

  await store.cancel('a');

  assert.ok(server.calls.includes('/api/v1/operations/a/cancellation'));
  assert.equal(store.snapshot().operations[0]?.cancellationRequested, true);
});

test('settled work is neither cancellable nor still running', () => {
  assert.equal(isSettled(operation('a', 'succeeded')), true);
  assert.equal(isSettled(operation('a', 'running')), false);
  assert.equal(isCancellable(operation('a', 'running')), true);
  assert.equal(isCancellable(operation('a', 'failed')), false);
  assert.equal(
    isCancellable({ ...operation('a', 'running'), cancellationRequested: true }),
    false,
    'Asking twice is not worth showing as an action.',
  );
});

function operation(id: string, state: OperationState, revision = 1): OperationView {
  return {
    operationId: id,
    operationType: 'package.install',
    sourcePackageId: null,
    targetReference: `target-${id}`,
    state,
    progressPercent: null,
    currentStep: null,
    createdAtUtc: '2026-03-01T08:00:00Z',
    startedAtUtc: null,
    completedAtUtc: null,
    failureCode: null,
    failureDetail: null,
    correlationId: `correlation-${id}`,
    cancellationRequested: false,
    revision,
  };
}

function fakeServer(
  initial: readonly OperationView[],
  options: { readonly pageSize?: number } = {},
): {
  readonly fetch: typeof fetch;
  readonly calls: string[];
  replace: (operation: OperationView) => void;
  remove: (operationId: string) => void;
} {
  const calls: string[] = [];
  let operations = [...initial];
  const pageSize = options.pageSize ?? 50;

  const server = {
    calls,
    replace: (operation: OperationView) => {
      operations = [operation, ...operations.filter((item) => item.operationId !== operation.operationId)];
    },
    remove: (operationId: string) => {
      operations = operations.filter((item) => item.operationId !== operationId);
    },
    fetch: (async (path: string, init: RequestInit = {}) => {
      calls.push(path);
      if (path === '/api/v1/auth/antiforgery') {
        return json({ headerName: 'X-JulOS-Antiforgery', token: 'token-value' });
      }

      const cancellation = /^\/api\/v1\/operations\/([^/]+)\/cancellation$/u.exec(path);
      if (cancellation !== null && (init.method ?? 'GET') === 'POST') {
        const id = cancellation[1] as string;
        const known = operations.find((item) => item.operationId === id);
        assert.ok(known);
        const cancelled = { ...known, cancellationRequested: true, revision: known.revision + 1 };
        server.replace(cancelled);
        return json(cancelled);
      }

      const single = /^\/api\/v1\/operations\/([^/?]+)$/u.exec(path);
      if (single !== null) {
        const known = operations.find((item) => item.operationId === single[1]);
        return known === undefined
          ? json({ status: 404, title: 'Not found', code: 'operation.not_found' }, 404)
          : json(known);
      }

      const url = new URL(path, 'https://julos.test');
      const states = url.searchParams.get('states');
      const cursor = url.searchParams.get('cursor');
      const matching = states === null
        ? operations
        : operations.filter((item) => states.split(',').includes(item.state));
      const start = cursor === null ? 0 : Number(cursor);
      const page = matching.slice(start, start + pageSize);
      const next = start + pageSize < matching.length ? String(start + pageSize) : null;
      return json({ items: page, nextCursor: next });
    }) as unknown as typeof fetch,
  };
  return server;
}

function json(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': status === 200 ? 'application/json' : 'application/problem+json' },
  });
}
