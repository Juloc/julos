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

export interface OfficialPackageStoreView {
  readonly packageId: string;
  readonly version: string;
  readonly displayNameEn: string;
  readonly displayNameDe: string;
  readonly descriptionEn: string;
  readonly descriptionDe: string;
  readonly installedVersion: string | null;
  readonly installedState: PackageLifecycleState | null;
  readonly installedRevision: number | null;
  readonly updateAvailable: boolean;
}

export interface PackageInstallRequest {
  readonly artifact: File;
  /** Absent for an unsigned artifact, which installs after an explicit warning. */
  readonly signature: File | null;
  readonly publisherId: string;
  readonly publisherKeyId: string;
}

export type PackageSignatureStateView = 'trusted-signed' | 'unknown-signed' | 'unsigned';

/** What installing one artifact would mean, as the server assessed it. */
export interface PackageInstallPreviewView {
  readonly packageId: string;
  readonly version: string;
  readonly artifactDigest: string;
  readonly signatureState: PackageSignatureStateView;
  readonly publisherId: string | null;
  readonly keyId: string | null;
  readonly publicKeyFingerprint: string | null;
  readonly permissions: readonly string[];
  readonly runtimeKind: string;
  readonly networkAccess: boolean;
  readonly criticalRightsDigest: string;
  readonly warnings: readonly string[];
  readonly requiresIsolation: boolean;
  readonly acknowledgementRequired: boolean;
  readonly acknowledgementDigest: string;
  readonly alreadyInstalled: boolean;
}

/**
 * Asked before anything is installed, and answered by whoever is looking at the warnings.
 *
 * The store owns both calls so the acknowledgement the server issued is the one the store
 * sends back. A caller cannot install without previewing, because it never sees the
 * acknowledgement to skip.
 */
export type PackageInstallConfirmation =
  (preview: PackageInstallPreviewView) => boolean | Promise<boolean>;

export interface PackageManagerSnapshot {
  readonly packages: readonly PackageInstallationView[];
  readonly catalog: readonly OfficialPackageStoreView[];
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

/** State model for the Core Package Manager application and official package store. */
export class PackageManagerStore {
  readonly #api: JulOsApiClient;
  readonly #listeners = new Set<PackageManagerListener>();
  #packages: PackageInstallationView[] = [];
  #catalog: OfficialPackageStoreView[] = [];
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
      catalog: [...this.#catalog],
      loading: this.#loading,
      activePackageId: this.#activePackageId,
      safeMode: this.#safeMode,
      lastError: this.#lastError,
    };
  }

  public async refresh(): Promise<void> {
    await this.#run(() => this.#load());
  }

  /**
   * Installs one official package after its assessment was confirmed.
   *
   * @returns Whether it was installed; false when the confirmation was declined.
   */
  public async installOfficial(
    packageId: string,
    confirm: PackageInstallConfirmation,
  ): Promise<boolean> {
    if (this.#safeMode) {
      throw new PackageManagerError('package.safe_mode', 'Optional packages cannot be installed in safe mode.');
    }
    let installed = false;
    await this.#run(async () => {
      const antiforgery = await this.#readAntiforgery();
      const preview = await this.#api.requestJson<PackageInstallPreviewView>(
        `/api/v1/packages/catalog/${encodeURIComponent(packageId)}/previews`,
        {
          method: 'POST',
          body: {},
          headers: { [antiforgery.headerName]: antiforgery.token },
        },
      );
      if (preview.acknowledgementRequired && !(await confirm(preview))) {
        return;
      }

