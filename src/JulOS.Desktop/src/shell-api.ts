import type { MotionMode, ThemeMode } from './appearance.js';
import type { SupportedLanguage } from './localization.js';

export interface AuthenticatedUser {
  readonly userId: string;
  readonly userName: string;
  readonly displayName: string;
}

export interface AuthenticationStatus {
  readonly setupRequired: boolean;
  readonly authenticated: boolean;
  readonly user: AuthenticatedUser | null;
}

export interface UserProfile {
  readonly userId: string;
  readonly userName: string;
  readonly displayName: string;
  readonly preferredLanguage: SupportedLanguage;
  readonly timeZone: string;
  readonly theme: ThemeMode;
  readonly motion: MotionMode;
  readonly revision: number;
}

export interface ServerVersion {
  readonly component: string;
  readonly version: string;
}

export class ShellApiClient {
  readonly #fetch: typeof fetch;

  public constructor(fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.#fetch = fetchImplementation;
  }

  public readAuthenticationStatus(): Promise<AuthenticationStatus> {
    return this.#getJson<AuthenticationStatus>('/api/v1/auth/status');
  }

  public readProfile(): Promise<UserProfile> {
    return this.#getJson<UserProfile>('/api/v1/profile');
  }

  public readServerVersion(): Promise<ServerVersion> {
    return this.#getJson<ServerVersion>('/api/v1/system/version');
  }

  async #getJson<T>(path: string): Promise<T> {
    const response = await this.#fetch(path, {
      credentials: 'same-origin',
      headers: { Accept: 'application/json' },
    });

    if (!response.ok) {
      throw new Error(`The shell request '${path}' failed with status ${response.status}.`);
    }

    return (await response.json()) as T;
  }
}
