import { JulOsApiClient } from './api-client.js';

export type PackageLifecycleState =
  | 'installing'
  | 'installed'
  | 'configuring'
  | 'disabled'
  | 'starting'
  | 'enabled'
  | 'stopping'
  | 'updating'
  | 'faulted'
  | 'removing'
  | 'removed';

export interface PackageInstallationView {
  readonly installationId: string;
  readonly packageId: string;
  readonly version: string;
  readonly state: PackageLifecycleState;
  readonly revision: number;
  readonly faultCode: string | null;
  readonly faultDetail: string | null;
  readonly faultedAtUtc: string | null;
  readonly configurationRequired: boolean;
  readonly workerHealthy: boolean;
  readonly artifactDigest: string;
}

export interface PackageInstallRequest {
  readonly artifact: File;
  readonly signature: File;
  readonly publisherId: string;
  readonly publisherKeyId: string;
}

export interface PackageManagerSnapshot {
  readonly packages: readonly PackageInstallationView[];
  readonly loading: boolean;
  readonly activePackageId: string | null;
  readonly safeMode: boolean;
  readonly lastError: string | null;
}

export type PackageManagerListener = (snapshot: PackageManagerSnapshot) => void;

interface AntiforgeryToken {
  readonly headerName: string;
  readonly token: string;
}

/** State model for the Core Package Manager application. */
export class PackageManagerStore {
  readonly #api: JulOsApiClient;
  readonly #listeners = new Set<PackageManagerListener>();
  #packages: PackageInstallationView[] = [];
  #loading = false;
  #activePackageId: string | null = null;
  #safeMode = false;
  #lastError: string | null = null;
  #antiforgery: AntiforgeryToken | null = null;

  public constructor(fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.#api = new JulOsApiClient(fetchImplementation);
  }

  public subscribe(listener: PackageManagerListener): () => void {
    this.#listeners.add(listener);
    listener(this.snapshot());
    return () => this.#listeners.delete(listener);
  }

  public snapshot(): PackageManagerSnapshot {
    return {
      packages: [...this.#packages],
      loading: this.#loading,
      activePackageId: this.#activePackageId,
      safeMode: this.#safeMode,
      lastError: this.#lastError,
    };
  }

  public async refresh(): Promise<void> {
    await this.#run(async () => {
      this.#packages = await this.#api.get<PackageInstallationView[]>('/api/v1/packages/');
      if (
        this.#activePackageId !== null
        && !this.#packages.some((item) => item.packageId === this.#activePackageId)
      ) {
        this.#activePackageId = null;
      }
    });
  }

  public async install(request: PackageInstallRequest): Promise<void> {
    validateInstallRequest(request);
    await this.#run(async () => {
      const antiforgery = await this.#readAntiforgery();
      const form = new FormData();
      form.set('Artifact', request.artifact, request.artifact.name);
      form.set('Signature', request.signature, request.signature.name);
      form.set('PublisherId', request.publisherId.trim());
      form.set('PublisherKeyId', request.publisherKeyId.trim());
      form.set('OperationKey', globalThis.crypto.randomUUID());
      const installed = await this.#api.requestJson<PackageInstallationView>('/api/v1/packages/install', {
        method: 'POST',
        formData: form,
        headers: { [antiforgery.headerName]: antiforgery.token },
      });
      this.#packages = [...this.#packages.filter((item) => item.packageId !== installed.packageId), installed]
        .sort((left, right) => left.packageId.localeCompare(right.packageId));
      this.#activePackageId = installed.packageId;
    });
  }

  public select(packageId: string | null): void {
    if (packageId !== null && !this.#packages.some((item) => item.packageId === packageId)) {
      throw new PackageManagerError('package.selection_missing', 'The selected package is not installed.');
    }
    this.#activePackageId = packageId;
    this.#publish();
  }

  public async configure(
    packageId: string,
    revision: number,
    values: Readonly<Record<string, string>>,
  ): Promise<void> {
    await this.#mutate(packageId, 'PUT', `/api/v1/packages/${encodeURIComponent(packageId)}/configuration`, {
      revision,
      values,
    });
  }

  public async enable(packageId: string, revision: number): Promise<void> {
    if (this.#safeMode) {
      throw new PackageManagerError('package.safe_mode', 'Optional packages cannot be enabled in safe mode.');
    }
    await this.#mutate(packageId, 'POST', `/api/v1/packages/${encodeURIComponent(packageId)}/enable`, { revision });
  }

  public async disable(packageId: string, revision: number): Promise<void> {
    await this.#mutate(packageId, 'POST', `/api/v1/packages/${encodeURIComponent(packageId)}/disable`, { revision });
  }

  public async remove(packageId: string, revision: number, deletePackageData: boolean): Promise<void> {
    await this.#mutate(packageId, 'DELETE', `/api/v1/packages/${encodeURIComponent(packageId)}`, {
      revision,
      deletePackageData,
    });
  }

  public setSafeMode(enabled: boolean): void {
    this.#safeMode = enabled;
    this.#publish();
  }

  public statusLabel(item: PackageInstallationView): string {
    if (item.state === 'faulted') {
      return item.faultCode === null ? 'Faulted' : `Faulted · ${item.faultCode}`;
    }
    if (item.configurationRequired) {
      return 'Configuration required';
    }
    if (item.state === 'enabled' && !item.workerHealthy) {
      return 'Enabled · health unavailable';
    }
    return item.state;
  }

  async #mutate(
    packageId: string,
    method: 'POST' | 'PUT' | 'DELETE',
    path: string,
    body: unknown,
  ): Promise<void> {
    await this.#run(async () => {
      const antiforgery = await this.#readAntiforgery();
      const updated = await this.#api.requestJson<PackageInstallationView>(path, {
        method,
        body,
        headers: { [antiforgery.headerName]: antiforgery.token },
      });
      this.#packages = updated.state === 'removed'
        ? this.#packages.filter((item) => item.packageId !== packageId)
        : this.#packages.map((item) => item.packageId === packageId ? updated : item);
    });
  }

  async #readAntiforgery(): Promise<AntiforgeryToken> {
    if (this.#antiforgery === null) {
      this.#antiforgery = await this.#api.get<AntiforgeryToken>('/api/v1/auth/antiforgery');
    }
    return this.#antiforgery;
  }

  async #run(action: () => Promise<void>): Promise<void> {
    this.#loading = true;
    this.#lastError = null;
    this.#publish();
    try {
      await action();
    } catch (error) {
      this.#lastError = error instanceof Error ? error.message : 'Package operation failed.';
      throw error;
    } finally {
      this.#loading = false;
      this.#publish();
    }
  }

  #publish(): void {
    const snapshot = this.snapshot();
    for (const listener of this.#listeners) {
      listener(snapshot);
    }
  }
}

export class PackageManagerError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = 'PackageManagerError';
    this.code = code;
  }
}

function validateInstallRequest(request: PackageInstallRequest): void {
  if (request.artifact.size < 1 || request.signature.size < 1) {
    throw new PackageManagerError('package.upload_invalid', 'Package and signature files are required.');
  }
  if (request.publisherId.trim().length === 0 || request.publisherKeyId.trim().length === 0) {
    throw new PackageManagerError('package.publisher_missing', 'Publisher and publisher key are required.');
  }
}