      await this.#api.requestJson<PackageInstallationView>(
        `/api/v1/packages/catalog/${encodeURIComponent(packageId)}/install`,
        {
          method: 'POST',
          body: { acknowledgementDigest: preview.acknowledgementDigest },
          headers: { [antiforgery.headerName]: antiforgery.token },
        },
      );
      await this.#load();
      this.#activePackageId = packageId;
      installed = true;
    });
    return installed;
  }

  /**
   * Installs one uploaded artifact after its assessment was confirmed.
   *
   * Both calls carry the same operation key, because the acknowledgement is bound to it:
   * an approval obtained for one install cannot be presented for another.
   *
   * @returns Whether it was installed; false when the confirmation was declined.
   */
  public async install(
    request: PackageInstallRequest,
    confirm: PackageInstallConfirmation,
  ): Promise<boolean> {
    validateInstallRequest(request);
    let installed = false;
    await this.#run(async () => {
      const antiforgery = await this.#readAntiforgery();
      const operationKey = globalThis.crypto.randomUUID();
      const preview = await this.#api.requestJson<PackageInstallPreviewView>(
        '/api/v1/packages/previews',
        {
          method: 'POST',
          formData: uploadForm(request, operationKey, null),
          headers: { [antiforgery.headerName]: antiforgery.token },
        },
      );
      if (preview.acknowledgementRequired && !(await confirm(preview))) {
        return;
      }

      const result = await this.#api.requestJson<PackageInstallationView>('/api/v1/packages/install', {
        method: 'POST',
        formData: uploadForm(request, operationKey, preview.acknowledgementDigest),
        headers: { [antiforgery.headerName]: antiforgery.token },
      });
      await this.#load();
      this.#activePackageId = result.packageId;
      installed = true;
    });
    return installed;
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
    await this.#mutate('PUT', `/api/v1/packages/${encodeURIComponent(packageId)}/configuration`, { revision, values });
  }

  public async enable(packageId: string, revision: number): Promise<void> {
    if (this.#safeMode) {
      throw new PackageManagerError('package.safe_mode', 'Optional packages cannot be enabled in safe mode.');
    }
    await this.#mutate('POST', `/api/v1/packages/${encodeURIComponent(packageId)}/enable`, { revision });
  }

  public async disable(packageId: string, revision: number): Promise<void> {
    await this.#mutate('POST', `/api/v1/packages/${encodeURIComponent(packageId)}/disable`, { revision });
  }

  public async remove(packageId: string, revision: number, deletePackageData: boolean): Promise<void> {
    await this.#mutate('DELETE', `/api/v1/packages/${encodeURIComponent(packageId)}`, { revision, deletePackageData });
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

  async #mutate(method: 'POST' | 'PUT' | 'DELETE', path: string, body: unknown): Promise<void> {
    await this.#run(async () => {
      const antiforgery = await this.#readAntiforgery();
      await this.#api.requestJson<PackageInstallationView>(path, {
        method,
        body,
        headers: { [antiforgery.headerName]: antiforgery.token },
      });
      await this.#load();
    });
  }

  async #load(): Promise<void> {
    const [packages, catalog] = await Promise.all([
      this.#api.get<PackageInstallationView[]>('/api/v1/packages/'),
      this.#api.get<OfficialPackageStoreView[]>('/api/v1/packages/catalog'),
    ]);
    this.#packages = packages;
    this.#catalog = catalog;
    if (this.#activePackageId !== null && !packages.some((item) => item.packageId === this.#activePackageId)) {
      this.#activePackageId = null;
    }
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

/**
 * Refuses an upload that cannot be assessed at all.
 *
 * A signature is optional, but half a claim is not: a signature without a publisher, or a
 * publisher without a signature, is something the server would have to call invalid, and
 * saying so here costs the administrator one round trip less.
 */
function validateInstallRequest(request: PackageInstallRequest): void {
  if (request.artifact.size < 1) {
    throw new PackageManagerError('package.upload_invalid', 'A package file is required.');
  }

  const signed = request.signature !== null && request.signature.size > 0;
  const named = request.publisherId.trim().length > 0 && request.publisherKeyId.trim().length > 0;
  if (signed !== named) {
    throw new PackageManagerError(
      'package.publisher_missing',
      'A signed package needs both a signature file and its publisher identity.',
    );
  }
}

/** Builds the upload both the preview and the install send. */
function uploadForm(
  request: PackageInstallRequest,
  operationKey: string,
  acknowledgementDigest: string | null,
): FormData {
  const form = new FormData();
  form.set('Artifact', request.artifact, request.artifact.name);
  if (request.signature !== null && request.signature.size > 0) {
    form.set('Signature', request.signature, request.signature.name);
    form.set('PublisherId', request.publisherId.trim());
    form.set('PublisherKeyId', request.publisherKeyId.trim());
  }

  form.set('OperationKey', operationKey);
  if (acknowledgementDigest !== null) {
    form.set('AcknowledgementDigest', acknowledgementDigest);
  }

  return form;
}
