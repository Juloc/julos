import { LauncherIndex, type LauncherSearchResult } from './launcher-index.js';
import {
  DesktopLayoutPersistence,
  windowsForPersistence,
  type DesktopLayoutDocument,
  type DesktopViewport,
  type PersistedDesktopWindow,
} from './layout-persistence.js';
import { PackageCapabilityClient } from './package-capability-client.js';
import { PackageFrontendHost } from './package-frontend-host.js';
import type { SupportedLanguage } from './localization.js';
import type { DesktopApplication, ShellApiClient } from './shell-api.js';
import { WindowInteractionController, type ResizeEdge } from './window-interactions.js';
import { WindowSnapController } from './window-snapping.js';
import { TaskbarWindowModel, WindowLaunchCoordinator } from './window-taskbar.js';
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
  readonly language: () => SupportedLanguage;
  readonly onFailure: (error: unknown) => void;
}

/** Composes the existing launcher, package frontend, persistence and window controllers. */
export class DesktopRuntime {
  readonly #api: ShellApiClient;
  readonly #elements: DesktopRuntimeElements;
  readonly #language: () => SupportedLanguage;
  readonly #onFailure: (error: unknown) => void;
  readonly #store = new WindowStore();
  readonly #launcherCoordinator = new WindowLaunchCoordinator(this.#store);
  readonly #taskbar = new TaskbarWindowModel(this.#store);
  readonly #interactions = new WindowInteractionController(this.#store);
  readonly #snap = new WindowSnapController(this.#store);
  readonly #frontendHost = new PackageFrontendHost();
  readonly #capabilities = new PackageCapabilityClient();
  readonly #layoutPersistence: DesktopLayoutPersistence;
  readonly #applications = new Map<string, DesktopApplication>();
  readonly #windowElements = new Map<string, HTMLElement>();
  readonly #packageSurfaces = new Map<string, HTMLElement>();
  #launcher: LauncherIndex | null = null;
  #viewport: DesktopViewport = 'desktop';
  #layoutLoaded = false;
  #restoringLayout = false;
  #unsubscribeWindows: (() => void) | null = null;
  #unsubscribeSnap: (() => void) | null = null;

  public constructor(options: DesktopRuntimeOptions) {
    this.#api = options.api;
    this.#elements = options.elements;
    this.#language = options.language;
    this.#onFailure = options.onFailure;
    this.#layoutPersistence = new DesktopLayoutPersistence(
      globalThis.fetch.bind(globalThis),
      { onFailure: (error) => this.#onFailure(error) },
    );
  }

  public async start(): Promise<void> {
    if (this.#unsubscribeWindows !== null) {
      return;
    }

    this.#ensureStyles();
    this.#viewport = viewportClass(this.#elements.windowLayer.clientWidth);
    const applications = await this.#api.readApplications(this.#viewport);
    this.#applications.clear();
    for (const application of applications) {
      this.#applications.set(application.applicationDefinitionId, application);
    }

    this.#launcher = new LauncherIndex({
      applications: applications.map((application) => ({
        applicationId: application.applicationDefinitionId,
        title: applicationTitle(application),
        description: application.packageId,
        keywords: [application.stableKey, application.packageId],
        instancePolicy: application.instancePolicy,
        defaultBounds: defaultBounds(application, this.#usableArea()),
        requiredPermissions: [],
      })),
      targets: [],
      commands: [],
    }, []);

    let layout: DesktopLayoutDocument | null = null;
    try {
      layout = await this.#layoutPersistence.load(this.#viewport);
      this.#layoutLoaded = true;
    } catch (error) {
      this.#onFailure(error);
    }

    if (layout !== null) {
      this.#restoringLayout = true;
      try {
        this.#restoreLayout(layout.windows);
      } finally {
        this.#restoringLayout = false;
      }
    }

    this.#renderLauncher('');
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

