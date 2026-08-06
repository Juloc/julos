import { JulOsApiClient } from './api-client.js';
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
  readonly #api: JulOsApiClient;

  public constructor(fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.#api = new JulOsApiClient(fetchImplementation);
  }

  public readAuthenticationStatus(): Promise<AuthenticationStatus> {
    return this.#api.get<AuthenticationStatus>('/api/v1/auth/status');
  }

  public readProfile(): Promise<UserProfile> {
    return this.#api.get<UserProfile>('/api/v1/profile');
  }

  public readServerVersion(): Promise<ServerVersion> {
    return this.#api.get<ServerVersion>('/api/v1/system/version');
  }
}
