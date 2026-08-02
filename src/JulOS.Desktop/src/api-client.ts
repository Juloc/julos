export type ApiFailureKind =
  | 'offline'
  | 'unauthorized'
  | 'forbidden'
  | 'problem'
  | 'unexpected';

export interface JulOsProblemDetails {
  readonly type: string | null;
  readonly title: string;
  readonly status: number;
  readonly detail: string | null;
  readonly code: string;
  readonly correlationId: string | null;
  readonly retryable: boolean;
  readonly sourcePackage: string | null;
  readonly fieldErrors: Readonly<Record<string, readonly string[]>> | null;
  readonly currentRevision: number | null;
}

/** One normalized failure from the JulOS control-plane API. */
export class JulOsApiError extends Error {
  public readonly kind: ApiFailureKind;
  public readonly status: number | null;
  public readonly problem: JulOsProblemDetails | null;

  public constructor(
    kind: ApiFailureKind,
    message: string,
    status: number | null,
    problem: JulOsProblemDetails | null,
    options?: ErrorOptions,
  ) {
    super(message, options);
    this.name = 'JulOsApiError';
    this.kind = kind;
    this.status = status;
    this.problem = problem;
  }

  public get correlationId(): string | null {
    return this.problem?.correlationId ?? null;
  }

  public get retryable(): boolean {
    return this.problem?.retryable ?? this.kind === 'offline';
  }
}

export interface ApiRequestOptions {
  readonly method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  readonly body?: unknown;
  readonly headers?: HeadersInit;
  readonly signal?: AbortSignal;
}

/**
 * Same-origin API client. Authentication is carried only by the secure session cookie;
 * callers cannot pass an Authorization header or a raw credential through this surface.
 */
export class JulOsApiClient {
  readonly #fetch: typeof fetch;

  public constructor(fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.#fetch = fetchImplementation;
  }

  public get<T>(path: string, signal?: AbortSignal): Promise<T> {
    return this.requestJson<T>(path, { method: 'GET', signal });
  }

  public async requestJson<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
    const response = await this.#request(path, options);
    if (response.status === 204) {
      throw new JulOsApiError(
        'unexpected',
        `The API request '${path}' returned no JSON body.`,
        response.status,
        null,
      );
    }

    try {
      return (await response.json()) as T;
    } catch (cause) {
      throw new JulOsApiError(
        'unexpected',
        `The API request '${path}' returned invalid JSON.`,
        response.status,
        null,
        { cause },
      );
    }
  }

  public async requestVoid(path: string, options: ApiRequestOptions = {}): Promise<void> {
    await this.#request(path, options);
  }

  async #request(path: string, options: ApiRequestOptions): Promise<Response> {
    validatePath(path);
    const headers = new Headers(options.headers);
    if (headers.has('Authorization')) {
      throw new TypeError('Raw authentication headers are not accepted by the JulOS API client.');
    }

    headers.set('Accept', 'application/json');
    let body: BodyInit | undefined;
    if (options.body !== undefined) {
      headers.set('Content-Type', 'application/json');
      body = JSON.stringify(options.body);
    }

    let response: Response;
    try {
      response = await this.#fetch(path, {
        method: options.method ?? 'GET',
        credentials: 'same-origin',
        headers,
        body,
        signal: options.signal,
      });
    } catch (cause) {
      if (isAbort(cause)) {
        throw cause;
      }

      throw new JulOsApiError(
        'offline',
        'The JulOS server could not be reached.',
        null,
        null,
        { cause },
      );
    }

    if (response.ok) {
      return response;
    }

    const problem = await readProblemDetails(response);
    const kind = response.status === 401
      ? 'unauthorized'
      : response.status === 403
        ? 'forbidden'
        : problem === null
          ? 'unexpected'
          : 'problem';
    const message = problem?.detail ?? problem?.title ?? `The request failed with status ${response.status}.`;

    throw new JulOsApiError(kind, message, response.status, problem);
  }
}

function validatePath(path: string): void {
  if (!path.startsWith('/') || path.startsWith('//')) {
    throw new TypeError('JulOS API paths must be same-origin absolute paths.');
  }
}

function isAbort(value: unknown): boolean {
  return value instanceof DOMException && value.name === 'AbortError';
}

async function readProblemDetails(response: Response): Promise<JulOsProblemDetails | null> {
  const contentType = response.headers.get('Content-Type') ?? '';
  if (!contentType.toLowerCase().includes('application/problem+json')) {
    return fallbackProblem(response);
  }

  let raw: unknown;
  try {
    raw = await response.json();
  } catch {
    return fallbackProblem(response);
  }

  if (!isRecord(raw)) {
    return fallbackProblem(response);
  }

  const status = typeof raw['status'] === 'number' ? raw['status'] : response.status;
  const title = typeof raw['title'] === 'string' ? raw['title'] : `Request failed with status ${status}.`;
  const code = typeof raw['code'] === 'string' ? raw['code'] : fallbackCode(status);
  const correlationId = safeOptionalString(raw['correlationId'])
    ?? safeOptionalString(response.headers.get('X-Correlation-Id'));

  return {
    type: safeOptionalString(raw['type']),
    title,
    status,
    detail: safeOptionalString(raw['detail']),
    code,
    correlationId,
    retryable: raw['retryable'] === true,
    sourcePackage: safeOptionalString(raw['sourcePackage']),
    fieldErrors: readFieldErrors(raw['fieldErrors']),
    currentRevision: typeof raw['currentRevision'] === 'number' ? raw['currentRevision'] : null,
  };
}

function fallbackProblem(response: Response): JulOsProblemDetails {
  return {
    type: null,
    title: `Request failed with status ${response.status}.`,
    status: response.status,
    detail: null,
    code: fallbackCode(response.status),
    correlationId: safeOptionalString(response.headers.get('X-Correlation-Id')),
    retryable: response.status === 429 || response.status === 503,
    sourcePackage: null,
    fieldErrors: null,
    currentRevision: null,
  };
}

function fallbackCode(status: number): string {
  switch (status) {
    case 401:
      return 'request.unauthenticated';
    case 403:
      return 'request.forbidden';
    case 404:
      return 'request.not_found';
    case 409:
      return 'request.rule_violation';
    case 429:
      return 'request.rate_limited';
    default:
      return status >= 500 ? 'server.unexpected' : 'request.invalid';
  }
}

function readFieldErrors(value: unknown): Readonly<Record<string, readonly string[]>> | null {
  if (!isRecord(value)) {
    return null;
  }

  const result: Record<string, readonly string[]> = {};
  for (const [field, messages] of Object.entries(value)) {
    if (Array.isArray(messages) && messages.every((message) => typeof message === 'string')) {
      result[field] = messages;
    }
  }

  return Object.keys(result).length > 0 ? result : null;
}

function safeOptionalString(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