    await this.#loadRestoredFrontends();
  }

  public stop(): void {
    this.#unsubscribeWindows?.();
    this.#unsubscribeSnap?.();
    this.#unsubscribeWindows = null;
    this.#unsubscribeSnap = null;

    if (this.#layoutLoaded) {
      void this.#layoutPersistence.flush(this.#viewport)
        .catch((error: unknown) => this.#onFailure(error))
        .finally(() => this.#layoutPersistence.dispose());
    } else {
      this.#layoutPersistence.dispose();
    }

    this.#store.clear();
    this.#applications.clear();
    this.#windowElements.clear();
    this.#packageSurfaces.clear();
    this.#elements.windowLayer.replaceChildren(this.#elements.snapPreview);
    this.#elements.runningApplications.replaceChildren();
    this.#elements.launcherEntries.replaceChildren();
    this.#launcher = null;
  }

  public search(query: string): void {
    this.#renderLauncher(query);
  }

  public openApplication(applicationId: string): void {
    const launcher = this.#launcher;
    if (launcher === null) {
      return;
    }
    const result = launcher.search('').find((entry) =>
      entry.kind === 'application' && entry.applicationId === applicationId,
    );
    if (result !== undefined) {
      void this.#launch(result);
    }
  }

  #renderLauncher(query: string): void {
    const launcher = this.#launcher;
    const container = this.#elements.launcherEntries;
    container.replaceChildren();
    if (launcher === null) {
      return;
    }

    for (const result of launcher.search(query)) {
      if (result.kind !== 'application') {
        continue;
      }
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'launcher-entry application-entry';
      button.dataset['applicationId'] = result.applicationId ?? result.id;
      const mark = document.createElement('span');
      mark.className = 'application-glyph';
      mark.textContent = result.title.slice(0, 1).toLocaleUpperCase();
      const text = document.createElement('span');
      text.className = 'application-entry-text';
      const title = document.createElement('strong');
      title.textContent = result.title;
      const description = document.createElement('small');
      description.textContent = result.description;
      text.append(title, description);
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
      await this.#loadFrontend(application);
      this.#renderWindows(this.#store.windows);
      this.#scheduleLayout();
    } catch (error) {
      if (launchedWindowId !== null && this.#store.windows.some((window) => window.id === launchedWindowId)) {
        this.#store.close(launchedWindowId);
      }
      this.#onFailure(error);
    }
  }

  async #loadFrontend(application: DesktopApplication): Promise<void> {
    await this.#frontendHost.load({
      packageId: application.packageId,
      version: application.packageVersion,
      moduleUrl: application.frontend.moduleUrl,
      sha256: application.frontend.sha256,
      exportedElements: application.frontend.exportedElements,
    }, {
      packageId: application.packageId,
      language: this.#language(),
      theme: resolvedTheme(),
      invokeCapability: (name, operation, payload) =>
        this.#capabilities.invoke(application.packageId, name, operation, payload),
      openApplication: (applicationId) => this.openApplication(applicationId),
    });
  }

  async #loadRestoredFrontends(): Promise<void> {
    const applicationIds = new Set(this.#store.windows.map((window) => window.applicationId));
    for (const applicationId of applicationIds) {
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

  #restoreLayout(windows: readonly PersistedDesktopWindow[]): void {
    const area = this.#usableArea();
    for (const persisted of [...windows].sort((left, right) => left.zIndex - right.zIndex)) {
      const application = this.#applications.get(persisted.applicationDefinitionId);
      if (application === undefined) {
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

      this.#store.open({
        id: persisted.windowId,
        applicationId: persisted.applicationDefinitionId,
        launchTargetId: persisted.launchTargetId,
        title: applicationTitle(application),
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
    const openIds = new Set(windows.map((window) => window.id));
    for (const [windowId, element] of this.#windowElements) {
      if (!openIds.has(windowId)) {
        element.remove();
        this.#windowElements.delete(windowId);
        this.#packageSurfaces.delete(windowId);
      }
    }

    for (const window of windows) {
      const element = this.#windowElements.get(window.id) ?? this.#createWindowElement(window);
      element.hidden = window.state === 'minimized';
      element.style.zIndex = String(window.zIndex + 1);
      applyBounds(element, window.bounds);
      element.dataset['state'] = window.state;
      element.dataset['active'] = String(this.#store.frontWindow?.id === window.id);
      this.#mountPackageSurface(window);
    }

    this.#renderTaskbar();
    this.#elements.emptyState.hidden = windows.length > 0 || this.#applications.size > 0;
  }

  #createWindowElement(window: DesktopWindowSnapshot): HTMLElement {
    const element = document.createElement('article');
    element.className = 'desktop-window';
    element.dataset['windowId'] = window.id;
    element.innerHTML = `
      <header class="window-titlebar">
        <span class="window-title"></span>
        <div class="window-controls">
          <button type="button" data-action="minimize" aria-label="Minimize">−</button>
          <button type="button" data-action="maximize" aria-label="Maximize">□</button>
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
      if ((event.target as HTMLElement).closest('button') !== null) {
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
      if (event.button !== 0 || (event.target as HTMLElement).closest('button') !== null) {
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
        if (event.button !== 0 || this.#requireWindow(windowId).state !== 'normal') {
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

  #mountPackageSurface(window: DesktopWindowSnapshot): void {
    if (this.#packageSurfaces.has(window.id)) {
      return;
    }
    const application = this.#applications.get(window.applicationId);
    if (application === undefined || !customElements.get(application.elementName)) {
      return;
    }
    const body = this.#windowElements.get(window.id)?.querySelector<HTMLElement>('.window-body');
    if (body === null || body === undefined) {
      return;
    }
    const surface = this.#frontendHost.createHostElement(application.elementName);
    body.replaceChildren(surface);
    this.#packageSurfaces.set(window.id, surface);
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
          this.#scheduleLayout();
        }
      });
      container.append(button);
    }
  }

  #scheduleLayout(): void {
    if (!this.#layoutLoaded || this.#restoringLayout) {
      return;
    }
    this.#layoutPersistence.schedule(
      this.#viewport,
      windowsForPersistence(this.#store),
      [],
    );
  }

  #ensureStyles(): void {
    const root = this.#elements.windowLayer.getRootNode();
    if (!(root instanceof ShadowRoot) || root.querySelector('link[data-julos-desktop-runtime]') !== null) {
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
  return application.stableKey
    .split(/[-_.]+/u)
    .filter((part) => part.length > 0)
    .map((part) => part[0]!.toLocaleUpperCase() + part.slice(1))
    .join(' ');
}

function viewportClass(width: number): DesktopViewport {
  return width < 600 ? 'mobile' : width < 1024 ? 'tablet' : 'desktop';
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
