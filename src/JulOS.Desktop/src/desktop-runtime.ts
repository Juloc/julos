import { JulOsApiError } from './api-client.js';
import {
  ClientDeviceStore,
  defaultDeviceName,
  detectWorkspaceClass,
  effectiveWorkspaceClass,
  windowCapabilitySource,
} from './client-devices.js';
import { CoreApplicationCatalog, CoreApplicationIds } from './core-applications.js';
import { desktopNotificationCenter } from './desktop-observability.js';
import { classifyHomeIndicatorGesture } from './home-gesture.js';
import { LauncherIndex, type LauncherSearchResult } from './launcher-index.js';
import {
  DesktopLayoutPersistence,
  windowsForPersistence,
  type LayoutPresentation,
  type WorkspaceLayoutResponse,
  type PersistedDesktopWindow,
  type PersistedWidgetPlacement,
} from './layout-persistence.js';
import type { NotificationCenterSnapshot, NotificationCenterStore } from './notification-center.js';
import { PackageCapabilityClient } from './package-capability-client.js';
import { PackageFrontendHost } from './package-frontend-host.js';
import { translate, type SupportedLanguage } from './localization.js';
import { classifyViewport, type DesktopViewport } from './responsive-desktop.js';
import { ExecutionPreferenceClient, type ExecutionPreference } from './execution-preferences.js';
import {
  createIsolatedFrame,
  readIsolatedRequest,
  type IsolatedGrants,
} from './isolated-frontend.js';
import { OperationCenterStore } from './operation-center.js';
import { PhoneForegroundController } from './phone-foreground.js';
import { SurfaceActivityMonitor } from './surface-activity.js';
import { readSurfaceHost, SurfaceScheduler } from './surface-scheduler.js';
import type { WorkspaceClass } from './workspace-contract.js';
import {
  clampSplitRatioPermille,
  deriveWorkspaceStage,
  type WorkspaceStage,
} from './workspace-stage.js';
import { ShellKeyboardController } from './shell-keyboard.js';
import type { DesktopApplication, DesktopWidget, ShellApiClient } from './shell-api.js';
import { isDynamicWebAppBrowserAvailable } from './webapp-availability.js';
import { WidgetHostStore } from './widget-host.js';
import { WindowInteractionController, type ResizeEdge } from './window-interactions.js';
import { WindowSnapController } from './window-snapping.js';

import {
  AltTabWindowSwitcher,
  TaskbarWindowModel,
  WindowLaunchCoordinator,
  type WindowSwitcherSnapshot,
} from './window-taskbar.js';
import {
  WindowStore,
  type DesktopWindowSnapshot,
  type FixedWindowState,
  type UsableArea,
  type WindowBounds,
} from './window-store.js';

const RemovedBrowserPackageIds = new Set([
  'de.juloc.julos.browser',
  'de.juloc.julos.adaptive-browser',
]);

export interface DesktopRuntimeElements {
  readonly windowLayer: HTMLElement;
  readonly launcherEntries: HTMLElement;
  readonly runningApplications: HTMLElement;
  readonly emptyState: HTMLElement;
  readonly snapPreview: HTMLElement;
}

export interface DesktopRuntimeOptions {
  readonly api: ShellApiClient;
  readonly elements: DesktopRuntimeElements;
  readonly notifications?: NotificationCenterStore;
  readonly language: () => SupportedLanguage;
  readonly onFailure: (error: unknown) => void;
  readonly onProfileChanged?: () => void | Promise<void>;
}

/** Composes the existing launcher, package frontend, persistence, widget and window controllers. */
export class DesktopRuntime {
  readonly #api: ShellApiClient;
  readonly #elements: DesktopRuntimeElements;
  readonly #notifications: NotificationCenterStore;
  readonly #language: () => SupportedLanguage;
  readonly #onFailure: (error: unknown) => void;
  readonly #store = new WindowStore();
  readonly #launcherCoordinator = new WindowLaunchCoordinator(this.#store);
  readonly #taskbar = new TaskbarWindowModel(this.#store);
  readonly #windowSwitcher = new AltTabWindowSwitcher(this.#store);
  readonly #interactions = new WindowInteractionController(this.#store);
  readonly #snap = new WindowSnapController(this.#store);
  readonly #frontendHost = new PackageFrontendHost();
  readonly #capabilities = new PackageCapabilityClient();
  readonly #widgetHost = new WidgetHostStore();
  readonly #clientDevices = new ClientDeviceStore();
  readonly #phoneForeground = new PhoneForegroundController();
  readonly #preferences = new ExecutionPreferenceClient();
  readonly #operations = new OperationCenterStore();
  readonly #packageElements = new Map<string, HTMLElement>();
  readonly #isolatedListeners = new Map<string, () => void>();
  readonly #layoutPersistence: DesktopLayoutPersistence;
  readonly #coreApplications: CoreApplicationCatalog;
  readonly #keyboard: ShellKeyboardController;
  readonly #applications = new Map<string, DesktopApplication>();
  readonly #widgets = new Map<string, DesktopWidget>();
  readonly #windowElements = new Map<string, HTMLElement>();
  readonly #windowSurfaces = new Map<string, HTMLElement>();
  readonly #coreSurfaceDisposers = new Map<string, () => void>();
  readonly #registeredWidgetIds = new Map<string, string>();
  readonly #keyDownHandler = (event: KeyboardEvent): void => this.#handleKeyDown(event);
  readonly #keyUpHandler = (event: KeyboardEvent): void => { this.#keyboard.handleKeyUp(event); };
  readonly #resizeHandler = (): void => this.#renderWindows(this.#store.windows);
  #widgetLayer: HTMLElement | null = null;
  #switcherLayer: HTMLElement | null = null;
  #widgetPlacements: readonly PersistedWidgetPlacement[] = [];
  #launcher: LauncherIndex | null = null;
  #launcherQuery = '';
  #viewport: DesktopViewport = 'desktop';
  // Layout identity, which is separate from the viewport the applications are listed
  // for: several workspace classes present the same application viewport class.
  #workspaceClass: WorkspaceClass = 'desktop-single';
  #webAppBrowserAvailable = false;
  #layoutLoaded = false;
  #restoringLayout = false;
  #unsubscribeWindows: (() => void) | null = null;
  #unsubscribeSnap: (() => void) | null = null;
  #unsubscribeObservability: (() => void) | null = null;
  #unbindCoreShellActions: (() => void) | null = null;
  #unbindHomeIndicator: (() => void) | null = null;
  #dockRevealTimer: ReturnType<typeof globalThis.setTimeout> | null = null;
  #homeGestureStart: { readonly x: number; readonly y: number } | null = null;
  readonly #surfaces: SurfaceScheduler;
  readonly #activity: SurfaceActivityMonitor;
  #dividerElement: HTMLElement | null = null;
  #dividerDrag: number | null = null;

