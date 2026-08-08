import { CoreApplicationCatalog, CoreApplicationIds } from './core-applications.js';
import { desktopNotificationCenter } from './desktop-observability.js';
import { LauncherIndex, type LauncherSearchResult } from './launcher-index.js';
import {
  DesktopLayoutPersistence,
  windowsForPersistence,
  type DesktopLayoutDocument,
  type DesktopViewport,
  type PersistedDesktopWindow,
  type PersistedWidgetPlacement,
} from './layout-persistence.js';
import type { NotificationCenterSnapshot, NotificationCenterStore } from './notification-center.js';
import { PackageCapabilityClient } from './package-capability-client.js';
import { PackageFrontendHost } from './package-frontend-host.js';
import type { SupportedLanguage } from './localization.js';
import { classifyViewport, deriveResponsiveDesktop } from './responsive-desktop.js';
import { ShellKeyboardController } from './shell-keyboard.js';
import type { DesktopApplication, DesktopWidget, ShellApiClient } from './shell-api.js';
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
  #layoutLoaded = false;
  #restoringLayout = false;
  #unsubscribeWindows: (() => void) | null = null;
  #unsubscribeSnap: (() => void) | null = null;
  #unsubscribeObservability: (() => void) | null = null;
  #unbindCoreShellActions: (() => void) | null = null;

  public constructor(options: DesktopRuntimeOptions) {
    this.#api = options.api;
    this.#elements = options.elements;
    this.#notifications = options.notifications ?? desktopNotificationCenter;
    this.#language = options.language;
    this.#onFailure = options.onFailure;
    this.#layoutPersistence = new DesktopLayoutPersistence(
      globalThis.fetch.bind(globalThis),
      { onFailure: (error) => this.#onFailure(error) },
    );
    this.#coreApplications = new CoreApplicationCatalog({
      api: options.api,
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
    const [packageApplications, widgets] = await this.#readPackageCatalog();
    this.#replaceCatalog(packageApplications, widgets);

    let layout: DesktopLayoutDocument | null = null;
    try {
      layout = await this.#layoutPersistence.load(this.#viewport);
      this.#layoutLoaded = true;
    } catch (error) {
      this.#onFailure(error);
    }

    if (layout !== null) {
      this.#widgetPlacements = layout.widgets.map((placement) => ({ ...placement }));
      this.#restoringLayout = true;
      try {
        this.#restoreLayout(layout.windows);
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

    await Promise.all([this.#loadRestoredFrontends(), this.#renderWidgets()]);
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
    this.#windowSwitcher.cancel();
    this.#hideWindowSwitcher();

    if (this.#layoutLoaded) {
      void this.#layoutPersistence.flush(this.#viewport)
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

  #replaceCatalog(
    packageApplications: readonly DesktopApplication[],
    widgets: readonly DesktopWidget[],
  ): void {
    const applications = [...this.#coreApplications.applications(), ...packageApplications];
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
    const [packageApplications, widgets] = await this.#readPackageCatalog();
    this.#replaceCatalog(packageApplications, widgets);

    const availableApplications = new Set(this.#applications.keys());
    for (const window of [...this.#store.windows]) {
      if (!availableApplications.has(window.applicationId)) {
        this.#store.close(window.id);
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
      this.#renderWindows(this.#store.windows);
      this.#focusActiveWindow();
      this.#scheduleLayout();
    } catch (error) {
      if (launchedWindowId !== null && this.#store.windows.some((window) => window.id === launchedWindowId)) {
        this.#store.close(launchedWindowId);
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
        frame.append(surface);
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
    const responsive = deriveResponsiveDesktop(
      Math.max(this.#elements.windowLayer.clientWidth, 320),
      windows,
      this.#store.frontWindow?.id ?? null,
    );
    const visibleWindowIds = new Set(responsive.visibleWindows.map((window) => window.id));
    const area = this.#usableArea();
    const openIds = new Set(windows.map((window) => window.id));

    for (const [windowId, element] of this.#windowElements) {
      if (!openIds.has(windowId)) {
        element.remove();
        this.#windowElements.delete(windowId);
        this.#windowSurfaces.delete(windowId);
        this.#coreSurfaceDisposers.get(windowId)?.();
        this.#coreSurfaceDisposers.delete(windowId);
      }
    }

    for (const window of windows) {
      const element = this.#windowElements.get(window.id) ?? this.#createWindowElement(window);
      element.hidden = window.state === 'minimized' || !visibleWindowIds.has(window.id);
      element.style.zIndex = String(window.zIndex + 1);
      element.dataset['state'] = window.state;
      element.dataset['active'] = String(this.#store.frontWindow?.id === window.id);
      element.dataset['presentation'] = responsive.presentation;
      applyBounds(element, responsive.presentation === 'windowed' ? window.bounds : area);
      this.#mountWindowSurface(window);
    }

    this.#renderTaskbar();
    this.#elements.emptyState.hidden = this.#applications.size > 0;
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
          <button type="button" data-action="minimize" aria-label="Minimize">−</button>
          <button type="button" data-action="maximize" aria-label="Maximize">□</button>
          <button type="button" data-action="fullscreen" aria-label="Full screen">◇</button>
          <button type="button" data-action="close" aria-label="Close">×</button>
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
    element.querySelector<HTMLButtonElement>('[data-action="fullscreen"]')!
      .addEventListener('click', () => {
        const current = this.#requireWindow(windowId);
        if (current.state === 'full-screen') {
          this.#store.restore(windowId, this.#usableArea());
        } else {
          if (current.state !== 'normal') {
            this.#store.restore(windowId, this.#usableArea());
          }
          this.#store.applyFixedState(windowId, 'full-screen', this.#usableArea());
        }
        this.#scheduleLayout();
      });
    element.querySelector<HTMLButtonElement>('[data-action="close"]')!
      .addEventListener('click', () => {
        this.#store.close(windowId);
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
    body.replaceChildren(surface);
    this.#windowSurfaces.set(window.id, surface);
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
          this.#focusActiveWindow();
          this.#scheduleLayout();
        }
      });
      container.append(button);
    }
  }

  #handleKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.#closeLauncher()) {
      event.preventDefault();
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
    panel.focus({ preventScroll: true });
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
    this.#store.close(active.id);
    this.#scheduleLayout();
  }

  #focusActiveWindow(): void {
    const active = this.#store.frontWindow;
    if (active === null) {
      return;
    }
    this.#windowElements.get(active.id)?.focus({ preventScroll: true });
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
      this.#viewport,
      packageWindows,
      this.#widgetPlacements,
    );
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
