import { AgentDashboardStore, type AgentDashboardEntry } from './agent-dashboard.js';
import { NotificationCenterStore, type NotificationCenterSnapshot } from './notification-center.js';
import {
  PackageManagerStore,
  type PackageInstallationView,
  type PackageManagerSnapshot,
} from './package-manager.js';
import type { SupportedLanguage } from './localization.js';
import type { DesktopApplication, ShellApiClient, UserProfile } from './shell-api.js';

export const CoreApplicationIds = {
  settings: 'core.settings',
  packages: 'core.packages',
  agents: 'core.agents',
  notifications: 'core.notifications',
  problems: 'core.problems',
} as const;

export interface CoreSurfaceHandle {
  readonly element: HTMLElement;
  readonly dispose: () => void;
}

export interface CoreApplicationCatalogOptions {
  readonly api: ShellApiClient;
  readonly notifications: NotificationCenterStore;
  readonly language: () => SupportedLanguage;
  readonly onFailure: (error: unknown) => void;
  readonly onProfileChanged: () => void | Promise<void>;
  readonly onPackagesChanged: () => void | Promise<void>;
}

/** Provides JulOS-owned system utilities as normal windows in the desktop runtime. */
export class CoreApplicationCatalog {
  readonly #api: ShellApiClient;
  readonly #notifications: NotificationCenterStore;
  readonly #language: () => SupportedLanguage;
  readonly #onFailure: (error: unknown) => void;
  readonly #onProfileChanged: () => void | Promise<void>;
  readonly #onPackagesChanged: () => void | Promise<void>;
  readonly #applicationIds = new Set<string>(Object.values(CoreApplicationIds));

  public constructor(options: CoreApplicationCatalogOptions) {
    this.#api = options.api;
    this.#notifications = options.notifications;
    this.#language = options.language;
    this.#onFailure = options.onFailure;
    this.#onProfileChanged = options.onProfileChanged;
    this.#onPackagesChanged = options.onPackagesChanged;
  }

  public applications(): readonly DesktopApplication[] {
    const language = this.#language();
    return [
      coreApplication(CoreApplicationIds.settings, text(language, 'settings'), 'settings', 720, 560, 420, 360),
      coreApplication(CoreApplicationIds.packages, text(language, 'packages'), 'package-manager', 860, 620, 520, 380),
      coreApplication(CoreApplicationIds.agents, text(language, 'agents'), 'agents', 820, 580, 480, 360),
      coreApplication(CoreApplicationIds.notifications, text(language, 'notifications'), 'notifications', 720, 560, 420, 340),
      coreApplication(CoreApplicationIds.problems, text(language, 'problems'), 'problems', 760, 580, 440, 360),
    ];
  }

  public isCoreApplication(applicationId: string): boolean {
    return this.#applicationIds.has(applicationId);
  }

  public createSurface(applicationId: string): CoreSurfaceHandle {
    switch (applicationId) {
      case CoreApplicationIds.settings:
        return this.#createSettingsSurface();
      case CoreApplicationIds.packages:
        return this.#createPackageManagerSurface();
      case CoreApplicationIds.agents:
        return this.#createAgentSurface();
      case CoreApplicationIds.notifications:
        return this.#createNotificationSurface(false);
      case CoreApplicationIds.problems:
        return this.#createNotificationSurface(true);
      default:
        throw new Error(`Core application '${applicationId}' is not registered.`);
    }
  }

  #createSettingsSurface(): CoreSurfaceHandle {
    const language = this.#language();
    const root = section('core-settings');
    const heading = document.createElement('h2');
    heading.textContent = text(language, 'settings');
    const status = statusText(text(language, 'loading'));
    const form = document.createElement('form');
    form.className = 'core-form';
    form.hidden = true;