  public constructor(options: DesktopRuntimeOptions) {
    this.#api = options.api;
    this.#elements = options.elements;
    this.#notifications = options.notifications ?? desktopNotificationCenter;
    this.#language = options.language;
    this.#onFailure = options.onFailure;
    this.#surfaces = new SurfaceScheduler({
      onFailure: (failure) => options.onFailure(
        new Error(`Surface ${failure.call ?? 'transition'} failed: ${failure.code}`, { cause: failure.cause }),
      ),
    });
    this.#activity = new SurfaceActivityMonitor({
      onViolation: (violation) => options.onFailure(
        new Error(
          `A suspended Surface kept rendering (${violation.mutations} changes): ${violation.code}`,
        ),
      ),
    });
    this.#layoutPersistence = new DesktopLayoutPersistence(
      globalThis.fetch.bind(globalThis),
      { onFailure: (error) => this.#onFailure(error) },
    );
    this.#coreApplications = new CoreApplicationCatalog({
      api: options.api,
      clientDevices: this.#clientDevices,
      operations: this.#operations,
      notifications: this.#notifications,
      language: options.language,
      onFailure: options.onFailure,
      onProfileChanged: options.onProfileChanged ?? (() => undefined),
      onPackagesChanged: () => this.#refreshPackageCatalog(),
    });
    this.#keyboard = new ShellKeyboardController({
      openLauncher: () => this.#openLauncher(),
      openCommandPalette: () => this.#openLauncher(),
      openNotifications: () => this.openApplication(CoreApplicationIds.notifications),
      openProblems: () => this.openApplication(CoreApplicationIds.problems),
      beginWindowSwitcher: () => this.#renderWindowSwitcher(this.#windowSwitcher.begin()),
      nextWindow: () => this.#renderWindowSwitcher(this.#windowSwitcher.next()),
      previousWindow: () => this.#renderWindowSwitcher(this.#windowSwitcher.previous()),
      commitWindowSwitcher: () => {
        this.#windowSwitcher.commit(this.#usableArea());
        this.#hideWindowSwitcher();
        this.#scheduleLayout();
      },
      cancelWindowSwitcher: () => {
        this.#windowSwitcher.cancel();
        this.#hideWindowSwitcher();
      },
      closeActiveWindow: () => this.#closeActiveWindow(),
      restoreFocus: () => this.#focusActiveWindow(),
    });
  }

  public async start(): Promise<void> {
    if (this.#unsubscribeWindows !== null) {
      return;
    }

    this.#ensureStyles();
    this.#viewport = classifyViewport(Math.max(this.#elements.windowLayer.clientWidth, 320));

    // The device is resolved before the layout is loaded, because its stored pin and
    // preference decide which workspace class and which of the two stored layouts answer.
    await this.#registerClientDevice();
    const [[packageApplications, widgets], webAppBrowserAvailable] = await Promise.all([
      this.#readPackageCatalog(),
      this.#readWebAppBrowserAvailability(),
    ]);
    this.#webAppBrowserAvailable = webAppBrowserAvailable;
    this.#replaceCatalog(packageApplications, widgets);

    let layout: WorkspaceLayoutResponse | null = null;
    try {
      layout = await this.#layoutPersistence.load(this.#workspaceClass);
      this.#layoutLoaded = true;
    } catch (error) {
      this.#onFailure(error);
    }

    if (layout !== null) {
      this.#phoneForeground.restore({
        primaryWindowId: layout.layout.primaryWindowId,
        secondaryWindowId: layout.layout.secondaryWindowId,
        splitRatioPermille: layout.layout.splitRatioPermille,
      });
      this.#widgetPlacements = layout.layout.widgets.map((placement) => ({ ...placement }));
      this.#restoringLayout = true;
      try {
        this.#restoreLayout(layout.layout.windows);
      } finally {
        this.#restoringLayout = false;
      }
    }

    this.#renderLauncher(this.#launcherQuery);
    this.#applyShortcutLabels();
    this.#unbindCoreShellActions = this.#bindCoreShellActions();
    this.#unsubscribeObservability = this.#notifications.subscribe((snapshot) => this.#renderStatus(snapshot));
    this.#unsubscribeWindows = this.#store.subscribe((windows) => this.#renderWindows(windows));
    this.#unsubscribeSnap = this.#snap.subscribe((preview) => {
      const element = this.#elements.snapPreview;
      if (preview === null) {
        element.hidden = true;
        return;
      }
      element.hidden = false;
      applyBounds(element, preview.bounds);
    });
    globalThis.addEventListener('keydown', this.#keyDownHandler, true);
    globalThis.addEventListener('keyup', this.#keyUpHandler, true);
    globalThis.addEventListener('resize', this.#resizeHandler);
    this.#bindHomeIndicator();

    await Promise.all([this.#loadRestoredFrontends(), this.#renderWidgets()]);
  }

  /**
   * Registers this browser as a client device, or resolves the one its cookie names.
   *
   * The desktop is fully usable without a device record — the record only carries layout
   * preferences — so a failure here is reported and does not stop the runtime.
   */
  async #registerClientDevice(): Promise<void> {
    // Nothing in here may prevent the desktop from starting. Classification reads browser
    // capabilities, which not every browser reports, and a device record only carries
    // layout preferences: without one the desktop still works, on the default class.
    try {
      const detected = detectWorkspaceClass(windowCapabilitySource(globalThis.window));
      this.#workspaceClass = detected;
      await this.#clientDevices.register(defaultDeviceName(detected, this.#language()), detected);
      const current = this.#clientDevices.currentDevice();
      if (current !== null) {
        // A stored pin is authoritative over detection.
        this.#workspaceClass = effectiveWorkspaceClass(current);
      }
    } catch (error) {
      this.#onFailure(error);
    }
  }

  public stop(): void {
    this.#unsubscribeWindows?.();
    this.#unsubscribeSnap?.();
    this.#unsubscribeObservability?.();
    this.#unbindCoreShellActions?.();
    this.#unsubscribeWindows = null;
    this.#unsubscribeSnap = null;
    this.#unsubscribeObservability = null;
    this.#unbindCoreShellActions = null;
    globalThis.removeEventListener('keydown', this.#keyDownHandler, true);
    globalThis.removeEventListener('keyup', this.#keyUpHandler, true);
    globalThis.removeEventListener('resize', this.#resizeHandler);
    this.#unbindHomeIndicator?.();
    this.#unbindHomeIndicator = null;
    this.#hideDock();
    this.#windowSwitcher.cancel();
    this.#hideWindowSwitcher();

    if (this.#layoutLoaded) {
      void this.#layoutPersistence.flush(this.#workspaceClass)
        .catch((error: unknown) => this.#onFailure(error))
        .finally(() => this.#layoutPersistence.dispose());
    } else {
      this.#layoutPersistence.dispose();
    }

    for (const dispose of this.#coreSurfaceDisposers.values()) {
      dispose();
    }
    this.#coreSurfaceDisposers.clear();
    this.#clearRenderedWidgets();
    this.#store.clear();
    this.#applications.clear();
    this.#widgets.clear();
    this.#windowElements.clear();
    this.#windowSurfaces.clear();
    this.#widgetPlacements = [];
    this.#widgetLayer = null;
    this.#switcherLayer = null;
    this.#elements.windowLayer.replaceChildren(this.#elements.snapPreview);
    this.#elements.runningApplications.replaceChildren();
    this.#elements.launcherEntries.replaceChildren();
    this.#launcher = null;
    this.#launcherQuery = '';
  }

  public search(query: string): void {
    this.#launcherQuery = query;
    this.#renderLauncher(query);
  }

  /**
   * Routes one realtime event to the state it belongs to.
   *
   * The event carries identity and revision only; whatever consumes it refetches the
   * authoritative record rather than trusting the notification.
   */
  public async applyRealtimeEvent(eventType: string, resourceId: string, revision: number | null): Promise<void> {
    if (eventType !== 'operation.changed') {
      return;
    }

    try {
      await this.#operations.applyChange(resourceId, revision);
    } catch (error) {
      this.#onFailure(error);
    }
  }

  public openApplication(applicationId: string, targetId?: string): void {
    const launcher = this.#launcher;
    if (launcher === null) {
      return;
    }
    const result = launcher.search('').find((entry) => targetId === undefined
      ? entry.kind === 'application' && entry.applicationId === applicationId
      : entry.kind === 'target' && entry.applicationId === applicationId && entry.targetId === targetId);
    if (result !== undefined) {
      void this.#launch(result);
    }
  }

  async #readPackageCatalog(): Promise<readonly [readonly DesktopApplication[], readonly DesktopWidget[]]> {
    const [applications, widgets] = await Promise.all([
      this.#api.readApplications(this.#viewport),
      this.#viewport === 'mobile'
        ? Promise.resolve([] as readonly DesktopWidget[])
        : this.#api.readWidgets(),
    ]);
    return [applications, widgets];
  }

  async #readWebAppBrowserAvailability(): Promise<boolean> {
    try {
      return isDynamicWebAppBrowserAvailable(await this.#api.readWebProxyConfig());
    } catch (error) {
      if (error instanceof JulOsApiError && error.kind === 'forbidden') {
        return false;
      }
      this.#onFailure(error);
      return false;
    }
  }

  #replaceCatalog(
    packageApplications: readonly DesktopApplication[],
    widgets: readonly DesktopWidget[],
  ): void {
    const coreApplications = this.#coreApplications.applications()
      .filter((application) => this.#webAppBrowserAvailable
        || application.applicationDefinitionId !== CoreApplicationIds.webappBrowser);
    const visiblePackageApplications = packageApplications.filter(
      (application) => !RemovedBrowserPackageIds.has(application.packageId),
    );
    const applications = [
      ...coreApplications,
      ...visiblePackageApplications,
    ];
    this.#applications.clear();
    this.#widgets.clear();
    for (const application of applications) {
      this.#applications.set(application.applicationDefinitionId, application);
    }
    for (const widget of widgets) {
      this.#widgets.set(widget.widgetKey, widget);
    }
    this.#launcher = this.#createLauncher(applications);
  }

  #createLauncher(applications: readonly DesktopApplication[]): LauncherIndex {
    return new LauncherIndex({
      applications: applications.map((application) => ({
        applicationId: application.applicationDefinitionId,
        title: applicationTitle(application),
        description: application.packageId === 'julos.core' ? '' : application.packageId,
        keywords: [application.stableKey, application.packageId],
        instancePolicy: application.instancePolicy,
        defaultBounds: defaultBounds(application, this.#usableArea()),
        requiredPermissions: [],
      })),
      targets: applications.flatMap((application) => (application.launchTargets ?? []).map((target) => ({
        targetId: target.launchTargetId,
        applicationId: application.applicationDefinitionId,
        title: target.displayName,
        description: target.externalIdentity,
        keywords: [application.stableKey, application.packageId, target.externalIdentity],
        state: 'approved' as const,
        requiredPermissions: [],
      }))),
      commands: [],
    }, []);
  }

  async #refreshPackageCatalog(): Promise<void> {
    const [webAppBrowserAvailable, [packageApplications, widgets]] = await Promise.all([
      this.#readWebAppBrowserAvailability(),
      this.#readPackageCatalog(),
    ]);
    this.#webAppBrowserAvailable = webAppBrowserAvailable;
    this.#replaceCatalog(packageApplications, widgets);

    const availableApplications = new Set(this.#applications.keys());
    for (const window of [...this.#store.windows]) {
      if (!availableApplications.has(window.applicationId)) {
        this.#closeWindow(window.id);
      }
    }

    this.#renderLauncher(this.#launcherQuery);
    await this.#renderWidgets();
    this.#scheduleLayout();
  }

  #renderLauncher(query: string): void {
    const launcher = this.#launcher;
    const container = this.#elements.launcherEntries;
    container.replaceChildren();
    if (launcher === null) {
      return;
    }

    for (const result of launcher.search(query)) {
      if ((result.kind !== 'application' && result.kind !== 'target') || result.applicationId === CoreApplicationIds.settings) {
        continue;
      }
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'launcher-entry application-entry';
      button.dataset['applicationId'] = result.applicationId ?? result.id;
      if (result.targetId !== null) {
        button.dataset['launchTargetId'] = result.targetId;
      }
      const mark = document.createElement('span');
      mark.className = 'application-glyph';
      mark.textContent = result.title.slice(0, 1).toLocaleUpperCase();
      const text = document.createElement('span');
      text.className = 'application-entry-text';
      const title = document.createElement('strong');
      title.textContent = result.title;
      text.append(title);
      if (result.description.trim().length > 0) {
        const description = document.createElement('small');
        description.textContent = result.description;
        text.append(description);
      }
      button.append(mark, text);
      button.addEventListener('click', () => void this.#launch(result));
      container.append(button);
    }
  }

  async #launch(result: LauncherSearchResult): Promise<void> {
    const launcher = this.#launcher;
    if (launcher === null) {
      return;
    }

    let launchedWindowId: string | null = null;
    try {
      const launch = launcher.launch(result, this.#launcherCoordinator, this.#usableArea());
      launchedWindowId = launch.window.id;
      const application = this.#applications.get(launch.window.applicationId);
      if (application === undefined) {
        throw new Error(`Application '${launch.window.applicationId}' is not available.`);
      }
      if (!this.#coreApplications.isCoreApplication(application.applicationDefinitionId)) {
        await this.#loadFrontend(application);
      }
      this.#closeLauncher();
      this.#phoneForeground.show(launch.window.id);
      this.#renderWindows(this.#store.windows);
      this.#focusActiveWindow();
      this.#scheduleLayout();
    } catch (error) {
      if (launchedWindowId !== null && this.#store.windows.some((window) => window.id === launchedWindowId)) {
        this.#closeWindow(launchedWindowId);
      }
      this.#onFailure(error);
    }
  }

  async #loadFrontend(application: DesktopApplication): Promise<void> {
    await this.#loadPackageFrontend(
      application.packageId,
      application.packageVersion,
      application.frontend,
    );
  }

  async #loadPackageFrontend(
    packageId: string,
    packageVersion: string,
    frontend: DesktopApplication['frontend'],
  ): Promise<void> {
    await this.#frontendHost.load({
      packageId,
      version: packageVersion,
      moduleUrl: frontend.moduleUrl,
      sha256: frontend.sha256,
      exportedElements: frontend.exportedElements,
    }, {
      packageId,
      language: this.#language(),
      theme: resolvedTheme(),
      invokeCapability: (name, operation, payload) =>
        this.#capabilities.invoke(packageId, name, operation, payload),
      openApplication: (applicationId, targetId) => this.openApplication(applicationId, targetId),
      saveLaunchTarget: async (applicationStableKey, externalIdentity, displayName) => {
        const target = await this.#api.saveLaunchTarget(
          packageId,
          applicationStableKey,
          externalIdentity,
          displayName,
        );
        await this.#refreshPackageCatalog();
        return {
          launchTargetId: target.launchTargetId,
          externalIdentity: target.externalIdentity,
          displayName: target.displayName,
        };
      },
      deleteLaunchTarget: async (launchTargetId) => {
        await this.#api.deleteLaunchTarget(packageId, launchTargetId);
        await this.#refreshPackageCatalog();
      },
    });
  }

  async #loadRestoredFrontends(): Promise<void> {
    const applicationIds = new Set(this.#store.windows.map((window) => window.applicationId));
    for (const applicationId of applicationIds) {
      if (this.#coreApplications.isCoreApplication(applicationId)) {
        continue;
      }
      const application = this.#applications.get(applicationId);
      if (application === undefined) {
        continue;
      }
      try {
        await this.#loadFrontend(application);
      } catch (error) {
        this.#onFailure(error);
      }
    }
    this.#renderWindows(this.#store.windows);
  }

  async #renderWidgets(): Promise<void> {
    this.#clearRenderedWidgets();
    const layer = this.#ensureWidgetLayer();
    if (this.#viewport === 'mobile') {
      return;
    }

    for (const placement of this.#widgetPlacements) {
      const widget = this.#widgets.get(placement.widgetKey);
      if (widget === undefined) {
        continue;
      }

      try {
        await this.#loadPackageFrontend(widget.packageId, widget.packageVersion, widget.frontend);
        if (!customElements.get(widget.elementName)) {
          throw new Error(`Widget element '${widget.elementName}' was not registered.`);
        }
        this.#widgetHost.register({
          widgetId: placement.widgetPlacementId,
          packageId: widget.packageId,
          size: widget.defaultSize,
        });
        this.#registeredWidgetIds.set(placement.widgetPlacementId, widget.packageId);

        const frame = document.createElement('article');
        frame.className = 'desktop-widget';
        frame.dataset['widgetKey'] = widget.widgetKey;
        frame.dataset['size'] = widget.defaultSize;
        applyWidgetPlacement(frame, placement);
        const surface = this.#frontendHost.createHostElement(widget.elementName);
        frame.append(surface.host);
        layer.append(frame);
      } catch (error) {
        this.#onFailure(error);
      }
    }
  }

  #clearRenderedWidgets(): void {
    for (const [widgetId, packageId] of this.#registeredWidgetIds) {
      this.#widgetHost.remove(packageId, widgetId);
    }
    this.#registeredWidgetIds.clear();
    this.#widgetLayer?.replaceChildren();
  }

  #ensureWidgetLayer(): HTMLElement {
    if (this.#widgetLayer !== null) {
      return this.#widgetLayer;
    }
    const layer = document.createElement('section');
    layer.className = 'desktop-widget-layer';
    layer.setAttribute('aria-label', 'Desktop widgets');
    this.#elements.windowLayer.prepend(layer);
    this.#widgetLayer = layer;
    return layer;
  }

  #restoreLayout(windows: readonly PersistedDesktopWindow[]): void {
    const area = this.#usableArea();
    for (const persisted of [...windows].sort((left, right) => left.zIndex - right.zIndex)) {
      const application = this.#applications.get(persisted.applicationDefinitionId);
      if (application === undefined || this.#coreApplications.isCoreApplication(persisted.applicationDefinitionId)) {
        continue;
      }

      const restoreBounds = clampBounds({
        x: persisted.restoreX,
        y: persisted.restoreY,
        width: persisted.restoreWidth,
        height: persisted.restoreHeight,
      }, area, application.minimumWidth, application.minimumHeight);
      const normalBounds = persisted.state === 'normal'
        ? clampBounds({
            x: persisted.x,
            y: persisted.y,
            width: persisted.width,
            height: persisted.height,
          }, area, application.minimumWidth, application.minimumHeight)
        : restoreBounds;
      const launchTarget = persisted.launchTargetId === null
        ? undefined
        : (application.launchTargets ?? []).find((target) => target.launchTargetId === persisted.launchTargetId);

      this.#store.open({
        id: persisted.windowId,
        applicationId: persisted.applicationDefinitionId,
        launchTargetId: persisted.launchTargetId,
        title: launchTarget?.displayName ?? applicationTitle(application),
        bounds: normalBounds,
      });

      if (isFixedState(persisted.state)) {
        this.#store.applyFixedState(persisted.windowId, persisted.state, area);
      } else if (persisted.state === 'minimized') {
        this.#store.minimize(persisted.windowId);
      }
    }
  }

  #renderWindows(windows: readonly DesktopWindowSnapshot[]): void {
    const area = this.#usableArea();
    const stage = deriveWorkspaceStage({
      workspaceClass: this.#workspaceClass,
      windows,
      area,
      activeWindowId: this.#store.frontWindow?.id ?? null,
      phone: this.#phoneForeground.state(),
      freeWindowPlacement: this.#allowsFreeWindowPlacement(area),
    });
    const placements = new Map(stage.placements.map((placement) => [placement.windowId, placement]));
    const openIds = new Set(windows.map((window) => window.id));

    for (const [windowId, element] of this.#windowElements) {
      if (!openIds.has(windowId)) {
        element.remove();
        this.#windowElements.delete(windowId);
        this.#windowSurfaces.delete(windowId);
        this.#coreSurfaceDisposers.get(windowId)?.();
        this.#coreSurfaceDisposers.delete(windowId);
        this.#activity.release(windowId);
        this.#packageElements.delete(windowId);
        this.#isolatedListeners.get(windowId)?.();
        this.#isolatedListeners.delete(windowId);
        void this.#surfaces.dispose(windowId, 'window-closed');
      }
    }

    for (const window of windows) {
      const element = this.#windowElements.get(window.id) ?? this.#createWindowElement(window);
      const placement = placements.get(window.id);
      element.hidden = placement === undefined;
      element.style.zIndex = String(window.zIndex + 1);
      element.dataset['state'] = window.state;
      element.dataset['active'] = String(stage.activeWindowId === window.id);
      element.dataset['presentation'] = stage.presentation;
      if (placement?.pane === undefined || placement.pane === null) {
        delete element.dataset['pane'];
      } else {
        element.dataset['pane'] = placement.pane;
      }
      element.querySelector<HTMLButtonElement>('[data-action="maximize"]')
        ?.setAttribute('aria-label', window.state === 'maximized' ? 'Restore' : 'Maximize');
      this.#refreshBackgroundModeControl(element, window);
      applyBounds(element, placement?.bounds ?? area);
      this.#mountWindowSurface(window);
    }

    this.#driveSurfaces(stage, windows);
    this.#renderDivider(stage);
    this.#renderTaskbar();
    this.#updateDockMode(stage.placements.length > 0);
    this.#elements.emptyState.hidden = this.#applications.size > 0;
  }

  /**
   * Whether a tablet places windows freely instead of tiling them.
   *
   * Section 7 enables free placement where there is both room and a precise pointer. A
   * coarse-pointer tablet keeps the tiled default, where every window is large enough to
   * hit without a mouse.
   */
  #allowsFreeWindowPlacement(area: UsableArea): boolean {
    if (this.#workspaceClass !== 'tablet') {
      return false;
    }
    return globalThis.matchMedia('(any-pointer: fine)').matches && area.width >= 1024;
  }

  /** Draws the divider between two phone panes and lets the user move it. */
  #renderDivider(stage: WorkspaceStage): void {
    if (stage.divider === null) {
      this.#dividerElement?.remove();
      this.#dividerElement = null;
      return;
    }

    const divider = this.#dividerElement ?? this.#createDividerElement();
    divider.dataset['orientation'] = stage.orientation;
    applyBounds(divider, stage.divider);
  }

  #createDividerElement(): HTMLElement {
    const divider = document.createElement('div');
    divider.className = 'phone-split-divider';
    divider.setAttribute('role', 'separator');
    divider.setAttribute('aria-label', translate(this.#language(), 'splitDivider'));
    divider.tabIndex = 0;
    divider.addEventListener('pointerdown', (event) => this.#beginDividerDrag(event, divider));
    divider.addEventListener('pointermove', (event) => this.#moveDivider(event));
    divider.addEventListener('pointerup', (event) => this.#endDividerDrag(event, divider));
    divider.addEventListener('pointercancel', (event) => this.#endDividerDrag(event, divider));
    this.#elements.windowLayer.append(divider);
    this.#dividerElement = divider;
    return divider;
  }

  #beginDividerDrag(event: PointerEvent, divider: HTMLElement): void {
    this.#dividerDrag = event.pointerId;
    divider.setPointerCapture(event.pointerId);
    event.preventDefault();
  }

  #moveDivider(event: PointerEvent): void {
    if (this.#dividerDrag !== event.pointerId) {
      return;
    }

    const area = this.#usableArea();
    const portrait = area.height > area.width;
    const offset = portrait ? event.clientY - area.y : event.clientX - area.x;
    const extent = portrait ? area.height : area.width;
    if (extent <= 0) {
      return;
    }

    try {
      this.#phoneForeground.setSplitRatio(clampSplitRatioPermille((offset / extent) * 1000));
    } catch {
      // The split ended under the pointer; there is nothing left to move.
      return;
    }
    this.#renderWindows(this.#store.windows);
  }

  #endDividerDrag(event: PointerEvent, divider: HTMLElement): void {
    if (this.#dividerDrag !== event.pointerId) {
      return;
    }
    this.#dividerDrag = null;
    if (divider.hasPointerCapture(event.pointerId)) {
      divider.releasePointerCapture(event.pointerId);
    }
    // The position is stored once the drag settles, not on every pointer move.
    this.#scheduleLayout();
  }

  /**
   * Offers "Open in split" beside a task that is not already in the foreground.
   *
   * Split is always an explicit user action, so it needs a control of its own; nothing
   * about opening or activating an application may produce one.
   */
  #appendSplitAffordance(container: HTMLElement, windowId: string | undefined): void {
    if (windowId === undefined || this.#workspaceClass !== 'phone') {
      return;
    }

    const foreground = this.#phoneForeground.state();
    if (foreground.primaryWindowId === null || foreground.primaryWindowId === windowId) {
      return;
    }

    const language = this.#language();
    const split = document.createElement('button');
    split.type = 'button';
    split.className = 'taskbar-button taskbar-split-button';
    const ends = foreground.secondaryWindowId === windowId;
    split.title = translate(language, ends ? 'endSplit' : 'openInSplit');
    split.setAttribute('aria-label', split.title);
    split.textContent = ends ? '□' : '◫';
    split.addEventListener('click', () => {
      if (ends) {
        this.#phoneForeground.closeSplit();
        this.#renderWindows(this.#store.windows);
        this.#scheduleLayout();
        return;
      }
      this.#showOnPhone(windowId, true);
    });
    container.append(split);
  }

  /** Brings a window to the phone foreground, replacing the focused pane. */
  #showOnPhone(windowId: string, inSplit: boolean): void {
    if (this.#workspaceClass !== 'phone') {
      return;
    }
    if (inSplit) {
      this.#phoneForeground.showInSplit(windowId);
    } else {
      this.#phoneForeground.show(windowId);
    }
    this.#renderWindows(this.#store.windows);
    this.#scheduleLayout();
  }

  /** Closes a window and keeps the phone foreground consistent with what is open. */
  #closeWindow(windowId: string): void {
    this.#store.close(windowId);
    this.#phoneForeground.closed(windowId);
  }

  #createWindowElement(window: DesktopWindowSnapshot): HTMLElement {
    const element = document.createElement('article');
    element.className = 'desktop-window';
    element.dataset['windowId'] = window.id;
    element.tabIndex = -1;
    element.innerHTML = `
      <header class="window-titlebar">
        <span class="window-title"></span>
        <div class="window-controls">
          <button type="button" data-action="minimize" aria-label="Minimize"><span class="window-control-fallback" aria-hidden="true">−</span></button>
          <button type="button" data-action="maximize" aria-label="Maximize"><span class="window-control-fallback" aria-hidden="true">□</span></button>
          <button type="button" data-action="close" aria-label="Close"><span class="window-control-fallback" aria-hidden="true">×</span></button>
        </div>
      </header>
      <div class="window-body"><div class="window-loading">Loading…</div></div>
    `;
    element.querySelector<HTMLElement>('.window-title')!.textContent = window.title;
    this.#bindWindowActions(element, window.id);
    this.#addResizeHandles(element, window.id);
    this.#elements.windowLayer.append(element);
    this.#windowElements.set(window.id, element);
    return element;
  }

  #bindWindowActions(element: HTMLElement, windowId: string): void {
    element.addEventListener('pointerdown', () => {
      this.#store.focus(windowId);
      this.#scheduleLayout();
    });
    const titlebar = element.querySelector<HTMLElement>('.window-titlebar')!;
    titlebar.addEventListener('dblclick', (event) => {
      if ((event.target as HTMLElement).closest('button') !== null || !this.#isWindowedPresentation()) {
        return;
      }
      const window = this.#requireWindow(windowId);
      if (window.state === 'maximized') {
        this.#store.restore(windowId, this.#usableArea());
      } else if (window.state === 'normal') {
        this.#store.maximize(windowId, this.#usableArea());
      }
      this.#scheduleLayout();
    });
    titlebar.addEventListener('pointerdown', (event) => {
      if (
        event.button !== 0
        || (event.target as HTMLElement).closest('button') !== null
        || !this.#isWindowedPresentation()
      ) {
        return;
      }
      const current = this.#requireWindow(windowId);
      if (current.state !== 'normal') {
        this.#snap.restoreForDrag(
          windowId,
          { x: event.clientX, y: event.clientY },
          this.#usableArea(),
          38,
          96,
        );
      }
      if (this.#interactions.beginMove(windowId, pointerSample(event), {
        usableArea: this.#usableArea(),
        titleBarHeight: 38,
        minimumVisibleTitleBarWidth: 96,
        source: 'draggable',
      })) {
        titlebar.setPointerCapture(event.pointerId);
      }
    });
    titlebar.addEventListener('pointermove', (event) => {
      if (this.#interactions.updatePointer(pointerSample(event))) {
        this.#snap.updatePreview({ x: event.clientX, y: event.clientY }, this.#usableArea());
      }
    });
    titlebar.addEventListener('pointerup', (event) => {
      void this.#finishMove(windowId, event);
    });
    titlebar.addEventListener('pointercancel', (event) => {
      this.#interactions.cancelPointer(event.pointerId);
      this.#snap.clearPreview();
    });

    element.querySelector<HTMLButtonElement>('[data-action="minimize"]')!
      .addEventListener('click', () => {
        this.#store.minimize(windowId);
        this.#scheduleLayout();
      });
    element.querySelector<HTMLButtonElement>('[data-action="maximize"]')!
      .addEventListener('click', () => {
        if (!this.#isWindowedPresentation()) {
          return;
        }
        const current = this.#requireWindow(windowId);
        if (current.state === 'maximized') {
          this.#store.restore(windowId, this.#usableArea());
        } else {
          if (current.state !== 'normal') {
            this.#store.restore(windowId, this.#usableArea());
          }
          this.#store.maximize(windowId, this.#usableArea());
        }
        this.#scheduleLayout();
      });
    element.querySelector<HTMLButtonElement>('[data-action="close"]')!
      .addEventListener('click', () => {
        this.#closeWindow(windowId);
        this.#scheduleLayout();
      });
  }

  async #finishMove(windowId: string, event: PointerEvent): Promise<void> {
    if (await this.#interactions.endPointer(pointerSample(event))) {
      this.#snap.commitPointer(
        windowId,
        { x: event.clientX, y: event.clientY },
        this.#usableArea(),
      );
      this.#scheduleLayout();
    }
  }

  #addResizeHandles(element: HTMLElement, windowId: string): void {
    const edges: readonly ResizeEdge[] = [
      'top', 'right', 'bottom', 'left',
      'top-left', 'top-right', 'bottom-left', 'bottom-right',
    ];
    for (const edge of edges) {
      const handle = document.createElement('div');
      handle.className = `resize-handle resize-${edge}`;
      handle.dataset['edge'] = edge;
      handle.addEventListener('pointerdown', (event) => {
        if (
          event.button !== 0
          || this.#requireWindow(windowId).state !== 'normal'
          || !this.#isWindowedPresentation()
        ) {
          return;
        }
        const application = this.#applications.get(this.#requireWindow(windowId).applicationId);
        if (application === undefined) {
          return;
        }
        this.#interactions.beginResize(windowId, pointerSample(event), {
          usableArea: this.#usableArea(),
          minimumSize: { width: application.minimumWidth, height: application.minimumHeight },
          edge,
        });
        handle.setPointerCapture(event.pointerId);
      });
      handle.addEventListener('pointermove', (event) => void this.#interactions.updatePointer(pointerSample(event)));
      handle.addEventListener('pointerup', (event) => {
        void this.#finishResize(event);
      });
      handle.addEventListener('pointercancel', (event) => void this.#interactions.cancelPointer(event.pointerId));
      element.append(handle);
    }
  }

  async #finishResize(event: PointerEvent): Promise<void> {
    if (await this.#interactions.endPointer(pointerSample(event))) {
      this.#scheduleLayout();
    }
  }

  #mountWindowSurface(window: DesktopWindowSnapshot): void {
    if (this.#windowSurfaces.has(window.id)) {
      return;
    }
    const application = this.#applications.get(window.applicationId);
    if (application === undefined) {
      return;
    }
    const body = this.#windowElements.get(window.id)?.querySelector<HTMLElement>('.window-body');
    if (body === null || body === undefined) {
      return;
    }

    if (this.#coreApplications.isCoreApplication(application.applicationDefinitionId)) {
      const handle = this.#coreApplications.createSurface(application.applicationDefinitionId);
      body.replaceChildren(handle.element);
      this.#windowSurfaces.set(window.id, handle.element);
      this.#coreSurfaceDisposers.set(window.id, handle.dispose);
      return;
    }

    if (application.requiresIsolation === true) {
      // Untrusted code never reaches the Shell realm, so its custom element is never
      // registered here and there is nothing to look up.
      this.#mountIsolatedSurface(window, application, body);
      return;
    }

    if (!customElements.get(application.elementName)) {
      return;
    }
    const target = window.launchTargetId === null
      ? null
      : (application.launchTargets ?? []).find((item) => item.launchTargetId === window.launchTargetId) ?? null;
    const surface = this.#frontendHost.createHostElement(
      application.elementName,
      target === null
        ? null
        : {
            launchTargetId: target.launchTargetId,
            externalIdentity: target.externalIdentity,
            displayName: target.displayName,
          },
    );
    body.replaceChildren(surface.host);
    this.#windowSurfaces.set(window.id, surface.host);
    this.#packageElements.set(window.id, surface.element);
    void this.#registerSurface(window, surface.element);
  }

  #renderTaskbar(): void {
    const container = this.#elements.runningApplications;
    container.replaceChildren();
    for (const group of this.#taskbar.groups) {
      const application = this.#applications.get(group.applicationId);
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'taskbar-button running-application';
      button.dataset['active'] = String(group.activeWindowId !== null);
      button.dataset['minimized'] = String(group.minimizedCount === group.count);
      button.title = group.title;
      button.textContent = applicationTitle(application).slice(0, 1).toLocaleUpperCase();
      if (group.count > 1) {
        const badge = document.createElement('span');
        badge.className = 'window-count';
        badge.textContent = String(group.count);
        button.append(badge);
      }
      button.addEventListener('click', () => {
        const windowId = group.windowIds[0];
        if (windowId !== undefined) {
          this.#taskbar.activateWindow(windowId, this.#usableArea());
          this.#showOnPhone(windowId, false);
          this.#focusActiveWindow();
          this.#scheduleLayout();
        }
      });
      container.append(button);
      this.#appendSplitAffordance(container, group.windowIds[0]);
    }
  }

  #handleKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.#closeLauncher()) {
      event.preventDefault();
      return;
    }
    if (event.key === 'F11' && !event.defaultPrevented) {
      event.preventDefault();
      this.#toggleActiveWindowFullScreen();
      return;
    }
    this.#keyboard.handleKeyDown(event);
  }

  #openLauncher(): void {
    const root = this.#shellRoot();
    const button = root?.getElementById('launcher-button');
    const panel = root?.getElementById('launcher-panel');
    if (!(button instanceof HTMLButtonElement) || panel === null || panel === undefined) {
      return;
    }
    button.setAttribute('aria-expanded', 'true');
    panel.hidden = false;
    // Open as a command palette: reset the query and focus the search field so
    // the user can type immediately (Ctrl/Cmd+K, Cmd+L). Falls back to the panel
    // if the field is not present.
    const searchInput = root?.getElementById('launcher-search');
    if (searchInput instanceof HTMLInputElement) {
      searchInput.value = '';
      this.search('');
      searchInput.focus({ preventScroll: true });
    } else {
      panel.focus({ preventScroll: true });
    }
  }

  #closeLauncher(): boolean {
    const root = this.#shellRoot();
    const button = root?.getElementById('launcher-button');
    const panel = root?.getElementById('launcher-panel');
    if (!(button instanceof HTMLButtonElement) || panel === null || panel === undefined || panel.hidden) {
      return false;
    }
    button.setAttribute('aria-expanded', 'false');
    panel.hidden = true;
    button.focus({ preventScroll: true });
    return true;
  }

  #closeActiveWindow(): void {
    const active = this.#store.frontWindow;
    if (active === null) {
      return;
    }
    this.#closeWindow(active.id);
    this.#scheduleLayout();
  }

  #toggleActiveWindowFullScreen(): void {
    const active = this.#store.frontWindow;
    if (active === null) {
      return;
    }

    if (active.state === 'full-screen') {
      this.#store.restore(active.id, this.#usableArea());
    } else {
      if (active.state !== 'normal') {
        this.#store.restore(active.id, this.#usableArea());
      }
      this.#store.applyFixedState(active.id, 'full-screen', this.#usableArea());
    }
    this.#scheduleLayout();
  }

  #focusActiveWindow(): void {
    const active = this.#store.frontWindow;
    if (active === null) {
      return;
    }
    this.#windowElements.get(active.id)?.focus({ preventScroll: true });
  }

  // iPad-style mobile navigation: when an app fills the screen the dock hides
  // and a home indicator becomes the gesture surface — swipe up (or tap) to
  // reveal the dock, swipe left/right to switch apps.
  #bindHomeIndicator(): void {
    const indicator = this.#shellRoot()?.getElementById('home-indicator');
    if (!(indicator instanceof HTMLElement)) {
      return;
    }
    // Track the whole gesture at the window level: a swipe lifts the finger up
    // in the app content, away from the small indicator, so pointerup must be
    // heard globally or the gesture is lost.
    const finish = (event: PointerEvent): void => {
      const start = this.#homeGestureStart;
      this.#homeGestureStart = null;
      globalThis.removeEventListener('pointerup', finish, true);
      globalThis.removeEventListener('pointercancel', cancel, true);
      if (start === null) {
        return;
      }
      switch (classifyHomeIndicatorGesture(event.clientX - start.x, event.clientY - start.y)) {
        case 'tap':
          this.#toggleDock();
          break;
        case 'switch-next':
          this.#switchWindow(1);
          break;
        case 'switch-previous':
          this.#switchWindow(-1);
          break;
        case 'reveal':
          this.#revealDock();
          break;
        case 'hide':
          this.#hideDock();
          break;
      }
    };
    const cancel = (): void => {
      this.#homeGestureStart = null;
      globalThis.removeEventListener('pointerup', finish, true);
      globalThis.removeEventListener('pointercancel', cancel, true);
    };
    const onDown = (event: PointerEvent): void => {
      event.preventDefault();
      this.#homeGestureStart = { x: event.clientX, y: event.clientY };
      globalThis.addEventListener('pointerup', finish, true);
      globalThis.addEventListener('pointercancel', cancel, true);
    };
    indicator.addEventListener('pointerdown', onDown);
    this.#unbindHomeIndicator = (): void => {
      indicator.removeEventListener('pointerdown', onDown);
      globalThis.removeEventListener('pointerup', finish, true);
      globalThis.removeEventListener('pointercancel', cancel, true);
    };
  }

  #updateDockMode(hasVisibleWindow: boolean): void {
    const root = this.#shellRoot();
    const host = root?.host;
    const indicator = root?.getElementById('home-indicator');
    // Match the CSS, which keys the mobile dock off the host's
    // data-interface-viewport attribute (kept in sync on resize by the
    // interface-plan layer), rather than the runtime's start-time viewport.
    const isMobile = host instanceof HTMLElement && host.dataset['interfaceViewport'] === 'mobile';
    const immersive = isMobile && hasVisibleWindow;
    if (host instanceof HTMLElement) {
      if (immersive) {
        host.setAttribute('data-immersive', 'true');
      } else {
        host.removeAttribute('data-immersive');
        this.#hideDock();
      }
    }
    if (indicator instanceof HTMLElement) {
      indicator.hidden = !immersive;
    }
  }

  #toggleDock(): void {
    const host = this.#shellRoot()?.host;
    if (host instanceof HTMLElement && host.hasAttribute('data-dock-revealed')) {
      this.#hideDock();
    } else {
      this.#revealDock();
    }
  }

  #revealDock(): void {
    const host = this.#shellRoot()?.host;
    if (!(host instanceof HTMLElement)) {
      return;
    }
    host.setAttribute('data-dock-revealed', 'true');
    if (this.#dockRevealTimer !== null) {
      globalThis.clearTimeout(this.#dockRevealTimer);
    }
    this.#dockRevealTimer = globalThis.setTimeout(() => this.#hideDock(), 3500);
  }

  #hideDock(): void {
    if (this.#dockRevealTimer !== null) {
      globalThis.clearTimeout(this.#dockRevealTimer);
      this.#dockRevealTimer = null;
    }
    const host = this.#shellRoot()?.host;
    if (host instanceof HTMLElement) {
      host.removeAttribute('data-dock-revealed');
    }
  }

  #switchWindow(direction: 1 | -1): void {
    const open = this.#store.windows.filter((window) => window.state !== 'minimized');
    if (open.length < 2) {
      return;
    }
    const ordered = [...open].sort((first, second) => first.zIndex - second.zIndex);
    const frontId = this.#store.frontWindow?.id ?? null;
    const index = ordered.findIndex((window) => window.id === frontId);
    const base = index < 0 ? 0 : index;
    const next = ordered[(base + direction + ordered.length) % ordered.length];
    if (next !== undefined) {
      this.#taskbar.activateWindow(next.id, this.#usableArea());
      this.#focusActiveWindow();
      this.#scheduleLayout();
      this.#hideDock();
    }
  }

  #renderWindowSwitcher(snapshot: WindowSwitcherSnapshot | null): void {
    if (snapshot === null) {
      this.#hideWindowSwitcher();
      return;
    }
    const layer = this.#ensureSwitcherLayer();
    layer.replaceChildren();
    for (const windowId of snapshot.windowIds) {
      const window = this.#store.windows.find((candidate) => candidate.id === windowId);
      if (window === undefined) {
        continue;
      }
      const item = document.createElement('div');
      item.className = 'window-switcher-item';
      item.dataset['selected'] = String(windowId === snapshot.selectedWindowId);
      const glyph = document.createElement('span');
      glyph.className = 'application-glyph';
      glyph.textContent = window.title.slice(0, 1).toLocaleUpperCase();
      const title = document.createElement('span');
      title.textContent = window.title;
      item.append(glyph, title);
      layer.append(item);
    }
    layer.hidden = false;
  }

  #ensureSwitcherLayer(): HTMLElement {
    if (this.#switcherLayer !== null) {
      return this.#switcherLayer;
    }
    const layer = document.createElement('div');
    layer.className = 'window-switcher';
    layer.setAttribute('role', 'listbox');
    layer.setAttribute('aria-label', 'Open windows');
    layer.hidden = true;
    this.#elements.windowLayer.append(layer);
    this.#switcherLayer = layer;
    return layer;
  }

  #hideWindowSwitcher(): void {
    if (this.#switcherLayer !== null) {
      this.#switcherLayer.hidden = true;
      this.#switcherLayer.replaceChildren();
    }
  }

  #bindCoreShellActions(): () => void {
    const root = this.#shellRoot();
    if (root === null) {
      return () => undefined;
    }

    const bindings: Array<{ selector: string; applicationId: string }> = [
      { selector: '[data-label="settings"]', applicationId: CoreApplicationIds.settings },
      { selector: '[data-label="notifications"]', applicationId: CoreApplicationIds.notifications },
      { selector: '[data-label="problems"]', applicationId: CoreApplicationIds.problems },
      { selector: '[data-label="agentStatus"]', applicationId: CoreApplicationIds.agents },
    ];
    const cleanup: Array<() => void> = [];
    for (const binding of bindings) {
      for (const element of root.querySelectorAll<HTMLElement>(binding.selector)) {
        const handler = (): void => this.openApplication(binding.applicationId);
        element.addEventListener('click', handler);
        cleanup.push(() => element.removeEventListener('click', handler));
      }
    }
    return () => {
      for (const remove of cleanup) {
        remove();
      }
    };
  }

  #renderStatus(snapshot: NotificationCenterSnapshot): void {
    const root = this.#shellRoot();
    if (root === null) {
      return;
    }
    setStatusCount(root.querySelector<HTMLElement>('[data-label="notifications"]'), snapshot.unreadCount);
    setStatusCount(root.querySelector<HTMLElement>('[data-label="problems"]'), snapshot.activeProblems.length);
  }

  #applyShortcutLabels(): void {
    const root = this.#shellRoot();
    const search = root?.getElementById('search-button');
    if (!(search instanceof HTMLButtonElement)) {
      return;
    }
    const shortcut = `${isApplePlatform() ? '⌘' : 'Ctrl+'}K`;
    const title = search.getAttribute('title') ?? 'Search and commands';
    search.setAttribute('title', `${title} (${shortcut})`);
  }

  #shellRoot(): ShadowRoot | null {
    const root = this.#elements.windowLayer.getRootNode();
    return root instanceof ShadowRoot ? root : null;
  }

  #isWindowedPresentation(): boolean {
    return classifyViewport(Math.max(this.#elements.windowLayer.clientWidth, 320)) === 'desktop';
  }

  #scheduleLayout(): void {
    if (!this.#layoutLoaded || this.#restoringLayout) {
      return;
    }
    const packageWindows = windowsForPersistence(this.#store)
      .filter((window) => !this.#coreApplications.isCoreApplication(window.applicationDefinitionId));
    this.#layoutPersistence.schedule(
      this.#workspaceClass,
      packageWindows,
      this.#widgetPlacements,
      this.#presentationFor(packageWindows),
    );
  }

  /**
   * The arrangement a save stores.
   *
   * Only windows that are themselves persisted may be named as the phone foreground. A
   * Core application window is not part of the stored layout, so pointing the foreground
   * at one would describe a layout the server is right to reject.
   */
  #presentationFor(windows: readonly PersistedDesktopWindow[]): LayoutPresentation | undefined {
    if (this.#workspaceClass !== 'phone') {
      return undefined;
    }

    const persisted = new Set(windows.map((window) => window.windowId));
    const foreground = this.#phoneForeground.state();
    const primary = foreground.primaryWindowId !== null && persisted.has(foreground.primaryWindowId)
      ? foreground.primaryWindowId
      : null;
    const secondary = primary !== null
      && foreground.secondaryWindowId !== null
      && persisted.has(foreground.secondaryWindowId)
      ? foreground.secondaryWindowId
      : null;

    if (primary === null) {
      return {
        presentationMode: 'phone-empty',
        primaryWindowId: null,
        secondaryWindowId: null,
        splitRatioPermille: null,
      };
    }

    return secondary === null
      ? {
        presentationMode: 'phone-single',
        primaryWindowId: primary,
        secondaryWindowId: null,
        splitRatioPermille: null,
      }
      : {
        presentationMode: 'phone-split',
        primaryWindowId: primary,
        secondaryWindowId: secondary,
        splitRatioPermille: clampSplitRatioPermille(foreground.splitRatioPermille ?? 500),
      };
  }

  /**
   * Drives every registered Surface to match what the stage is showing.
   *
   * Window presentation and Surface execution are separate lifecycles, so this translates
   * from one to the other rather than conflating them: a window that is placed and focused
   * has a focused Surface, a window that is placed but not focused is visible, and a window
   * that is open but not on screen goes to its resolved background state.
   */
  #driveSurfaces(stage: WorkspaceStage, windows: readonly DesktopWindowSnapshot[]): void {
    const placements = new Map(stage.placements.map((placement) => [placement.windowId, placement]));

    for (const window of windows) {
      if (this.#surfaces.state(window.id) === null) {
        continue;
      }

      const placement = placements.get(window.id);
      if (placement === undefined) {
        void this.#surfaces.background(window.id, 'window-backgrounded');
        this.#watchSuspended(window.id);
        continue;
      }

      this.#activity.release(window.id);
      void this.#surfaces.show(
        {
          windowId: window.id,
          workspaceClass: this.#workspaceClass,
          presentation: stage.activeWindowId === window.id ? 'focused' : 'visible',
          bounds: { ...placement.bounds },
          revision: window.zIndex + 1,
        },
        'presentation-changed',
      );
    }
  }

  /**
   * Watches a Surface once it has actually reached the suspended state.
   *
   * A Surface whose owner chose to keep it active is deliberately still running, so it is
   * never watched: mutations there are expected rather than a violation.
   */
  #watchSuspended(windowId: string): void {
    const element = this.#packageElements.get(windowId);
    if (element === undefined) {
      return;
    }

    globalThis.queueMicrotask(() => {
      if (this.#surfaces.state(windowId) === 'suspended') {
        this.#activity.watch(windowId, element);
      }
    });
  }

  /**
   * Registers the Surface a package element implements, once its window is mounted.
   *
   * A package that declares the contract in its manifest but does not implement it is
   * refused rather than quietly run without a lifecycle.
   */
  async #registerSurface(window: DesktopWindowSnapshot, element: HTMLElement): Promise<void> {
    const application = this.#applications.get(window.applicationId);
    if (application === undefined || this.#surfaces.state(window.id) !== null) {
      return;
    }

    let preference: ExecutionPreference | null = null;
    try {
      preference = this.#preferences.cached(application.applicationDefinitionId, this.#workspaceClass)
        ?? await this.#preferences.read(application.applicationDefinitionId, this.#workspaceClass);
    } catch (error) {
      this.#onFailure(error);
    }

    try {
      this.#surfaces.register(
        window.id,
        readSurfaceHost(element),
        preference?.backgroundMode ?? 'suspend',
        preference?.supportsKeepSurfaceActive === true,
      );
    } catch (error) {
      // An element that does not implement the contract gets no lifecycle at all. It keeps
      // rendering, because removing a window the user opened would be worse, but it is
      // reported rather than silently treated as Surface-capable.
      this.#onFailure(error);
      return;
    }

    this.#renderWindows(this.#store.windows);
  }

  /** Lets the user choose whether an application keeps running in the background. */
  #refreshBackgroundModeControl(element: HTMLElement, window: DesktopWindowSnapshot): void {
    element.querySelector('[data-action="background-mode"]')?.remove();
    const container = element.querySelector<HTMLElement>('.window-controls');
    const application = this.#applications.get(window.applicationId);
    if (container === null || application === undefined) {
      return;
    }

    const preference = this.#preferences.cached(application.applicationDefinitionId, this.#workspaceClass);
    if (preference === null || !preference.supportsKeepSurfaceActive) {
      // An application that never declared the capability is not offered the choice, and
      // could not be given it by Server either.
      return;
    }

    const language = this.#language();
    const active = preference.backgroundMode === 'keep-surface-active';
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset['action'] = 'background-mode';
    button.dataset['enabled'] = String(active);
    button.setAttribute('aria-pressed', String(active));
    button.title = translate(language, 'keepActiveInBackground');
    button.setAttribute('aria-label', button.title);
    button.innerHTML = '<span class="window-control-fallback" aria-hidden="true">◎</span>';
    button.addEventListener('click', () => {
      button.disabled = true;
      void this.#preferences
        .write(
          application.applicationDefinitionId,
          this.#workspaceClass,
          active ? 'suspend' : 'keep-surface-active',
          preference.revision,
        )
        .then(() => this.#renderWindows(this.#store.windows))
        .catch(this.#onFailure)
        .finally(() => { button.disabled = false; });
    });
    container.prepend(button);
  }

  /**
   * Mounts an untrusted package frontend in its own sandboxed frame.
   *
   * The frame gets an opaque origin, so it holds no JulOS session cookie and can reach
   * neither the Shell DOM nor Core. Everything it may do arrives here as a message and is
   * checked against the package manifest before the Shell acts on it.
   */
  #mountIsolatedSurface(
    window: DesktopWindowSnapshot,
    application: DesktopApplication,
    body: HTMLElement,
  ): void {
    const grants: IsolatedGrants = {
      packageId: application.packageId,
      capabilities: application.requiredCapabilities ?? [],
    };
    const frame = createIsolatedFrame(application.frontend.moduleUrl, application.packageId);

    const listener = (event: MessageEvent): void => {
      // Only the frame the Shell created may speak through this bridge. Anything else is
      // another window shouting at the same page.
      if (event.source !== frame.contentWindow) {
        return;
      }
      void this.#handleIsolatedMessage(frame, grants, event.data);
    };
    globalThis.addEventListener('message', listener);

    body.replaceChildren(frame);
    this.#windowSurfaces.set(window.id, frame);
    this.#isolatedListeners.set(window.id, () => globalThis.removeEventListener('message', listener));
  }

  async #handleIsolatedMessage(
    frame: HTMLIFrameElement,
    grants: IsolatedGrants,
    message: unknown,
  ): Promise<void> {
    const parsed = readIsolatedRequest(message, grants);
    if ('rejection' in parsed) {
      // Lifecycle notices from the frame are not requests and are not rejections either.
      if (!isIsolatedNotice(message)) {
        this.#onFailure(new Error(`${parsed.rejection.code}: ${parsed.rejection.detail}`));
        replyToIsolated(frame, message, { ok: false, code: parsed.rejection.code });
      }
      return;
    }

    const request = parsed.request;
    try {
      if (request.kind === 'capability') {
        const result = await this.#capabilities.invoke(
          grants.packageId,
          request.capability ?? '',
          request.operation ?? '',
          request.payload,
        );
        replyToIsolated(frame, message, { ok: true, requestId: request.requestId, result });
        return;
      }

      await this.openApplication(request.applicationId ?? '', request.targetId);
      replyToIsolated(frame, message, { ok: true, requestId: request.requestId, result: null });
    } catch (error) {
      this.#onFailure(error);
      replyToIsolated(frame, message, {
        ok: false,
        requestId: request.requestId,
        code: 'package.bridge_failed',
      });
    }
  }

  #ensureStyles(): void {
    const root = this.#shellRoot();
    if (root === null || root.querySelector('link[data-julos-desktop-runtime]') !== null) {
      return;
    }
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = './styles/desktop-runtime.css';
    link.dataset['julosDesktopRuntime'] = 'true';
    root.prepend(link);
  }

  #requireWindow(windowId: string): DesktopWindowSnapshot {
    const window = this.#store.windows.find((candidate) => candidate.id === windowId);
    if (window === undefined) {
      throw new Error(`Window '${windowId}' is not open.`);
    }
    return window;
  }

  #usableArea(): UsableArea {
    return {
      x: 0,
      y: 0,
      width: Math.max(this.#elements.windowLayer.clientWidth, 320),
      height: Math.max(this.#elements.windowLayer.clientHeight, 240),
    };
  }
}

