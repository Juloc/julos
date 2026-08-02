export interface PackageFrontendDescriptor {
  readonly packageId: string;
  readonly version: string;
  readonly moduleUrl: string;
  readonly sha256: string;
  readonly exportedElements: readonly string[];
}

export interface PackageFrontendContext {
  readonly packageId: string;
  readonly language: 'en' | 'de';
  readonly theme: 'light' | 'dark';
  readonly invokeCapability: (name: string, operation: string, payload: unknown) => Promise<unknown>;
  readonly openApplication: (applicationId: string, targetId?: string) => void | Promise<void>;
}

export interface PackageFrontendModule {
  readonly register: (context: PackageFrontendContext) => void | Promise<void>;
}

/** Loads package modules only after same-origin fetch and SHA-256 integrity verification. */
export class PackageFrontendHost {
  readonly #fetch: typeof fetch;
  readonly #loaded = new Map<string, Promise<void>>();

  public constructor(fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.#fetch = fetchImplementation;
  }

  public load(
    descriptor: PackageFrontendDescriptor,
    context: PackageFrontendContext,
  ): Promise<void> {
    validateDescriptor(descriptor);
    if (context.packageId !== descriptor.packageId) {
      throw new PackageFrontendError(
        'package.frontend_context_mismatch',
        'Frontend context package identity does not match the module.',
      );
    }

    const identity = `${descriptor.packageId}@${descriptor.version}`;
    const existing = this.#loaded.get(identity);
    if (existing !== undefined) {
      return existing;
    }
    const loading = this.#load(descriptor, Object.freeze({ ...context }));
    this.#loaded.set(identity, loading);
    return loading;
  }

  public createHostElement(elementName: string): HTMLElement {
    if (!customElements.get(elementName)) {
      throw new PackageFrontendError(
        'package.frontend_element_missing',
        `Package element '${elementName}' is not registered.`,
      );
    }
    const shell = document.createElement('section');
    shell.className = 'package-surface';
    const shadow = shell.attachShadow({ mode: 'closed' });
    const element = document.createElement(elementName);
    shadow.append(element);
    return shell;
  }

  async #load(
    descriptor: PackageFrontendDescriptor,
    context: PackageFrontendContext,
  ): Promise<void> {
    const response = await this.#fetch(descriptor.moduleUrl, {
      credentials: 'same-origin',
      headers: { Accept: 'text/javascript' },
    });
    if (!response.ok) {
      throw new PackageFrontendError(
        'package.frontend_download_failed',
        `Package frontend download failed with status ${response.status}.`,
      );
    }
    const bytes = new Uint8Array(await response.arrayBuffer());
    const digest = await crypto.subtle.digest('SHA-256', bytes);
    const actual = toHex(new Uint8Array(digest));
    if (actual !== descriptor.sha256) {
      throw new PackageFrontendError(
        'package.frontend_integrity_failed',
        'Package frontend integrity verification failed.',
      );
    }

    const blob = new Blob([bytes], { type: 'text/javascript' });
    const url = URL.createObjectURL(blob);
    try {
      const module = await import(url) as Partial<PackageFrontendModule>;
      if (typeof module.register !== 'function') {
        throw new PackageFrontendError(
          'package.frontend_contract_invalid',
          'Package frontend does not export register(context).',
        );
      }
      await module.register(context);
      for (const element of descriptor.exportedElements) {
        if (!customElements.get(element)) {
          throw new PackageFrontendError(
            'package.frontend_element_missing',
            `Package frontend did not register '${element}'.`,
          );
        }
      }
    } finally {
      URL.revokeObjectURL(url);
    }
  }
}

export class PackageFrontendError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = 'PackageFrontendError';
    this.code = code;
  }
}

function validateDescriptor(descriptor: PackageFrontendDescriptor): void {
  validateText(descriptor.packageId);
  validateText(descriptor.version);
  if (!descriptor.moduleUrl.startsWith('/api/v1/packages/') || descriptor.moduleUrl.startsWith('//')) {
    throw new PackageFrontendError(
      'package.frontend_url_invalid',
      'Package frontend URL must use the authenticated same-origin package endpoint.',
    );
  }
  if (!/^[0-9a-f]{64}$/u.test(descriptor.sha256)) {
    throw new PackageFrontendError('package.frontend_digest_invalid', 'Package frontend digest is invalid.');
  }
  if (
    descriptor.exportedElements.length === 0
    || descriptor.exportedElements.some((element) => !/^[a-z][a-z0-9]*(?:-[a-z0-9]+)+$/u.test(element))
  ) {
    throw new PackageFrontendError('package.frontend_elements_invalid', 'Package element names are invalid.');
  }
}

function validateText(value: string): void {
  if (value.trim().length === 0 || value !== value.trim() || value.length > 256) {
    throw new PackageFrontendError('package.frontend_value_invalid', 'Package frontend value is invalid.');
  }
}

function toHex(bytes: Uint8Array): string {
  return [...bytes].map((value) => value.toString(16).padStart(2, '0')).join('');
}
