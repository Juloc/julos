import type { DesktopApplication, ShellApiClient } from './shell-api.js';

/** The synthetic package identity for locally proxied web-application windows. */
export const WebAppPackageId = 'julos.webapp';

export interface WebAppCatalogOptions {
  readonly api: ShellApiClient;
  readonly onFailure: (error: unknown) => void;
}

/**
 * Presents each configured local web-application target as a desktop application whose window
 * renders the target in an iframe (local rendering). The target is reached through the JulOS
 * reverse proxy at its own host; see docs/WEB-APP-RENDERING.md.
 */
export class WebAppCatalog {
  readonly #api: ShellApiClient;
  readonly #onFailure: (error: unknown) => void;
  readonly #hostsById = new Map<string, string>();
  #applications: readonly DesktopApplication[] = [];

  public constructor(options: WebAppCatalogOptions) {
    this.#api = options.api;
    this.#onFailure = options.onFailure;
  }

  public async refresh(): Promise<void> {
    try {
      const targets = await this.#api.readWebApps();
      const applications: DesktopApplication[] = [];
      this.#hostsById.clear();
      for (const target of targets) {
        const id = applicationId(target.host);
        this.#hostsById.set(id, target.host);
        applications.push(webApplication(id, target.host));
      }
      this.#applications = applications;
    } catch (error) {
      this.#applications = [];
      this.#hostsById.clear();
      this.#onFailure(error);
    }
  }

  public applications(): readonly DesktopApplication[] {
    return this.#applications;
  }

  public isWebApp(applicationId: string): boolean {
    return this.#hostsById.has(applicationId);
  }

  public hostFor(applicationId: string): string | null {
    return this.#hostsById.get(applicationId) ?? null;
  }
}

/** Derives a readable window title from a target host, e.g. `unifi.os.juloc.de` -> `Unifi`. */
export function webAppTitle(host: string): string {
  const label = host.split('.')[0] ?? host;
  return label.length === 0 ? host : label.charAt(0).toLocaleUpperCase() + label.slice(1);
}

function applicationId(host: string): string {
  return `${WebAppPackageId}:${host}`;
}

function webApplication(id: string, host: string): DesktopApplication {
  return {
    applicationDefinitionId: id,
    packageId: WebAppPackageId,
    packageVersion: '1',
    stableKey: host,
    displayNameKey: webAppTitle(host),
    instancePolicy: 'multiple-instances',
    defaultWidth: 1024,
    defaultHeight: 720,
    minimumWidth: 480,
    minimumHeight: 360,
    viewports: ['desktop', 'tablet', 'mobile'],
    elementName: '',
    frontend: { moduleUrl: '', sha256: '', exportedElements: [] },
  };
}