/** Whether a message is one of the frame's own lifecycle notices rather than a request. */
function isIsolatedNotice(message: unknown): boolean {
  if (typeof message !== 'object' || message === null) {
    return false;
  }
  const kind = (message as Record<string, unknown>)['kind'];
  return kind === 'ready' || kind === 'failed';
}

/** Replies to an isolated frame, targeting its opaque origin with the wildcard it requires. */
function replyToIsolated(
  frame: HTMLIFrameElement,
  message: unknown,
  reply: { ok: boolean; requestId?: string; result?: unknown; code?: string },
): void {
  const requestId = reply.requestId
    ?? (typeof message === 'object' && message !== null
      ? String((message as Record<string, unknown>)['requestId'] ?? '')
      : '');
  if (requestId.length === 0) {
    return;
  }
  // A sandboxed frame has an opaque origin, which postMessage can only be targeted at
  // with '*'. That is safe here because the reply carries no secret: it is the result of
  // something the frame itself asked for and the Shell already authorized.
  frame.contentWindow?.postMessage({ ...reply, requestId }, '*');
}

function pointerSample(event: PointerEvent) {
  return {
    pointerId: event.pointerId,
    pointerType: event.pointerType,
    clientX: event.clientX,
    clientY: event.clientY,
  };
}

function defaultBounds(application: DesktopApplication, area: UsableArea): WindowBounds {
  const width = Math.min(application.defaultWidth, area.width);
  const height = Math.min(application.defaultHeight, area.height);
  return {
    x: Math.max(0, Math.floor((area.width - width) / 2)),
    y: Math.max(0, Math.floor((area.height - height) / 2)),
    width,
    height,
  };
}

