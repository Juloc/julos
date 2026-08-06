import { applyAppearance, isMotionMode, isThemeMode } from './appearance.js';
import { mapClientFailure, type ClientFailureState } from './client-failure.js';
import { DesktopClientServices } from './client-services.js';
import {
  normalizeLanguage,
  translate,
  type ShellMessageKey,
  type SupportedLanguage,
} from './localization.js';
import { SignalRJsonConnection } from './realtime-events.js';
import { ShellApiClient, type ServerVersion } from './shell-api.js';

const shellElementName = 'julos-shell';

const icon = (path: string): string => `
  <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <path d="${path}"></path>
  </svg>`;

const icons = {
  launcher: icon('M4 4h7v7H4V4Zm9 0h7v7h-7V4ZM4 13h7v7H4v-7Zm9 0h7v7h-7v-7Z'),
  search: icon('m20.5 19.1-4.2-4.2a7 7 0 1 0-1.4 1.4l4.2 4.2 1.4-1.4ZM5 10.5a5.5 5.5 0 1 1 11 0 5.5 5.5 0 0 1-11 0Z'),
  notification: icon('M12 22a2.5 2.5 0 0 0 2.45-2h-4.9A2.5 2.5 0 0 0 12 22Zm7-5h-1V10a6 6 0 0 0-5-5.92V3a1 1 0 1 0-2 0v1.08A6 6 0 0 0 6 10v7H5a1 1 0 1 0 0 2h14a1 1 0 1 0 0-2Z'),
  problem: icon('M12 2 1.8 20h20.4L12 2Zm0 4.1L18.8 18H5.2L12 6.1ZM11 9v5h2V9h-2Zm0 6.5v2h2v-2h-2Z'),
  agent: icon('M7 3a2 2 0 0 0-2 2v3H3a2 2 0 0 0-2 2v9h2v-9h2v9h14v-9h2v9h2v-9a2 2 0 0 0-2-2h-2V5a2 2 0 0 0-2-2H7Zm0 2h10v12H7V5Zm2 3v2h2V8H9Zm4 0v2h2V8h-2Zm-4 5v2h6v-2H9Z'),
  settings: icon('M19.1 13.5c.05-.33.08-.66.08-1s-.03-.67-.08-1l2.13-1.66-2-3.46-2.5 1a7.2 7.2 0 0 0-1.73-1L14.62 3h-4l-.38 2.38a7.2 7.2 0 0 0-1.73 1l-2.5-1-2 3.46L6.14 10.5c-.05.33-.08.66-.08 1s.03.67.08 1L4 14.16l2 3.46 2.5-1a7.2 7.2 0 0 0 1.73 1l.38 2.38h4l.38-2.38a7.2 7.2 0 0 0 1.73-1l2.5 1 2-3.46-2.12-1.66ZM12.62 16a3.5 3.5 0 1 1 0-7 3.5 3.5 0 0 1 0 7Z'),
} as const;

export class JulOsShell extends HTMLElement {
  readonly #api: ShellApiClient;
  #language: SupportedLanguage = normalizeLanguage(globalThis.navigator?.language);
  #clockTimer: ReturnType<typeof globalThis.setInterval> | null = null;
  #clientServices: DesktopClientServices | null = null;
  #connected = false;

  public constructor(api = new ShellApiClient()) {
    super();
    this.#api = api;
  }

  public connectedCallback(): void {
    if (this.#connected) {
      return;
    }

    this.#connected = true;
    this.#render();
    this.#bindActions();
    this.#applyLanguage();
    this.#updateClock();
    this.#clockTimer = globalThis.setInterval(() => this.#updateClock(), 30_000);
    void this.#initialize();
  }

  public disconnectedCallback(): void {
    if (this.#clockTimer !== null) {
      globalThis.clearInterval(this.#clockTimer);
      this.#clockTimer = null;
    }

    const services = this.#clientServices;
    this.#clientServices = null;
    if (services !== null) {
      void services.stop();
    }

    this.#connected = false;
  }

  async #initialize(): Promise<void> {
    const authenticated = await this.#loadSession();
    if (!authenticated || !this.#connected) {
      return;
    }

    const refresh = async (): Promise<void> => {
      await this.#loadSession();
    };
    const services = new DesktopClientServices(
      new SignalRJsonConnection(),
      refresh,
      refresh,
    );
    this.#clientServices = services;

    try {
      await services.start();
    } catch (error) {
      this.#showClientFailure(error);
    }
  }

  async #loadSession(): Promise<boolean> {
    const userLabel = this.#requiredElement<HTMLElement>('current-user');
    const versionLabel = this.#requiredElement<HTMLElement>('desktop-version');
    const aboutVersion = this.#requiredElement<HTMLElement>('about-version');
    const aboutComponent = this.#requiredElement<HTMLElement>('about-component');
    this.#hideClientFailure();

