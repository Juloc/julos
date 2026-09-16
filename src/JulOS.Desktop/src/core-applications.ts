import { AgentDashboardStore, type AgentDashboardEntry } from './agent-dashboard.js';
import {
  ClientDeviceStore,
  preferenceFor,
  preferenceWorkspaceClasses,
  type ClientDeviceSnapshot,
  type ClientDeviceView,
} from './client-devices.js';
import { NotificationCenterStore, type NotificationCenterSnapshot } from './notification-center.js';
import {
  PackageManagerStore,
  type OfficialPackageStoreView,
  type PackageInstallationView,
  type PackageManagerSnapshot,
} from './package-manager.js';
import type { SupportedLanguage } from './localization.js';
import {
  activeOperationStates,
  isCancellable,
  isSettled,
  OperationCenterStore,
  type OperationCenterSnapshot,
  type OperationState,
  type OperationView,
} from './operation-center.js';
import type { DesktopApplication, ShellApiClient, UserProfile } from './shell-api.js';
import { createWebAppBrowserSurface } from './webapp-browser.js';
import {
  isWorkspaceClassOverride,
  type WorkspaceClass,
  type WorkspaceClassOverride,
} from './workspace-contract.js';

export const CoreApplicationIds = {
  settings: 'core.settings',
  packages: 'core.packages',
  agents: 'core.agents',
  notifications: 'core.notifications',
  problems: 'core.problems',
  operations: 'core.operations',
  webappBrowser: 'core.webapp-browser',
} as const;

export interface CoreSurfaceHandle {
  readonly element: HTMLElement;
  readonly dispose: () => void;
}

export interface CoreApplicationCatalogOptions {
  readonly api: ShellApiClient;
  readonly clientDevices: ClientDeviceStore;
  readonly operations: OperationCenterStore;
  readonly notifications: NotificationCenterStore;
  readonly language: () => SupportedLanguage;
  readonly onFailure: (error: unknown) => void;
  readonly onProfileChanged: () => void | Promise<void>;
  readonly onPackagesChanged: () => void | Promise<void>;
}

/** Provides JulOS-owned system utilities as normal windows in the desktop runtime. */
export class CoreApplicationCatalog {
  readonly #api: ShellApiClient;
  readonly #clientDevices: ClientDeviceStore;
  readonly #operations: OperationCenterStore;
  readonly #notifications: NotificationCenterStore;
  readonly #language: () => SupportedLanguage;
  readonly #onFailure: (error: unknown) => void;
  readonly #onProfileChanged: () => void | Promise<void>;
  readonly #onPackagesChanged: () => void | Promise<void>;
  readonly #applicationIds = new Set<string>(Object.values(CoreApplicationIds));

