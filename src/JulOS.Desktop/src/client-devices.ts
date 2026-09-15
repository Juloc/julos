// MOB-003: the client-device registration and preference client from
// docs/MOBILE_PWA.md sections 3 and 4.
//
// A client device is not an authentication factor. The raw client instance key lives only
// in the HTTP-only `.JulOS.Device` cookie, so nothing in this module can read, store or
// forward it; every request here carries the session cookie and an antiforgery token and
// is answered only with records the authenticated user already owns.

import { JulOsApiClient } from './api-client.js';
import type { SupportedLanguage } from './localization.js';
import {
  classifyWorkspace,
  workspaceClasses,
  type LayoutScope,
  type PresentationCapabilities,
  type RestoreMode,
  type WorkspaceClass,
  type WorkspaceClassOverride,
} from './workspace-contract.js';

/** One device's stored preference for a single workspace class. */
export interface DeviceWorkspacePreferenceView {
  readonly workspaceClass: WorkspaceClass;
  readonly layoutScope: LayoutScope;
  readonly restoreMode: RestoreMode;
}

/** A registered client device as its owner sees it. */
export interface ClientDeviceView {
  readonly clientDeviceId: string;
  readonly displayName: string;
  readonly lastDetectedWorkspaceClass: WorkspaceClass;
  readonly workspaceClassOverride: WorkspaceClassOverride | null;
  readonly createdAtUtc: string;
  readonly lastSeenAtUtc: string;
  readonly revision: number;
  readonly preferences: readonly DeviceWorkspacePreferenceView[];
  readonly isCurrentDevice: boolean;
}

/** What the Settings surface renders. */
export interface ClientDeviceSnapshot {
  readonly devices: readonly ClientDeviceView[];
  readonly loading: boolean;
  readonly lastError: string | null;
}

export type ClientDeviceListener = (snapshot: ClientDeviceSnapshot) => void;

/**
 * What a workspace class resolves to before the user has chosen anything for it.
 *
 * Section 4 resolves an absent preference as the shared layout restored on load, so the
 * Settings surface shows that as the current value instead of an empty control.
 */
export const defaultDevicePreference: Readonly<{
  layoutScope: LayoutScope;
  restoreMode: RestoreMode;
}> = Object.freeze({ layoutScope: 'shared', restoreMode: 'resume' });

/** The inputs classification is allowed to read, as a browser exposes them. */
export interface CapabilitySource {
  matchMedia(query: string): { readonly matches: boolean };
  readonly screenWidthCssPx: number;
  readonly screenHeightCssPx: number;
  readonly layoutViewportWidthCssPx: number;
}

/** Reads the four permitted capability inputs. No user-agent or hardware value is read. */
export function readPresentationCapabilities(source: CapabilitySource): PresentationCapabilities {
  return {
    primaryPointerCoarse: source.matchMedia('(pointer: coarse)').matches,
    anyPointerFine: source.matchMedia('(any-pointer: fine)').matches,
    screenMinimumDimensionCssPx: Math.min(source.screenWidthCssPx, source.screenHeightCssPx),
    layoutViewportWidthCssPx: source.layoutViewportWidthCssPx,
  };
}

/** Classifies this client exactly as section 2 specifies. */
export function detectWorkspaceClass(source: CapabilitySource): WorkspaceClassOverride {
  return classifyWorkspace(readPresentationCapabilities(source));
}

/** Reads the capability source from a live browser window. */
export function windowCapabilitySource(view: Window): CapabilitySource {
  return {
    matchMedia: (query: string) => view.matchMedia(query),
    screenWidthCssPx: view.screen.width,
    screenHeightCssPx: view.screen.height,
    // Deliberately the layout viewport: a software keyboard shrinks the visual viewport
    // and must never reclassify the workspace.
    layoutViewportWidthCssPx: view.document.documentElement.clientWidth,
  };
}

/** The workspace class a device presents: its pin when set, otherwise what it detected. */
export function effectiveWorkspaceClass(device: ClientDeviceView): WorkspaceClass {
  return device.workspaceClassOverride ?? device.lastDetectedWorkspaceClass;
}

/** The stored preference for a workspace class, or the documented default. */
export function preferenceFor(
  device: ClientDeviceView,
  workspaceClass: WorkspaceClass,
): DeviceWorkspacePreferenceView {
  const stored = device.preferences.find((preference) => preference.workspaceClass === workspaceClass);
  return stored ?? {
    workspaceClass,
    layoutScope: defaultDevicePreference.layoutScope,
    restoreMode: defaultDevicePreference.restoreMode,
  };
}

/**
 * The workspace classes whose preference is reachable for one device.
 *
 * A device presents exactly one class — its pin when set, otherwise what it detected — so
 * offering all four would show controls that can never take effect. `desktop-multi` is
 * added for desktop devices because the Multi-Display controller can still enter it, and
 * any class that already has a stored preference is added so an existing choice is never
 * hidden behind a pin the user changed later.
 */
export function preferenceWorkspaceClasses(device: ClientDeviceView): readonly WorkspaceClass[] {
  const effective = effectiveWorkspaceClass(device);
  const reachable = new Set<WorkspaceClass>([effective]);

  if (effective === 'desktop-single' || effective === 'desktop-multi') {
    reachable.add('desktop-multi');
  }
  for (const preference of device.preferences) {
    reachable.add(preference.workspaceClass);
  }

  return workspaceClasses.filter((workspaceClass) => reachable.has(workspaceClass));
}

/** The name a newly registered device carries until its owner renames it. */
export function defaultDeviceName(
  workspaceClass: WorkspaceClassOverride,
  language: SupportedLanguage,
): string {
  return defaultDeviceNames[language][workspaceClass];
}