    try {
      const status = await this.#api.readAuthenticationStatus();
      if (status.setupRequired) {
        userLabel.textContent = translate(this.#language, 'setupRequired');
        this.#setVersionUnavailable(versionLabel, aboutVersion, aboutComponent);
        return false;
      }

      if (!status.authenticated || status.user === null) {
        userLabel.textContent = translate(this.#language, 'signedOut');
        this.#showClientState('unauthorized');
        this.#setVersionUnavailable(versionLabel, aboutVersion, aboutComponent);
        return false;
      }

      userLabel.textContent = status.user.displayName;
      const [profileResult, versionResult] = await Promise.allSettled([
        this.#api.readProfile(),
        this.#api.readServerVersion(),
      ]);

      let failure: unknown = null;
      if (profileResult.status === 'fulfilled') {
        const profile = profileResult.value;
        this.#language = normalizeLanguage(profile.preferredLanguage);
        document.documentElement.lang = this.#language;
        if (isThemeMode(profile.theme) && isMotionMode(profile.motion)) {
          applyAppearance(document.documentElement, profile.theme, profile.motion);
        }
        userLabel.textContent = profile.displayName;
        this.#applyLanguage();
        this.#updateClock();
      } else {
        failure = profileResult.reason;
      }

      if (versionResult.status === 'fulfilled') {
        this.#showVersion(versionResult.value, versionLabel, aboutVersion, aboutComponent);
      } else {
        failure ??= versionResult.reason;
        this.#setVersionUnavailable(versionLabel, aboutVersion, aboutComponent);
      }

      if (failure !== null) {
        this.#showClientFailure(failure);
        const state = mapClientFailure(failure).state;
        return state !== 'offline' && state !== 'unauthorized';
      }

      return true;
    } catch (error) {
      const state = mapClientFailure(error).state;
      userLabel.textContent = translate(
        this.#language,
        state === 'offline' ? 'offline' : 'signedOut',
      );
      this.#showClientFailure(error);
      this.#setVersionUnavailable(versionLabel, aboutVersion, aboutComponent);
      return false;
    }
  }

  #showVersion(
    serverVersion: ServerVersion,
    versionLabel: HTMLElement,
    aboutVersion: HTMLElement,
    aboutComponent: HTMLElement,
  ): void {
    versionLabel.textContent = `JulOS ${serverVersion.version}`;
    aboutVersion.textContent = serverVersion.version;
    aboutComponent.textContent = serverVersion.component;
  }