  public constructor(options: CoreApplicationCatalogOptions) {
    this.#api = options.api;
    this.#clientDevices = options.clientDevices;
    this.#operations = options.operations;
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
      coreApplication(CoreApplicationIds.operations, text(language, 'operations'), 'operations', 820, 620, 460, 380),
      coreApplication(CoreApplicationIds.webappBrowser, text(language, 'webappBrowser'), 'webapp-browser', 1024, 720, 480, 360, 'multiple-instances'),
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
      case CoreApplicationIds.operations:
        return this.#createOperationSurface();
      case CoreApplicationIds.webappBrowser:
        return createWebAppBrowserSurface(this.#api);
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
    form.append(languageSelect.label, themeSelect.label, motionSelect.label, timeZoneField.label, save);
    const devices = this.#deviceSection(language);
    root.append(heading, status, form, devices.element);

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
      if (profile === null) return;
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
      }).finally(() => { save.disabled = false; });
    });

    void load();
    return { element: root, dispose: devices.dispose };
  }

  /**
   * The client-device list inside Settings, from `docs/MOBILE_PWA.md` sections 3 and 4.
   *
   * Every control here writes through the owner-scoped API with the revision the rendered
   * record carried, so a change made on another device loses rather than silently wins.
   */
  #deviceSection(language: SupportedLanguage): CoreSurfaceHandle {
    const store = this.#clientDevices;
    const root = document.createElement('div');
    root.className = 'core-device-section';
    const heading = document.createElement('h3');
    heading.textContent = text(language, 'devices');
    const intro = statusText(text(language, 'devicesDescription'));
    const list = document.createElement('div');
    list.className = 'core-list device-list';
    root.append(heading, intro, list);

    const render = (snapshot: ClientDeviceSnapshot): void => {
      list.replaceChildren();
      if (snapshot.lastError !== null) {
        list.append(statusText(snapshot.lastError, 'error'));
      }
      if (snapshot.devices.length === 0) {
        list.append(emptyMessage(text(language, snapshot.loading ? 'loading' : 'noDevices')));
        return;
      }
      for (const device of snapshot.devices) {
        list.append(this.#deviceCard(store, device, language));
      }
    };

    const unsubscribe = store.subscribe(render);
    void store.refresh().catch(this.#onFailure);
    return { element: root, dispose: unsubscribe };
  }

  #deviceCard(
    store: ClientDeviceStore,
    device: ClientDeviceView,
    language: SupportedLanguage,
  ): HTMLElement {
    const card = document.createElement('article');
    card.className = 'core-card device-card';
    card.dataset['currentDevice'] = String(device.isCurrentDevice);

    const header = document.createElement('div');
    header.className = 'core-card-heading';
    const name = document.createElement('strong');
    name.textContent = device.displayName;
    header.append(name);
    if (device.isCurrentDevice) {
      const badge = document.createElement('span');
      badge.className = 'connectivity-badge';
      badge.textContent = text(language, 'thisDevice');
      header.append(badge);
    }

    const identity = document.createElement('form');
    identity.className = 'core-form';
    const nameField = inputField(text(language, 'deviceName'));
    nameField.input.value = device.displayName;
    nameField.input.maxLength = 128;
    nameField.input.required = true;
    const pin = selectField(text(language, 'presentation'), [
      ['', `${text(language, 'presentationAutomatic')} · ${workspaceLabel(device.lastDetectedWorkspaceClass, language)}`],
      ['phone', workspaceLabel('phone', language)],
      ['tablet', workspaceLabel('tablet', language)],
      ['desktop-single', workspaceLabel('desktop-single', language)],
    ]);
    pin.select.value = device.workspaceClassOverride ?? '';
    const save = document.createElement('button');
    save.type = 'submit';
    save.className = 'core-primary-button';
    save.textContent = text(language, 'save');
    identity.append(nameField.label, pin.label, save);
    identity.addEventListener('submit', (event) => {
      event.preventDefault();
      save.disabled = true;
      void store
        .update(device, nameField.input.value.trim(), readWorkspacePin(pin.select.value))
        .catch(this.#onFailure)
        .finally(() => { save.disabled = false; });
    });

    const preferences = document.createElement('div');
    preferences.className = 'core-list device-preferences';
    for (const workspaceClass of preferenceWorkspaceClasses(device)) {
      preferences.append(this.#devicePreference(store, device, workspaceClass, language));
    }

    const seen = document.createElement('small');
    seen.className = 'core-muted';
    seen.textContent = `${text(language, 'lastSeen')} · ${formatDate(device.lastSeenAtUtc, language)}`;

    const remove = actionButton(
      text(language, 'remove'),
      async () => {
        const question = device.isCurrentDevice ? 'confirmRemoveCurrentDevice' : 'confirmRemoveDevice';
        if (globalThis.confirm(text(language, question))) {
          await store.remove(device);
        }
      },
      this.#onFailure,
    );
    remove.classList.add('danger');

    card.append(header, identity, preferences, seen, remove);
    return card;
  }

  /** One workspace class's layout scope and restore mode for a single device. */
  #devicePreference(
    store: ClientDeviceStore,
    device: ClientDeviceView,
    workspaceClass: WorkspaceClass,
    language: SupportedLanguage,
  ): HTMLElement {
    const current = preferenceFor(device, workspaceClass);
    const group = document.createElement('fieldset');
    group.className = 'core-fieldset device-preference';
    group.dataset['workspaceClass'] = workspaceClass;
    const caption = document.createElement('legend');
    caption.textContent = workspaceLabel(workspaceClass, language);

    const scope = selectField(text(language, 'layoutScope'), [
      ['shared', text(language, 'layoutShared')],
      ['device', text(language, 'layoutDevice')],
    ]);
    scope.select.value = current.layoutScope;
    const restore = selectField(text(language, 'restoreMode'), [
      ['resume', text(language, 'restoreResume')],
      ['fresh', text(language, 'restoreFresh')],
    ]);
    restore.select.value = current.restoreMode;

    const apply = (): void => {
      scope.select.disabled = true;
      restore.select.disabled = true;
      void store
        .setPreference(
          device,
          workspaceClass,
          scope.select.value === 'device' ? 'device' : 'shared',
          restore.select.value === 'fresh' ? 'fresh' : 'resume',
        )
        .catch(this.#onFailure)
        .finally(() => {
          scope.select.disabled = false;
          restore.select.disabled = false;
        });
    };

    scope.select.addEventListener('change', apply);
    restore.select.addEventListener('change', apply);
    group.append(caption, scope.label, restore.label);
    return group;
  }

  /**
   * The Operation Center from `docs/MOBILE_PWA.md` section 12.
   *
   * Operations are shown independently of the window that started them: closing or
   * suspending that window cancels nothing, and cancellation is a separate action here.
   */
  #createOperationSurface(): CoreSurfaceHandle {
    const language = this.#language();
    const store = this.#operations;
    const root = section('core-operations');
    const toolbar = coreToolbar(text(language, 'operations'), text(language, 'refresh'));
    const intro = statusText(text(language, 'operationsDescription'));

    const filter = selectField(text(language, 'operationFilter'), [
      ['all', text(language, 'operationFilterAll')],
      ['active', text(language, 'operationFilterActive')],
      ['failed', text(language, 'operationFilterFailed')],
    ]);
    const list = document.createElement('div');
    list.className = 'core-list operation-list';
    const more = document.createElement('button');
    more.type = 'button';
    more.className = 'core-secondary-button';
    more.textContent = text(language, 'operationsMore');
    root.append(toolbar.root, intro, filter.label, list, more);

    const render = (snapshot: OperationCenterSnapshot): void => {
      toolbar.button.disabled = snapshot.loading;
      more.hidden = !snapshot.hasMore;
      more.disabled = snapshot.loading;
      list.replaceChildren();
      if (snapshot.lastError !== null) {
        list.append(statusText(snapshot.lastError, 'error'));
      }
      if (snapshot.operations.length === 0) {
        list.append(emptyMessage(text(language, snapshot.loading ? 'loading' : 'noOperations')));
        return;
      }
      for (const operation of snapshot.operations) {
        list.append(this.#operationCard(store, operation, language));
      }
    };

    const unsubscribe = store.subscribe(render);
    toolbar.button.addEventListener('click', () => void store.refresh().catch(this.#onFailure));
    more.addEventListener('click', () => void store.loadMore().catch(this.#onFailure));
    filter.select.addEventListener('change', () => {
      const states = filter.select.value === 'active'
        ? [...activeOperationStates]
        : filter.select.value === 'failed'
          ? (['failed'] as const).slice()
          : [];
      void store.setFilter({ states, sourcePackageId: null }).catch(this.#onFailure);
    });
    void store.refresh().catch(this.#onFailure);
    return { element: root, dispose: unsubscribe };
  }

  #operationCard(
    store: OperationCenterStore,
    operation: OperationView,
    language: SupportedLanguage,
  ): HTMLElement {
    const card = document.createElement('article');
    card.className = 'core-card operation-card';
    card.dataset['state'] = operation.state;

    const header = document.createElement('div');
    header.className = 'core-card-heading';
    const title = document.createElement('strong');
    title.textContent = operation.operationType;
    const state = document.createElement('span');
    state.className = 'connectivity-badge';
    state.textContent = operationStateLabel(operation.state, language);
    header.append(title, state);

    const target = document.createElement('p');
    target.textContent = operation.targetReference;

    const step = document.createElement('small');
    step.className = 'core-muted';
    step.textContent = operation.currentStep
      ?? `${text(language, 'operationCreated')} · ${formatDate(operation.createdAtUtc, language)}`;

    card.append(header, target, step);

    if (operation.progressPercent !== null) {
      const progress = document.createElement('progress');
      progress.max = 100;
      progress.value = operation.progressPercent;
      card.append(progress);
    }

    if (operation.failureCode !== null) {
      const failure = document.createElement('p');
      failure.className = 'core-error-detail';
      // Only the stable code and the sanitized detail are ever shown; a failure never
      // carries an exception, a stack trace or a credential this far.
      failure.textContent = operation.failureDetail === null
        ? operation.failureCode
        : `${operation.failureCode} · ${operation.failureDetail}`;
      card.append(failure);
    }

    if (operation.cancellationRequested && !isSettled(operation)) {
      const pending = document.createElement('small');
      pending.className = 'core-muted';
      pending.textContent = text(language, 'operationCancelling');
      card.append(pending);
    } else if (isCancellable(operation)) {
      const cancel = actionButton(
        text(language, 'operationCancel'),
        () => store.cancel(operation.operationId),
        this.#onFailure,
      );
      cancel.classList.add('danger');
      card.append(cancel);
    }

    return card;
  }

  #createPackageManagerSurface(): CoreSurfaceHandle {
    const language = this.#language();
    const store = new PackageManagerStore();
    const root = section('core-packages');
    const toolbar = coreToolbar(text(language, 'packageStore'), text(language, 'refresh'));
    const intro = statusText(text(language, 'storeDescription'));
    const catalog = document.createElement('div');
    catalog.className = 'core-list package-store-list';
    const installedHeading = document.createElement('h3');
    installedHeading.textContent = text(language, 'installedPackages');
    const installed = document.createElement('div');
    installed.className = 'core-list';
    const manual = this.#packageInstallForm(store, language);
    root.append(toolbar.root, intro, catalog, installedHeading, installed, manual.element);

    const render = (snapshot: PackageManagerSnapshot): void => {
      toolbar.button.disabled = snapshot.loading;
      manual.setBusy(snapshot.loading);
      catalog.replaceChildren();
      installed.replaceChildren();
      if (snapshot.lastError !== null) catalog.append(statusText(snapshot.lastError, 'error'));
      if (!snapshot.loading && snapshot.catalog.length === 0) {
        catalog.append(emptyMessage(text(language, 'noStorePackages')));
      } else {
        for (const item of snapshot.catalog) catalog.append(this.#officialPackageCard(store, item, language));
      }
      if (!snapshot.loading && snapshot.packages.length === 0) {
        installed.append(emptyMessage(text(language, 'noPackages')));
      } else {
        for (const item of snapshot.packages) installed.append(this.#packageCard(store, item, language));
      }
    };

    const unsubscribe = store.subscribe(render);
    toolbar.button.addEventListener('click', () => void store.refresh().catch(this.#onFailure));
    void store.refresh().catch(this.#onFailure);
    return { element: root, dispose: unsubscribe };
  }

  #officialPackageCard(
    store: PackageManagerStore,
    item: OfficialPackageStoreView,
    language: SupportedLanguage,
  ): HTMLElement {
    const card = document.createElement('article');
    card.className = 'core-card package-card package-store-card';
    const header = document.createElement('div');
    header.className = 'core-card-heading';
    const title = document.createElement('strong');
    title.textContent = language === 'de' ? item.displayNameDe : item.displayNameEn;
    const version = document.createElement('span');
    version.textContent = item.version;
    header.append(title, version);
    const description = document.createElement('p');
    description.textContent = language === 'de' ? item.descriptionDe : item.descriptionEn;
    const state = document.createElement('small');
    state.className = 'core-muted';
    state.textContent = item.installedVersion === null
      ? text(language, 'available')
      : item.updateAvailable
        ? `${text(language, 'updateAvailable')} · ${item.installedVersion} → ${item.version}`
        : `${text(language, 'installed')} · ${item.installedState ?? ''}`;
    card.append(header, description, state);

    if (item.installedVersion === null || item.updateAvailable) {
      const action = actionButton(
        text(language, item.updateAvailable ? 'update' : 'install'),
        async () => {
          await store.installOfficial(item.packageId);
          await this.#onPackagesChanged();
        },
        this.#onFailure,
      );
      action.classList.add('core-primary-button');
      card.append(action);
    }
    return card;
  }

  #packageInstallForm(
    store: PackageManagerStore,
    language: SupportedLanguage,
  ): { element: HTMLElement; setBusy: (busy: boolean) => void } {
    const details = document.createElement('details');
    details.className = 'package-install';
    const summary = document.createElement('summary');
    summary.textContent = text(language, 'advancedInstall');
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
        for (const control of form.querySelectorAll<HTMLInputElement | HTMLButtonElement>('input, button')) control.disabled = busy;
      },
    };
  }

  #packageCard(store: PackageManagerStore, item: PackageInstallationView, language: SupportedLanguage): HTMLElement {
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
        if (entries.length === 0) list.append(emptyMessage(text(language, 'noAgents')));
        else for (const entry of entries) list.append(agentCard(entry, language));
      } catch (error) {
        list.replaceChildren(statusText(errorMessage(error, text(language, 'requestFailed')), 'error'));
        this.#onFailure(error);
      } finally { toolbar.button.disabled = false; }
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
        if (problems.length === 0) { list.append(emptyMessage(text(language, 'noProblems'))); return; }
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
        if (snapshot.notifications.length === 0) { list.append(emptyMessage(text(language, 'noNotifications'))); return; }
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
  instancePolicy: DesktopApplication['instancePolicy'] = 'single-instance-per-user',
): DesktopApplication {
  return {
    applicationDefinitionId: id,
    packageId: 'julos.core',
    packageVersion: '1',
    stableKey,
    displayNameKey: title,
    instancePolicy,
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

function selectField(title: string, options: readonly (readonly [string, string])[]): { label: HTMLLabelElement; select: HTMLSelectElement } {
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
  if (accept !== undefined) field.input.accept = accept;
  return field;
}

function actionButton(title: string, action: () => void | Promise<void>, onFailure: (error: unknown) => void): HTMLButtonElement {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'core-secondary-button';
  button.textContent = title;
  button.addEventListener('click', () => {
    button.disabled = true;
    void Promise.resolve(action()).catch(onFailure).finally(() => { button.disabled = false; });
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
  observed.textContent = entry.observedAtUtc === null ? text(language, 'neverSeen') : formatDate(entry.observedAtUtc, language);
  card.append(header, platform, observed);
  return card;
}

function parseConfiguration(value: string): Readonly<Record<string, string>> {
  let parsed: unknown;
  try { parsed = JSON.parse(value); }
  catch (error) { throw new TypeError('Configuration must be a JSON object.', { cause: error }); }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) throw new TypeError('Configuration must be a JSON object.');
  const result: Record<string, string> = {};
  for (const [key, item] of Object.entries(parsed)) {
    if (typeof item !== 'string') throw new TypeError(`Configuration value '${key}' must be a string.`);
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

/** Reads the workspace pin a select carries; the empty option means automatic. */
function readWorkspacePin(value: string): WorkspaceClassOverride | null {
  return isWorkspaceClassOverride(value) ? value : null;
}

function workspaceLabel(workspaceClass: WorkspaceClass, language: SupportedLanguage): string {
  switch (workspaceClass) {
    case 'phone':
      return text(language, 'workspacePhone');
    case 'tablet':
      return text(language, 'workspaceTablet');
    case 'desktop-multi':
      return text(language, 'workspaceDesktopMulti');
    default:
      return text(language, 'workspaceDesktopSingle');
  }
}

function operationStateLabel(state: OperationState, language: SupportedLanguage): string {
  switch (state) {
    case 'queued':
      return text(language, 'operationQueued');
    case 'running':
      return text(language, 'operationRunning');
    case 'succeeded':
      return text(language, 'operationSucceeded');
    case 'cancelled':
      return text(language, 'operationCancelled');
    default:
      return text(language, 'operationFailed');
  }
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
    settings: 'Settings', packages: 'Package Manager', packageStore: 'JulOS Store', agents: 'Agents', notifications: 'Notifications', problems: 'Problems', webappBrowser: 'Browser',
    language: 'Language', theme: 'Theme', motion: 'Motion', timeZone: 'Time zone', system: 'System', light: 'Light', dark: 'Dark',
    motionEnabled: 'Enabled', motionReduced: 'Reduced', save: 'Save', saving: 'Saving…', saved: 'Saved', loading: 'Loading…',
    requestFailed: 'Request failed.', refresh: 'Refresh', storeDescription: 'Official JulOS packages. Signatures, configuration and activation are handled automatically.',
    noStorePackages: 'No official packages are included in this JulOS build.', installedPackages: 'Installed packages', noPackages: 'No packages are installed.',
    available: 'Available', updateAvailable: 'Update available', update: 'Update', enable: 'Enable', disable: 'Disable', remove: 'Remove',
    confirmRemove: 'Remove this package? Package data will be kept.', noAgents: 'No agents are enrolled.', neverSeen: 'Never seen',
    markAllRead: 'Mark all read', markRead: 'Mark read', noNotifications: 'No notifications.', noProblems: 'No problems.',
    advancedInstall: 'Advanced · install external signed package', packageFile: 'Package (.zip)', signatureFile: 'Signature file', publisherId: 'Publisher ID',
    publisherKeyId: 'Publisher key ID', install: 'Install', installing: 'Installing…', installed: 'Installed', filesRequired: 'Package and signature files are required.',
    configuration: 'Configuration JSON', configure: 'Configure',
    devices: 'Devices', thisDevice: 'This device', deviceName: 'Device name', noDevices: 'No devices are registered.',
    devicesDescription: 'Browsers and installed apps you have opened JulOS in. A device stores layout preferences only; it never grants access to your account.',
    presentation: 'Presentation', presentationAutomatic: 'Automatic', lastSeen: 'Last seen',
    workspacePhone: 'Phone', workspaceTablet: 'Tablet', workspaceDesktopSingle: 'Desktop', workspaceDesktopMulti: 'Multiple displays',
    layoutScope: 'Window layout', layoutShared: 'Shared with my other devices', layoutDevice: 'Only on this device',
    restoreMode: 'When JulOS opens', restoreResume: 'Restore my windows', restoreFresh: 'Start with an empty desktop',
    confirmRemoveDevice: 'Remove this device? Layouts and preferences stored only for it are deleted. Shared layouts are kept.',
    operations: 'Operations', operationsDescription: 'Background work JulOS is doing for you. Closing a window never cancels it.',
    noOperations: 'No operations.', operationsMore: 'Show older', operationFilter: 'Show',
    operationFilterAll: 'Everything', operationFilterActive: 'Still running', operationFilterFailed: 'Failed',
    operationQueued: 'Queued', operationRunning: 'Running', operationSucceeded: 'Succeeded',
    operationFailed: 'Failed', operationCancelled: 'Cancelled', operationCancel: 'Cancel',
    operationCancelling: 'Cancellation requested; waiting for the work to stop.', operationCreated: 'Created',
    confirmRemoveCurrentDevice: 'Remove the device you are using? It is registered again straight away as a new device, so layouts and preferences stored only for it are lost.',
  },
  de: {
    settings: 'Einstellungen', packages: 'Paketverwaltung', packageStore: 'JulOS Store', agents: 'Agents', notifications: 'Benachrichtigungen', problems: 'Probleme', webappBrowser: 'Browser',
    language: 'Sprache', theme: 'Design', motion: 'Animationen', timeZone: 'Zeitzone', system: 'System', light: 'Hell', dark: 'Dunkel',
    motionEnabled: 'Aktiviert', motionReduced: 'Reduziert', save: 'Speichern', saving: 'Speichern…', saved: 'Gespeichert', loading: 'Laden…',
    requestFailed: 'Anfrage fehlgeschlagen.', refresh: 'Aktualisieren', storeDescription: 'Offizielle JulOS-Pakete. Signatur, Konfiguration und Aktivierung erledigt JulOS automatisch.',
    noStorePackages: 'Dieser JulOS-Build enthält keine offiziellen Pakete.', installedPackages: 'Installierte Pakete', noPackages: 'Keine Pakete installiert.',
    available: 'Verfügbar', updateAvailable: 'Update verfügbar', update: 'Aktualisieren', enable: 'Aktivieren', disable: 'Deaktivieren', remove: 'Entfernen',
    confirmRemove: 'Dieses Paket entfernen? Paketdaten bleiben erhalten.', noAgents: 'Keine Agents registriert.', neverSeen: 'Noch nie verbunden',
    markAllRead: 'Alle als gelesen markieren', markRead: 'Als gelesen markieren', noNotifications: 'Keine Benachrichtigungen.', noProblems: 'Keine Probleme.',
    advancedInstall: 'Erweitert · externes signiertes Paket installieren', packageFile: 'Paket (.zip)', signatureFile: 'Signaturdatei', publisherId: 'Publisher-ID',
    publisherKeyId: 'Publisher-Key-ID', install: 'Installieren', installing: 'Installieren…', installed: 'Installiert', filesRequired: 'Paket- und Signaturdatei sind erforderlich.',
    configuration: 'Konfiguration als JSON', configure: 'Konfigurieren',
    devices: 'Geräte', thisDevice: 'Dieses Gerät', deviceName: 'Gerätename', noDevices: 'Keine Geräte registriert.',
    devicesDescription: 'Browser und installierte Apps, in denen du JulOS geöffnet hast. Ein Gerät speichert nur Layout-Einstellungen und gewährt nie Zugriff auf dein Konto.',
    presentation: 'Darstellung', presentationAutomatic: 'Automatisch', lastSeen: 'Zuletzt gesehen',
    workspacePhone: 'Telefon', workspaceTablet: 'Tablet', workspaceDesktopSingle: 'Desktop', workspaceDesktopMulti: 'Mehrere Bildschirme',
    layoutScope: 'Fensterlayout', layoutShared: 'Mit meinen anderen Geräten geteilt', layoutDevice: 'Nur auf diesem Gerät',
    restoreMode: 'Beim Öffnen von JulOS', restoreResume: 'Meine Fenster wiederherstellen', restoreFresh: 'Mit leerem Desktop starten',
    confirmRemoveDevice: 'Dieses Gerät entfernen? Layouts und Einstellungen, die nur dafür gespeichert sind, werden gelöscht. Geteilte Layouts bleiben erhalten.',
    operations: 'Vorgänge', operationsDescription: 'Hintergrundarbeit, die JulOS für dich erledigt. Ein Fenster zu schließen bricht sie nie ab.',
    noOperations: 'Keine Vorgänge.', operationsMore: 'Ältere anzeigen', operationFilter: 'Anzeigen',
    operationFilterAll: 'Alles', operationFilterActive: 'Läuft noch', operationFilterFailed: 'Fehlgeschlagen',
    operationQueued: 'In Warteschlange', operationRunning: 'Läuft', operationSucceeded: 'Erfolgreich',
    operationFailed: 'Fehlgeschlagen', operationCancelled: 'Abgebrochen', operationCancel: 'Abbrechen',
    operationCancelling: 'Abbruch angefordert; warte, bis die Arbeit stoppt.', operationCreated: 'Erstellt',
    confirmRemoveCurrentDevice: 'Das Gerät entfernen, das du gerade benutzt? Es wird sofort als neues Gerät registriert; Layouts und Einstellungen, die nur dafür gespeichert sind, gehen verloren.',
  },
} as const;