function clampBounds(
  bounds: WindowBounds,
  area: UsableArea,
  minimumWidth: number,
  minimumHeight: number,
): WindowBounds {
  const width = Math.min(Math.max(bounds.width, Math.min(minimumWidth, area.width)), area.width);
  const height = Math.min(Math.max(bounds.height, Math.min(minimumHeight, area.height)), area.height);
  const maximumX = area.x + area.width - width;
  const maximumY = area.y + area.height - height;
  return {
    x: Math.min(Math.max(bounds.x, area.x), maximumX),
    y: Math.min(Math.max(bounds.y, area.y), maximumY),
    width,
    height,
  };
}

function applicationTitle(application: DesktopApplication | undefined): string {
  if (application === undefined) {
    return 'Application';
  }
  if (application.packageId === 'julos.core') {
    return application.displayNameKey;
  }
  return application.stableKey
    .split(/[-_.]+/u)
    .filter((part) => part.length > 0)
    .map((part) => part[0]!.toLocaleUpperCase() + part.slice(1))
    .join(' ');
}

function resolvedTheme(): 'light' | 'dark' {
  const mode = document.documentElement.dataset['theme'];
  if (mode === 'dark' || mode === 'light') {
    return mode;
  }
  return globalThis.matchMedia?.('(prefers-color-scheme: dark)').matches === true ? 'dark' : 'light';
}