  #setVersionUnavailable(
    versionLabel: HTMLElement,
    aboutVersion: HTMLElement,
    aboutComponent: HTMLElement,
  ): void {
    const unavailable = translate(this.#language, 'serverUnavailable');
    versionLabel.textContent = unavailable;
    aboutVersion.textContent = unavailable;
    aboutComponent.textContent = 'JulOS.Server';
  }

  #showClientFailure(error: unknown): void {
    const view = mapClientFailure(error);
    this.#showClientState(view.state, view.detail, view.correlationId);
  }

  #showClientState(
    state: ClientFailureState,
    detail: string | null = null,
    correlationId: string | null = null,
  ): void {
    const notice = this.#requiredElement<HTMLElement>('connection-notice');
    const message = this.#requiredElement<HTMLElement>('connection-message');
    const reference = this.#requiredElement<HTMLElement>('connection-reference');
    const key: ShellMessageKey = state === 'offline'
      ? 'offline'
      : state === 'unauthorized'
        ? 'signedOut'
        : state === 'forbidden'
          ? 'accessDenied'
          : 'requestFailed';

    notice.dataset['state'] = state;
    notice.hidden = false;
    message.textContent = detail ?? translate(this.#language, key);
    reference.textContent = correlationId === null
      ? ''
      : `${translate(this.#language, 'reference')}: ${correlationId}`;
    reference.hidden = correlationId === null;
  }

  #hideClientFailure(): void {
    const notice = this.#requiredElement<HTMLElement>('connection-notice');
    notice.hidden = true;
    delete notice.dataset['state'];
  }

  #applyLanguage(): void {
    this.setAttribute('lang', this.#language);
    for (const element of this.shadowRoot?.querySelectorAll<HTMLElement>('[data-message]') ?? []) {
      const key = element.dataset['message'] as ShellMessageKey | undefined;
      if (key !== undefined) {
        element.textContent = translate(this.#language, key);
      }
    }

    for (const element of this.shadowRoot?.querySelectorAll<HTMLElement>('[data-label]') ?? []) {
      const key = element.dataset['label'] as ShellMessageKey | undefined;
      if (key !== undefined) {
        element.setAttribute('aria-label', translate(this.#language, key));
        element.setAttribute('title', translate(this.#language, key));
      }
    }
  }

  #updateClock(): void {
    const clock = this.shadowRoot?.getElementById('clock');
    if (clock !== null && clock !== undefined) {
      clock.textContent = new Intl.DateTimeFormat(this.#language, {
        hour: '2-digit',
        minute: '2-digit',
      }).format(new Date());
    }
  }

  #bindActions(): void {
    const launcher = this.#requiredElement<HTMLButtonElement>('launcher-button');
    const launcherPanel = this.#requiredElement<HTMLElement>('launcher-panel');
    const search = this.#requiredElement<HTMLButtonElement>('search-button');
    const aboutButton = this.#requiredElement<HTMLButtonElement>('about-button');
    const aboutDialog = this.#requiredElement<HTMLDialogElement>('about-dialog');
    const aboutClose = this.#requiredElement<HTMLButtonElement>('about-close');

    launcher.addEventListener('click', () => {
      const expanded = launcher.getAttribute('aria-expanded') === 'true';
      launcher.setAttribute('aria-expanded', String(!expanded));
      launcherPanel.hidden = expanded;
      if (!expanded) {
        launcherPanel.focus();
      }
    });

    search.addEventListener('click', () => {
      launcher.setAttribute('aria-expanded', 'true');
      launcherPanel.hidden = false;
      launcherPanel.focus();
    });

    aboutButton.addEventListener('click', () => aboutDialog.showModal());
    aboutClose.addEventListener('click', () => aboutDialog.close());
    aboutDialog.addEventListener('click', (event) => {
      if (event.target === aboutDialog) {
        aboutDialog.close();
      }
    });
  }

  #requiredElement<T extends HTMLElement>(id: string): T {
    const element = this.shadowRoot?.getElementById(id);
    if (element === null || element === undefined) {
      throw new Error(`The JulOS shell is missing its '${id}' element.`);
    }

    return element as T;
  }

  #render(): void {
    const shadow = this.attachShadow({ mode: 'open' });
    shadow.innerHTML = `
      <link rel="stylesheet" href="./styles/shell.css" />
      <main class="desktop" aria-label="JulOS Desktop">
        <section class="desktop-content" aria-labelledby="desktop-heading">
          <h1 id="desktop-heading" class="visually-hidden" data-message="desktop"></h1>
          <div id="connection-notice" class="connection-notice" role="alert" hidden>
            <strong id="connection-message"></strong>
            <code id="connection-reference" hidden></code>
          </div>
          <div class="empty-state" role="status">
            <div class="brand-mark" aria-hidden="true">J</div>
            <h2 data-message="noApplicationsTitle"></h2>
            <p data-message="noApplicationsBody"></p>
          </div>
        </section>

        <div id="desktop-version" class="desktop-version" aria-live="polite" data-message="loading"></div>

        <section id="launcher-panel" class="launcher-panel" tabindex="-1" hidden>
          <header>
            <strong>JulOS</strong>
            <span data-message="launcher"></span>
          </header>
          <button type="button" class="launcher-entry" data-label="settings">
            ${icons.settings}<span data-message="settings"></span>
          </button>
          <button id="about-button" type="button" class="launcher-entry" data-label="about">
            <span class="brand-glyph" aria-hidden="true">J</span><span data-message="about"></span>
          </button>
        </section>

        <nav class="taskbar" aria-label="JulOS taskbar">
          <div class="taskbar-primary">
            <button id="launcher-button" type="button" class="taskbar-button launcher-button" aria-expanded="false" aria-controls="launcher-panel" data-label="launcher">
              ${icons.launcher}
            </button>
            <button id="search-button" type="button" class="taskbar-button search-button" data-label="commandPalette">
              ${icons.search}<span data-message="commandPalette"></span>
            </button>
            <div class="running-applications" aria-live="polite"></div>
          </div>
          <div class="status-area">
            <button type="button" class="taskbar-button status-button" data-label="notifications">${icons.notification}</button>
            <button type="button" class="taskbar-button status-button" data-label="problems">${icons.problem}</button>
            <button type="button" class="taskbar-button status-button agent-button" data-label="agentStatus">${icons.agent}<span class="status-dot"></span></button>
            <button type="button" class="user-button" data-label="settings"><span id="current-user" data-message="loading"></span></button>
            <time id="clock" class="clock"></time>
          </div>
        </nav>
      </main>

      <dialog id="about-dialog" class="about-dialog" aria-labelledby="about-title">
        <div class="about-mark" aria-hidden="true">J</div>
        <h2 id="about-title" data-message="about"></h2>
        <dl>
          <div><dt data-message="version"></dt><dd id="about-version" data-message="loading"></dd></div>
          <div><dt data-message="component"></dt><dd id="about-component">JulOS.Server</dd></div>
        </dl>
        <button id="about-close" type="button" class="primary-button" data-message="close"></button>
      </dialog>
    `;
  }
}

export function defineJulOsShell(): void {
  if (!customElements.get(shellElementName)) {
    customElements.define(shellElementName, JulOsShell);
  }
}
