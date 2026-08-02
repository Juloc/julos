import assert from 'node:assert/strict';
import { test } from 'node:test';

import { JulOsApiError, type JulOsProblemDetails } from './api-client.js';
import { mapClientFailure } from './client-failure.js';

const baseProblem: JulOsProblemDetails = {
  type: 'https://os.juloc.de/problems/request-failed',
  title: 'The request failed.',
  status: 500,
  detail: 'Caller-safe detail.',
  code: 'server.unexpected',
  correlationId: 'corr-test',
  retryable: false,
  sourcePackage: null,
  fieldErrors: null,
  currentRevision: null,
};

test('offline, unauthorized and forbidden remain distinct', () => {
  assert.equal(mapClientFailure(new JulOsApiError('offline', 'offline', null, null)).state, 'offline');
  assert.equal(
    mapClientFailure(new JulOsApiError('unauthorized', 'unauthorized', 401, {
      ...baseProblem,
      status: 401,
      code: 'request.unauthenticated',
    })).state,
    'unauthorized',
  );
  assert.equal(
    mapClientFailure(new JulOsApiError('forbidden', 'forbidden', 403, {
      ...baseProblem,
      status: 403,
      code: 'request.forbidden',
    })).state,
    'forbidden',
  );
});

test('problem details expose only caller-safe detail and correlation reference', () => {
  const view = mapClientFailure(new JulOsApiError('problem', 'failed', 500, baseProblem));

  assert.equal(view.state, 'failed');
  assert.equal(view.detail, 'Caller-safe detail.');
  assert.equal(view.correlationId, 'corr-test');
  assert.equal(view.retryable, false);
});

test('unknown exceptions produce a generic failure without accidental details', () => {
  assert.deepEqual(mapClientFailure(new Error('internal file path')), {
    state: 'failed',
    detail: null,
    correlationId: null,
    retryable: false,
  });
});