function isFixedState(state: PersistedDesktopWindow['state']): state is FixedWindowState {
  return state !== 'normal' && state !== 'minimized';
}

function applyBounds(element: HTMLElement, bounds: WindowBounds): void {
  element.style.left = `${bounds.x}px`;
  element.style.top = `${bounds.y}px`;
  element.style.width = `${bounds.width}px`;
  element.style.height = `${bounds.height}px`;
}

function applyWidgetPlacement(element: HTMLElement, placement: PersistedWidgetPlacement): void {
  const unit = 88;
  const gap = 8;
  const inset = 16;
  element.style.left = `${inset + placement.gridColumn * unit}px`;
  element.style.top = `${inset + placement.gridRow * unit}px`;
  element.style.width = `${Math.max(unit - gap, placement.widthUnits * unit - gap)}px`;
  element.style.height = `${Math.max(unit - gap, placement.heightUnits * unit - gap)}px`;
}

function setStatusCount(element: HTMLElement | null, count: number): void {
  if (element === null) {
    return;
  }
  element.querySelector('.core-status-count')?.remove();
  if (count < 1) {
    return;
  }
  const badge = document.createElement('span');
  badge.className = 'core-status-count';
  badge.textContent = count > 99 ? '99+' : String(count);
  element.append(badge);
}

function isApplePlatform(): boolean {
  return /Mac|iPhone|iPad/u.test(globalThis.navigator?.platform ?? '');
}