    const languageSelect = selectField(text(language, 'language'), [
      ['en', 'English'],
      ['de', 'Deutsch'],
    ]);
    const themeSelect = selectField(text(language, 'theme'), [
      ['system', text(language, 'system')],
      ['light', text(language, 'light')],
      ['dark', text(language, 'dark')],
    ]);
    const motionSelect = selectField(text(language, 'motion'), [
      ['enabled', text(language, 'motionEnabled')],
      ['reduced', text(language, 'motionReduced')],
    ]);
    const timeZoneField = inputField(text(language, 'timeZone'));
    timeZoneField.input.autocomplete = 'off';
    timeZoneField.input.spellcheck = false;

    const save = document.createElement('button');
    save.type = 'submit';
    save.className = 'core-primary-button';
    save.textContent = text(language, 'save');
    form.append(
      languageSelect.label,
      themeSelect.label,
      motionSelect.label,
      timeZoneField.label,
      save,
    );
    root.append(heading, status, form);

    let profile: UserProfile | null = null;
    const load = async (): Promise<void> => {
      try {
        profile = await this.#api.readProfile();
        languageSelect.select.value = profile.preferredLanguage;
        themeSelect.select.value = profile.theme;
        motionSelect.select.value = profile.motion;
        timeZoneField.input.value = profile.timeZone;
        form.hidden = false;
        status.hidden = true;
      } catch (error) {
        status.textContent = errorMessage(error, text(language, 'requestFailed'));
        this.#onFailure(error);
      }
    };

