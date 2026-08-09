import assert from 'node:assert/strict';
import { test } from 'node:test';

import { JulOsApiClient, JulOsApiError } from './api-client.js';

test('successful requests use the same-origin session cookie contract', async () => {
  let capturedInput: string | URL | Request | null = null;
  let capturedInit: RequestInit | undefined;
  const fakeFetch: typeof fetch = async (input, init) => {
    capturedInput = input;
    capturedInit = init;
    return new Response(JSON.stringify({ value: 42 }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    });
  };

  const client = new JulOsApiClient(fakeFetch);
  const response = await client.get<{ readonly value: number }>('/api/v1/example');

  assert.equal(response.value, 42);
  assert.equal(capturedInput, '/api/v1/example');
  assert.equal(capturedInit?.credentials, 'same-origin');
  assert.equal(new Headers(capturedInit?.headers).has('Authorization'), false);
});

test('offline transport failures remain distinct from unauthorized responses', async () => {
  const offlineClient = new JulOsApiClient(async () => {
    throw new TypeError('network unavailable');
  });

  await assert.rejects(
    () => offlineClient.get('/api/v1/profile'),
    (error: unknown) =>
      error instanceof JulOsApiError
      && error.kind === 'offline'
      && error.status === null
      && error.retryable,
  );

  const unauthorizedClient = new JulOsApiClient(async () => problemResponse(401, {
    code: 'request.unauthenticated',
    correlationId: 'corr-401',
    retryable: false,
  }));

  await assert.rejects(
    () => unauthorizedClient.get('/api/v1/profile'),
    (error: unknown) =>
      error instanceof JulOsApiError
      && error.kind === 'unauthorized'
      && error.status === 401
      && error.correlationId === 'corr-401'
      && !error.retryable,
  );
});

test('forbidden and ordinary problems retain their stable details', async () => {
  const forbiddenClient = new JulOsApiClient(async () => problemResponse(403, {
    code: 'request.forbidden',
    correlationId: 'corr-403',
    retryable: false,
  }));
  await assert.rejects(
    () => forbiddenClient.get('/api/v1/authorization/roles'),
    (error: unknown) =>
      error instanceof JulOsApiError
      && error.kind === 'forbidden'
      && error.problem?.code === 'request.forbidden',
  );

  const conflictClient = new JulOsApiClient(async () => problemResponse(409, {
    code: 'request.concurrency_conflict',
    correlationId: 'corr-409',
    retryable: false,
    currentRevision: 8,
    fieldErrors: { revision: ['The revision is stale.'] },
  }));
  await assert.rejects(
    () => conflictClient.get('/api/v1/profile'),
    (error: unknown) =>
      error instanceof JulOsApiError
      && error.kind === 'problem'
      && error.problem?.currentRevision === 8
      && error.problem.fieldErrors?.['revision']?.[0] === 'The revision is stale.',
  );
});

test('plain JSON capability failures retain their code and detail', async () => {
  const client = new JulOsApiClient(async () => new Response(JSON.stringify({
    code: 'remote.runtime_unavailable',
    detail: 'No compatible Remote provider runtime is currently available.',
  }), {
    status: 503,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'X-Correlation-Id': 'corr-503',
    },
  }));

  await assert.rejects(
    () => client.get('/api/v1/packages/de.juloc.julos.browser/capabilities/interactive.session/create'),
    (error: unknown) =>
      error instanceof JulOsApiError
      && error.kind === 'problem'
      && error.status === 503
      && error.message === 'No compatible Remote provider runtime is currently available.'
      && error.problem?.code === 'remote.runtime_unavailable'
      && error.correlationId === 'corr-503'
      && error.retryable,
  );
});

test('raw authentication headers and cross-origin URLs are rejected before fetch', async () => {
  let called = false;
  const client = new JulOsApiClient(async () => {
    called = true;
    return new Response('{}');
  });

  await assert.rejects(
    () => client.requestJson('/api/v1/profile', {
      headers: { Authorization: 'Bearer must-not-be-exposed' },
    }),
    /Raw authentication headers/,
  );
  await assert.rejects(
    () => client.get('https://other.example/api/v1/profile'),
    /same-origin absolute paths/,
  );
  assert.equal(called, false);
});

function problemResponse(
  status: number,
  extensions: Readonly<Record<string, unknown>>,
): Response {
  return new Response(JSON.stringify({
    type: 'https://os.juloc.de/problems/request-failed',
    title: 'The request failed.',
    status,
    detail: 'Caller-safe detail.',
    ...extensions,
  }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}
