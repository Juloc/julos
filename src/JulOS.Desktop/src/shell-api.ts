import { JulOsApiClient } from './api-client.js';
import type { MotionMode, ThemeMode } from './appearance.js';
import type { SupportedLanguage } from './localization.js';

export interface AuthenticatedUser {
  readonly userId: string;
  readonly userName: string;
  readonly displayName: string;
}

export interface InitialAdministratorRequest {
  readonly userName: string;
  readonly displayName: string;
  readonly password: string;
}

export interface LocalLoginRequest {
  readonly userName: string;
  readonly password: string;
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

export interface UpdateProfilePreferencesRequest {
  readonly preferredLanguage: SupportedLanguage;
  readonly timeZone: string;
  readonly theme: ThemeMode;
  readonly motion: MotionMode;
  readonly revision: number;
}

export interface AntiforgeryToken {
  readonly headerName: string;
  readonly token: string;
}

export interface ServerVersion {
  readonly component: string;
  readonly version: string;
}

export interface DesktopApplicationFrontend {
  readonly moduleUrl: string;
  readonly sha256: string;
  readonly exportedElements: readonly string[];
}

export interface DesktopApplication {
  readonly applicationDefinitionId: string;
  readonly packageId: string;
  readonly packageVersion: string;
  readonly stableKey: string;
  readonly displayNameKey: string;
  readonly instancePolicy: 'single-instance-per-user' | 'single-instance-per-target' | 'multiple-instances';
  readonly defaultWidth: number;
  readonly defaultHeight: number;
  readonly minimumWidth: number;
  readonly minimumHeight: number;
  readonly viewports: readonly ('desktop' | 'tablet' | 'mobile')[];
  readonly elementName: string;
  readonly frontend: DesktopApplicationFrontend;
}

export class ShellApiClient {
  readonly #api: JulOsApiClient;

  public constructor(fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.#api = new JulOsApiClient(fetchImplementation);
  }

  public readAuthenticationStatus(): Promise<AuthenticationStatus> {
    return this.#api.get<AuthenticationStatus>('/api/v1/auth/status');
  }

  public createInitialAdministrator(
    request: InitialAdministratorRequest,
  ): Promise<AuthenticatedUser> {
    return this.#api.requestJson<AuthenticatedUser>('/api/v1/auth/setup', {
      method: 'POST',
      body: request,
    });
  }

  public login(request: LocalLoginRequest): Promise<AuthenticatedUser> {
    return this.#api.requestJson<AuthenticatedUser>('/api/v1/auth/login', {
      method: 'POST',
      body: request,
    });
  }

  public readProfile(): Promise<UserProfile> {
    return this.#api.get<UserProfile>('/api/v1/profile');
  }

  public async updateProfilePreferences(
    request: UpdateProfilePreferencesRequest,
  ): Promise<UserProfile> {
    const antiforgery = await this.readAntiforgeryToken();
    return this.#api.requestJson<UserProfile>('/api/v1/profile/preferences', {
      method: 'PUT',
      body: request,
      headers: { [antiforgery.headerName]: antiforgery.token },
    });
  }

  public readAntiforgeryToken(): Promise<AntiforgeryToken> {
    return this.#api.get<AntiforgeryToken>('/api/v1/auth/antiforgery');
  }

  public readServerVersion(): Promise<ServerVersion> {
    return this.#api.get<ServerVersion>('/api/v1/system/version');
  }

  public readApplications(viewport: 'desktop' | 'tablet' | 'mobile'): Promise<readonly DesktopApplication[]> {
    return this.#api.get<readonly DesktopApplication[]>(
      `/api/v1/applications?viewport=${encodeURIComponent(viewport)}`,
    );
  }
}
