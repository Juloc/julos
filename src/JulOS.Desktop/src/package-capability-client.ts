import { JulOsApiClient } from './api-client.js';

interface AntiforgeryToken {
  readonly headerName: string;
  readonly token: string;
}

/** Binds capability calls to one package identity and the authenticated same-origin API. */
export class PackageCapabilityClient {
  readonly #api: JulOsApiClient;
  #antiforgery: AntiforgeryToken | null = null;

  public constructor(fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.#api = new JulOsApiClient(fetchImplementation);
  }

  public async invoke<T>(
    packageId: string,
    capabilityName: string,
    operation: string,
    payload: unknown,
    signal?: AbortSignal,
  ): Promise<T> {
    validateSegment(packageId, 'package identity');
    validateSegment(capabilityName, 'capability identity');
    validateSegment(operation, 'capability operation');
    const antiforgery = await this.#readAntiforgery();
    const path = `/api/v1/packages/${encodeURIComponent(packageId)}`
      + `/capabilities/${encodeURIComponent(capabilityName)}/${encodeURIComponent(operation)}`;
    return this.#api.requestJson<T>(path, {
      method: 'POST',
      body: { payload },
      headers: { [antiforgery.headerName]: antiforgery.token },
      ...(signal === undefined ? {} : { signal }),
    });
  }

  async #readAntiforgery(): Promise<AntiforgeryToken> {
    if (this.#antiforgery === null) {
      this.#antiforgery = await this.#api.get<AntiforgeryToken>('/api/v1/auth/antiforgery');
    }

    return this.#antiforgery;
  }
}

function validateSegment(value: string, label: string): void {
  if (
    value.trim().length === 0
    || value !== value.trim()
    || value.length > 128
    || /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError(`The ${label} is invalid.`);
  }
}