    form.addEventListener('submit', (event) => {
      event.preventDefault();
      if (profile === null) {
        return;
      }
      save.disabled = true;
      status.hidden = false;
      status.textContent = text(language, 'saving');
      void this.#api.updateProfilePreferences({
        preferredLanguage: languageSelect.select.value === 'de' ? 'de' : 'en',
        timeZone: timeZoneField.input.value.trim(),
        theme: themeValue(themeSelect.select.value),
        motion: motionSelect.select.value === 'reduced' ? 'reduced' : 'enabled',
        revision: profile.revision,
      }).then(async (updated) => {
        profile = updated;
        status.textContent = text(language, 'saved');
        await this.#onProfileChanged();
      }).catch((error: unknown) => {
        status.textContent = errorMessage(error, text(language, 'requestFailed'));
        this.#onFailure(error);
      }).finally(() => {
        save.disabled = false;
      });
    });

    void load();
    return { element: root, dispose: () => undefined };
  }

  #createPackageManagerSurface(): CoreSurfaceHandle {
    const language = this.#language();
    const store = new PackageManagerStore();
    const root = section('core-packages');
    const toolbar = coreToolbar(text(language, 'packages'), text(language, 'refresh'));
    const install = this.#packageInstallForm(store, language);
    const list = document.createElement('div');
    list.className = 'core-list';
    root.append(toolbar.root, install.element, list);

    const render = (snapshot: PackageManagerSnapshot): void => {
      toolbar.button.disabled = snapshot.loading;
      install.setBusy(snapshot.loading);
      list.replaceChildren();
      if (snapshot.lastError !== null) {
        list.append(statusText(snapshot.lastError, 'error'));
      }
      if (!snapshot.loading && snapshot.packages.length === 0) {
        list.append(emptyMessage(text(language, 'noPackages')));
        return;
      }
      for (const item of snapshot.packages) {
        list.append(this.#packageCard(store, item, language));
      }
    };

    const unsubscribe = store.subscribe(render);
    toolbar.button.addEventListener('click', () => void store.refresh().catch(this.#onFailure));
    void store.refresh().catch(this.#onFailure);
    return { element: root, dispose: unsubscribe };
  }

  #packageInstallForm(
    store: PackageManagerStore,
    language: SupportedLanguage,
  ): { element: HTMLElement; setBusy: (busy: boolean) => void } {
    const details = document.createElement('details');
    details.className = 'package-install';
    const summary = document.createElement('summary');
    summary.textContent = text(language, 'installPackage');
    const form = document.createElement('form');
    form.className = 'core-form package-install-form';
    const artifact = fileField(text(language, 'packageFile'), '.zip,application/zip');
    const signature = fileField(text(language, 'signatureFile'), undefined);
    const publisher = inputField(text(language, 'publisherId'));
    const publisherKey = inputField(text(language, 'publisherKeyId'));
    const status = statusText('');
    status.hidden = true;
    const submit = document.createElement('button');
    submit.type = 'submit';
    submit.className = 'core-primary-button';
    submit.textContent = text(language, 'install');
    form.append(artifact.label, signature.label, publisher.label, publisherKey.label, submit, status);
    details.append(summary, form);

    form.addEventListener('submit', (event) => {
      event.preventDefault();
      const artifactFile = artifact.input.files?.item(0) ?? null;
      const signatureFile = signature.input.files?.item(0) ?? null;
      if (artifactFile === null || signatureFile === null) {
        status.hidden = false;
        status.textContent = text(language, 'filesRequired');
        status.className = 'core-status core-status-error';
        return;
      }

      status.hidden = false;
      status.className = 'core-status';
      status.textContent = text(language, 'installing');
      void store.install({
        artifact: artifactFile,
        signature: signatureFile,
        publisherId: publisher.input.value,
        publisherKeyId: publisherKey.input.value,
      }).then(async () => {
        form.reset();
        status.textContent = text(language, 'installed');
        await this.#onPackagesChanged();
      }).catch((error: unknown) => {
        status.className = 'core-status core-status-error';
        status.textContent = errorMessage(error, text(language, 'requestFailed'));
        this.#onFailure(error);
      });
    });

    return {
      element: details,
      setBusy: (busy) => {
        for (const control of form.querySelectorAll<HTMLInputElement | HTMLButtonElement>('input, button')) {
          control.disabled = busy;
        }
      },
    };
  }

  #packageCard(
    store: PackageManagerStore,
    item: PackageInstallationView,
    language: SupportedLanguage,
  ): HTMLElement {
    const card = document.createElement('article');
    card.className = 'core-card package-card';
    const header = document.createElement('div');
    header.className = 'core-card-heading';
    const title = document.createElement('strong');
    title.textContent = item.packageId;
    const version = document.createElement('span');
    version.textContent = item.version;
    header.append(title, version);
    const state = document.createElement('p');
    state.className = 'core-muted';
    state.textContent = store.statusLabel(item);
    const actions = document.createElement('div');
    actions.className = 'core-actions';

    if (item.configurationRequired && (item.state === 'installed' || item.state === 'disabled')) {
      const configuration = document.createElement('textarea');
      configuration.className = 'package-configuration';
      configuration.rows = 4;
      configuration.spellcheck = false;
      configuration.value = '{}';
      configuration.setAttribute('aria-label', text(language, 'configuration'));
      card.append(header, state, configuration);
      actions.append(actionButton(text(language, 'configure'), async () => {
        await store.configure(item.packageId, item.revision, parseConfiguration(configuration.value));
        await this.#onPackagesChanged();
      }, this.#onFailure));
    } else {
      card.append(header, state);
    }

    if (item.state === 'enabled') {
      actions.append(actionButton(text(language, 'disable'), async () => {
        await store.disable(item.packageId, item.revision);
        await this.#onPackagesChanged();
      }, this.#onFailure));
    } else if (item.state === 'disabled' && !item.configurationRequired) {
      actions.append(actionButton(text(language, 'enable'), async () => {
        await store.enable(item.packageId, item.revision);
        await this.#onPackagesChanged();
      }, this.#onFailure));
    }
    if (item.state !== 'removing' && item.state !== 'removed') {
      const remove = actionButton(text(language, 'remove'), async () => {
        if (globalThis.confirm(text(language, 'confirmRemove'))) {
          await store.remove(item.packageId, item.revision, false);
          await this.#onPackagesChanged();
        }
      }, this.#onFailure);
      remove.classList.add('danger');
      actions.append(remove);
    }

    if (item.faultDetail !== null) {
      const fault = document.createElement('p');
      fault.className = 'core-error-detail';
      fault.textContent = item.faultDetail;
      card.append(fault);
    }
    card.append(actions);
    return card;
  }

  #createAgentSurface(): CoreSurfaceHandle {
    const language = this.#language();
    const store = new AgentDashboardStore();
    const root = section('core-agents');
    const toolbar = coreToolbar(text(language, 'agents'), text(language, 'refresh'));
    const list = document.createElement('div');
    list.className = 'core-list';
    root.append(toolbar.root, list);

    const refresh = async (): Promise<void> => {
      toolbar.button.disabled = true;
      list.replaceChildren(statusText(text(language, 'loading')));
      try {
        const entries = await store.refresh();
        list.replaceChildren();
        if (entries.length === 0) {
          list.append(emptyMessage(text(language, 'noAgents')));
        } else {
          for (const entry of entries) {
            list.append(agentCard(entry, language));
          }
        }
      } catch (error) {
        list.replaceChildren(statusText(errorMessage(error, text(language, 'requestFailed')), 'error'));
        this.#onFailure(error);
      } finally {
        toolbar.button.disabled = false;
      }
    };

    toolbar.button.addEventListener('click', () => void refresh());
    void refresh();
    return { element: root, dispose: () => undefined };
  }

  #createNotificationSurface(problemsOnly: boolean): CoreSurfaceHandle {
    const language = this.#language();
    const root = section(problemsOnly ? 'core-problems' : 'core-notifications');
    const heading = document.createElement('div');
    heading.className = 'core-heading-row';
    const title = document.createElement('h2');
    title.textContent = text(language, problemsOnly ? 'problems' : 'notifications');
    heading.append(title);
    if (!problemsOnly) {
      const acknowledge = document.createElement('button');
      acknowledge.type = 'button';
      acknowledge.className = 'core-secondary-button';
      acknowledge.textContent = text(language, 'markAllRead');
      acknowledge.addEventListener('click', () => this.#notifications.acknowledgeAll());
      heading.append(acknowledge);
    }
    const list = document.createElement('div');
    list.className = 'core-list';
    root.append(heading, list);

    const render = (snapshot: NotificationCenterSnapshot): void => {
      list.replaceChildren();
      if (problemsOnly) {
        const problems = [...snapshot.activeProblems, ...snapshot.resolvedProblems];
        if (problems.length === 0) {
          list.append(emptyMessage(text(language, 'noProblems')));
          return;
        }
        for (const problem of problems) {
          const card = document.createElement('article');
          card.className = 'core-card';
          card.dataset['severity'] = problem.severity;
          const cardTitle = document.createElement('strong');
          cardTitle.textContent = problem.title;
          const detail = document.createElement('p');
          detail.textContent = problem.detail;
          const meta = document.createElement('small');
          meta.className = 'core-muted';
          meta.textContent = `${problem.state} · ${formatDate(problem.lastObservedAtUtc, language)}`;
          card.append(cardTitle, detail, meta);
          list.append(card);
        }
      } else {
        if (snapshot.notifications.length === 0) {
          list.append(emptyMessage(text(language, 'noNotifications')));
          return;
        }
        for (const notification of snapshot.notifications) {
          const card = document.createElement('article');
          card.className = 'core-card notification-card';
          card.dataset['severity'] = notification.severity;
          card.dataset['acknowledged'] = String(notification.acknowledged);
          const cardTitle = document.createElement('strong');
          cardTitle.textContent = notification.title;
          const message = document.createElement('p');
          message.textContent = notification.message;
          const meta = document.createElement('small');
          meta.className = 'core-muted';
          meta.textContent = notification.count > 1
            ? `${formatDate(notification.occurredAtUtc, language)} · ×${notification.count}`
            : formatDate(notification.occurredAtUtc, language);
          card.append(cardTitle, message, meta);
          if (!notification.acknowledged) {
            card.append(actionButton(text(language, 'markRead'), async () => {
              this.#notifications.acknowledge(notification.notificationId);
            }, this.#onFailure));
          }
          list.append(card);
        }
      }
    };

    const unsubscribe = this.#notifications.subscribe(render);
    return { element: root, dispose: unsubscribe };
  }
}

function coreApplication(
  id: string,
  title: string,
  stableKey: string,
  defaultWidth: number,
  defaultHeight: number,
  minimumWidth: number,
  minimumHeight: number,
): DesktopApplication {
  return {
    applicationDefinitionId: id,
    packageId: 'julos.core',
    packageVersion: '1',
    stableKey,
    displayNameKey: title,
    instancePolicy: 'single-instance-per-user',
    defaultWidth,
    defaultHeight,
    minimumWidth,
    minimumHeight,
    viewports: ['desktop', 'tablet', 'mobile'],
    elementName: '',
    frontend: { moduleUrl: '', sha256: '', exportedElements: [] },
  };
}

function section(className: string): HTMLElement {
  const root = document.createElement('section');
  root.className = `core-app ${className}`;
  return root;
}

function coreToolbar(titleText: string, actionText: string): { root: HTMLElement; button: HTMLButtonElement } {
  const root = document.createElement('div');
  root.className = 'core-heading-row';
  const heading = document.createElement('h2');
  heading.textContent = titleText;
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'core-secondary-button';
  button.textContent = actionText;
  root.append(heading, button);
  return { root, button };
}

function selectField(
  title: string,
  options: readonly (readonly [string, string])[],
): { label: HTMLLabelElement; select: HTMLSelectElement } {
  const label = document.createElement('label');
  label.className = 'core-field';
  const caption = document.createElement('span');
  caption.textContent = title;
  const select = document.createElement('select');
  for (const [value, name] of options) {
    const option = document.createElement('option');
    option.value = value;
    option.textContent = name;
    select.append(option);
  }
  label.append(caption, select);
  return { label, select };
}

function inputField(title: string): { label: HTMLLabelElement; input: HTMLInputElement } {
  const label = document.createElement('label');
  label.className = 'core-field';
  const caption = document.createElement('span');
  caption.textContent = title;
  const input = document.createElement('input');
  input.type = 'text';
  label.append(caption, input);
  return { label, input };
}

function fileField(title: string, accept: string | undefined): { label: HTMLLabelElement; input: HTMLInputElement } {
  const field = inputField(title);
  field.input.type = 'file';
  if (accept !== undefined) {
    field.input.accept = accept;
  }
  return field;
}

function actionButton(
  title: string,
  action: () => void | Promise<void>,
  onFailure: (error: unknown) => void,
): HTMLButtonElement {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'core-secondary-button';
  button.textContent = title;
  button.addEventListener('click', () => {
    button.disabled = true;
    void Promise.resolve(action()).catch(onFailure).finally(() => {
      button.disabled = false;
    });
  });
  return button;
}

function agentCard(entry: AgentDashboardEntry, language: SupportedLanguage): HTMLElement {
  const card = document.createElement('article');
  card.className = 'core-card agent-card';
  card.dataset['connectivity'] = entry.connectivity;
  const header = document.createElement('div');
  header.className = 'core-card-heading';
  const name = document.createElement('strong');
  name.textContent = entry.agent.name;
  const state = document.createElement('span');
  state.className = 'connectivity-badge';
  state.textContent = entry.connectivity;
  header.append(name, state);
  const platform = document.createElement('p');
  platform.textContent = `${entry.agent.operatingSystem} · ${entry.agent.architecture} · ${entry.agent.version}`;
  const observed = document.createElement('small');
  observed.className = 'core-muted';
  observed.textContent = entry.observedAtUtc === null
    ? text(language, 'neverSeen')
    : formatDate(entry.observedAtUtc, language);
  card.append(header, platform, observed);
  return card;
}

function parseConfiguration(value: string): Readonly<Record<string, string>> {
  let parsed: unknown;
  try {
    parsed = JSON.parse(value);
  } catch (error) {
    throw new TypeError('Configuration must be a JSON object.', { cause: error });
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    throw new TypeError('Configuration must be a JSON object.');
  }
  const result: Record<string, string> = {};
  for (const [key, item] of Object.entries(parsed)) {
    if (typeof item !== 'string') {
      throw new TypeError(`Configuration value '${key}' must be a string.`);
    }
    result[key] = item;
  }
  return result;
}

function emptyMessage(message: string): HTMLElement {
  const element = document.createElement('p');
  element.className = 'core-empty';
  element.textContent = message;
  return element;
}

function statusText(message: string, state: 'normal' | 'error' = 'normal'): HTMLElement {
  const element = document.createElement('p');
  element.className = state === 'error' ? 'core-status core-status-error' : 'core-status';
  element.textContent = message;
  return element;
}

function formatDate(value: string, language: SupportedLanguage): string {
  const date = new Date(value);
  return Number.isFinite(date.getTime())
    ? new Intl.DateTimeFormat(language, { dateStyle: 'short', timeStyle: 'short' }).format(date)
    : value;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message.trim().length > 0 ? error.message : fallback;
}

function themeValue(value: string): 'system' | 'light' | 'dark' {
  return value === 'dark' ? 'dark' : value === 'light' ? 'light' : 'system';
}

function text(language: SupportedLanguage, key: TextKey): string {
  return messages[language][key];
}

type TextKey = keyof typeof messages.en;

const messages = {
  en: {
    settings: 'Settings', packages: 'Package Manager', agents: 'Agents', notifications: 'Notifications', problems: 'Problems',
    language: 'Language', theme: 'Theme', motion: 'Motion', timeZone: 'Time zone', system: 'System', light: 'Light', dark: 'Dark',
    motionEnabled: 'Enabled', motionReduced: 'Reduced', save: 'Save', saving: 'Saving…', saved: 'Saved', loading: 'Loading…',
    requestFailed: 'Request failed.', refresh: 'Refresh', noPackages: 'No packages are installed.', enable: 'Enable', disable: 'Disable',
    remove: 'Remove', confirmRemove: 'Remove this package? Package data will be kept.', noAgents: 'No agents are enrolled.', neverSeen: 'Never seen',
    markAllRead: 'Mark all read', markRead: 'Mark read', noNotifications: 'No notifications.', noProblems: 'No problems.',
    installPackage: 'Install signed package', packageFile: 'Package (.zip)', signatureFile: 'Signature file', publisherId: 'Publisher ID',
    publisherKeyId: 'Publisher key ID', install: 'Install', installing: 'Installing…', installed: 'Installed', filesRequired: 'Package and signature files are required.',
    configuration: 'Configuration JSON', configure: 'Configure',
  },
  de: {
    settings: 'Einstellungen', packages: 'Paketverwaltung', agents: 'Agents', notifications: 'Benachrichtigungen', problems: 'Probleme',
    language: 'Sprache', theme: 'Design', motion: 'Animationen', timeZone: 'Zeitzone', system: 'System', light: 'Hell', dark: 'Dunkel',
    motionEnabled: 'Aktiviert', motionReduced: 'Reduziert', save: 'Speichern', saving: 'Speichern…', saved: 'Gespeichert', loading: 'Laden…',
    requestFailed: 'Anfrage fehlgeschlagen.', refresh: 'Aktualisieren', noPackages: 'Keine Pakete installiert.', enable: 'Aktivieren', disable: 'Deaktivieren',
    remove: 'Entfernen', confirmRemove: 'Dieses Paket entfernen? Paketdaten bleiben erhalten.', noAgents: 'Keine Agents registriert.', neverSeen: 'Noch nie verbunden',
    markAllRead: 'Alle als gelesen markieren', markRead: 'Als gelesen markieren', noNotifications: 'Keine Benachrichtigungen.', noProblems: 'Keine Probleme.',
    installPackage: 'Signiertes Paket installieren', packageFile: 'Paket (.zip)', signatureFile: 'Signaturdatei', publisherId: 'Publisher-ID',
    publisherKeyId: 'Publisher-Key-ID', install: 'Installieren', installing: 'Installieren…', installed: 'Installiert', filesRequired: 'Paket- und Signaturdatei sind erforderlich.',
    configuration: 'Konfiguration als JSON', configure: 'Konfigurieren',
  },
} as const;
