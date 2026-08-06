import { JulOsApiError } from './api-client.js';

export type ClientFailureState = 'offline' | 'unauthorized' | 'forbidden' | 'failed';

export interface ClientFailureView {
  readonly state: ClientFailureState;
  readonly detail: string | null;
  readonly correlationId: string | null;
  readonly retryable: boolean;
}

/** Converts transport and API failures into presentation-neutral client states. */
export function mapClientFailure(error: unknown): ClientFailureView {
  if (!(error instanceof JulOsApiError)) {
    return {
      state: 'failed',
      detail: null,
      correlationId: null,
      retryable: false,
    };
  }

  const state = error.kind === 'offline'
    ? 'offline'
    : error.kind === 'unauthorized'
      ? 'unauthorized'
      : error.kind === 'forbidden'
        ? 'forbidden'
        : 'failed';

  return {
    state,
    detail: state === 'failed' ? error.problem?.detail ?? error.problem?.title ?? null : null,
    correlationId: error.correlationId,
    retryable: error.retryable,
  };
}