const defaultDeviceNames: Readonly<
  Record<SupportedLanguage, Readonly<Record<WorkspaceClassOverride, string>>>
> = Object.freeze({
  en: { phone: 'Phone', tablet: 'Tablet', 'desktop-single': 'Computer' },
  de: { phone: 'Telefon', tablet: 'Tablet', 'desktop-single': 'Computer' },
});

interface AntiforgeryToken {
  readonly headerName: string;
  readonly token: string;
}

interface Registration {
  readonly displayName: string;
  readonly detectedWorkspaceClass: WorkspaceClassOverride;
}

/** State model for client-device registration and the Settings device list. */
export class ClientDeviceStore {
  readonly #api: JulOsApiClient;
  readonly #listeners = new Set<ClientDeviceListener>();
  #devices: ClientDeviceView[] = [];
  #loading = false;
  #lastError: string | null = null;
  #antiforgery: AntiforgeryToken | null = null;
  #registration: Registration | null = null;

  public constructor(fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.#api = new JulOsApiClient(fetchImplementation);
  }

  public subscribe(listener: ClientDeviceListener): () => void {
    this.#listeners.add(listener);
    listener(this.snapshot());
    return () => this.#listeners.delete(listener);
  }

  public snapshot(): ClientDeviceSnapshot {
    return {
      devices: [...this.#devices],
      loading: this.#loading,
      lastError: this.#lastError,
    };
  }

  /** The device this browser is, once registration has resolved it. */
  public currentDevice(): ClientDeviceView | null {
    return this.#devices.find((device) => device.isCurrentDevice) ?? null;
  }

  /**
   * Registers this browser, or resolves the device its cookie already names.
   *
   * Server decides which of the two happened; a client cannot ask for a specific device
   * identity, and no key is returned to this code either way.
   */
  public async register(
    displayName: string,
    detectedWorkspaceClass: WorkspaceClassOverride,
  ): Promise<void> {
    const registration: Registration = { displayName, detectedWorkspaceClass };
    this.#registration = registration;
    await this.#run(() => this.#register(registration));
  }

  public async refresh(): Promise<void> {
    await this.#run(() => this.#load());
  }

  /** Renames a device and sets or clears its workspace pin in one authoritative write. */
  public async update(
    device: ClientDeviceView,
    displayName: string,
    workspaceClassOverride: WorkspaceClassOverride | null,
  ): Promise<void> {
    await this.#write(`/api/v1/client-devices/${encodeURIComponent(device.clientDeviceId)}`, 'PUT', {
      displayName,
      workspaceClassOverride,
      expectedRevision: device.revision,
    });
  }

  /** Sets this device's layout scope and restore mode for one workspace class. */
  public async setPreference(
    device: ClientDeviceView,
    workspaceClass: WorkspaceClass,
    layoutScope: LayoutScope,
    restoreMode: RestoreMode,
  ): Promise<void> {
    const path = `/api/v1/client-devices/${encodeURIComponent(device.clientDeviceId)}`
      + `/preferences/${encodeURIComponent(workspaceClass)}`;
    await this.#write(path, 'PUT', {
      layoutScope,
      restoreMode,
      expectedRevision: device.revision,
    });
  }

  /**
   * Removes a device and its device-scoped preferences.
   *
   * Removing the device you are using clears its cookie, so this browser would otherwise
   * be left with no device at all. Registering again immediately reproduces exactly what
   * the next page load would do and keeps the list the user is looking at truthful.
   */
  public async remove(device: ClientDeviceView): Promise<void> {
    const removingCurrent = device.isCurrentDevice;
    await this.#run(async () => {
      const antiforgery = await this.#readAntiforgery();
      const path = `/api/v1/client-devices/${encodeURIComponent(device.clientDeviceId)}`
        + `?revision=${encodeURIComponent(String(device.revision))}`;
      await this.#api.requestVoid(path, {
        method: 'DELETE',
        headers: { [antiforgery.headerName]: antiforgery.token },
      });

      if (removingCurrent && this.#registration !== null) {
        await this.#register(this.#registration);
        return;
      }

      await this.#load();
    });
  }

  async #write(path: string, method: 'PUT', body: unknown): Promise<void> {
    await this.#run(async () => {
      const antiforgery = await this.#readAntiforgery();
      try {
        await this.#api.requestJson<ClientDeviceView>(path, {
          method,
          body,
          headers: { [antiforgery.headerName]: antiforgery.token },
        });
      } finally {
        // A rejected write leaves the caller holding a stale revision, so the
        // authoritative list is reloaded whether the write succeeded or conflicted.
        await this.#load();
      }
    });
  }

  async #register(registration: Registration): Promise<void> {
    const antiforgery = await this.#readAntiforgery();
    await this.#api.requestJson<ClientDeviceView>('/api/v1/client-devices/registration', {
      method: 'POST',
      body: {
        displayName: registration.displayName,
        detectedWorkspaceClass: registration.detectedWorkspaceClass,
      },
      headers: { [antiforgery.headerName]: antiforgery.token },
    });
    await this.#load();
  }

  async #load(): Promise<void> {
    this.#devices = [...await this.#api.get<ClientDeviceView[]>('/api/v1/client-devices')];
  }

  async #readAntiforgery(): Promise<AntiforgeryToken> {
    this.#antiforgery ??= await this.#api.get<AntiforgeryToken>('/api/v1/auth/antiforgery');
    return this.#antiforgery;
  }

  async #run(action: () => Promise<void>): Promise<void> {
    this.#loading = true;
    this.#lastError = null;
    this.#publish();
    try {
      await action();
    } catch (error) {
      this.#lastError = error instanceof Error && error.message.trim().length > 0
        ? error.message
        : 'The client-device request failed.';
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
