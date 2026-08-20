import { classifyViewport } from './responsive-desktop.js';

export type InterfaceViewport = 'desktop' | 'tablet' | 'mobile';

export const interfacePlanStyleHref = './styles/interface-plan.css';
export const desktopEditLongPressMs = 520;
export const desktopEditMovementTolerance = 12;

export function classifyInterfaceViewport(width: number): InterfaceViewport {
  if (!Number.isFinite(width) || width <= 0) {
    return 'desktop';
  }
  return classifyViewport(width);
}

export function canStartDesktopEditMode(target: Element | null): boolean {
  if (target === null) {
    return false;
  }

  return target.closest(
    'button, input, select, textarea, a, dialog, .authentication-view, .authentication-card, .desktop-window, .launcher-panel, .taskbar, [contenteditable="true"]',
  ) === null;
}

/**
 * Adds the final JulOS shell presentation layer without introducing a second
 * window/taskbar/launcher model. Behaviour continues to be owned by the
 * existing DesktopRuntime; this controller only owns shell-level presentation
 * state and the desktop edit-mode gesture.
 */
export class InterfacePlanController {
  readonly #host: HTMLElement;
  readonly #root: ShadowRoot;
  readonly #desktop: HTMLElement;
  readonly #window: Window;
  #timer: ReturnType<typeof globalThis.setTimeout> | null = null;
  #pointerId: number | null = null;
  #pointerX = 0;
  #pointerY = 0;
  #connected = false;
  #toolbar: HTMLDivElement | null = null;

  readonly #resizeHandler = (): void => this.#syncViewport();
  readonly #pointerDownHandler: EventListener = (event): void => this.#onPointerDown(event as PointerEvent);
  readonly #pointerMoveHandler: EventListener = (event): void => this.#onPointerMove(event as PointerEvent);
  readonly #pointerEndHandler: EventListener = (event): void => this.#onPointerEnd(event as PointerEvent);
  readonly #keyDownHandler: EventListener = (event): void => {
    const keyboardEvent = event as KeyboardEvent;
    if (keyboardEvent.key === 'Escape' && this.editMode) {
      keyboardEvent.preventDefault();
      this.exitEditMode();
    }
  };

  public constructor(host: HTMLElement) {
    const root = host.shadowRoot;
    const desktop = root?.querySelector<HTMLElement>('#desktop-root') ?? null;
    const view = host.ownerDocument.defaultView;
    if (root === null || desktop === null || view === null) {
      throw new Error('The JulOS shell must be connected before the interface plan is installed.');
    }

    this.#host = host;
    this.#root = root;
    this.#desktop = desktop;
    this.#window = view;
  }

  public get editMode(): boolean {
    return this.#desktop.dataset['editMode'] === 'true';
  }

  public connect(): void {
    if (this.#connected) {
      return;
    }

    this.#connected = true;
    this.#ensureStyleSheet();
    this.#ensureEditToolbar();
    this.#syncViewport();
    this.#root.addEventListener('pointerdown', this.#pointerDownHandler, true);
    this.#root.addEventListener('pointermove', this.#pointerMoveHandler, true);
    this.#root.addEventListener('pointerup', this.#pointerEndHandler, true);
    this.#root.addEventListener('pointercancel', this.#pointerEndHandler, true);
    this.#root.addEventListener('keydown', this.#keyDownHandler, true);
    this.#window.addEventListener('resize', this.#resizeHandler);
  }

  public disconnect(): void {
    if (!this.#connected) {
      return;
    }

    this.#connected = false;
    this.#cancelLongPress();
    this.#root.removeEventListener('pointerdown', this.#pointerDownHandler, true);
    this.#root.removeEventListener('pointermove', this.#pointerMoveHandler, true);
    this.#root.removeEventListener('pointerup', this.#pointerEndHandler, true);
    this.#root.removeEventListener('pointercancel', this.#pointerEndHandler, true);
    this.#root.removeEventListener('keydown', this.#keyDownHandler, true);
    this.#window.removeEventListener('resize', this.#resizeHandler);
  }

  public enterEditMode(): void {
    if (this.editMode) {
      return;
    }

    this.#desktop.dataset['editMode'] = 'true';
    this.#syncEditToolbarLanguage();
    if (this.#toolbar !== null) {
      this.#toolbar.hidden = false;
    }
    this.#dispatchEditModeChange(true);
  }

  public exitEditMode(): void {
    if (!this.editMode) {
      return;
    }

    delete this.#desktop.dataset['editMode'];
    if (this.#toolbar !== null) {
      this.#toolbar.hidden = true;
    }
    this.#dispatchEditModeChange(false);
  }


  #dispatchEditModeChange(active: boolean): void {
    const event = this.#host.ownerDocument.createEvent('CustomEvent');
    event.initCustomEvent('julos-edit-mode-change', true, true, { active });
    this.#host.dispatchEvent(event);
  }

  #ensureStyleSheet(): void {
    if (this.#root.querySelector('link[data-julos-interface-plan]') !== null) {
      return;
    }

    const link = this.#host.ownerDocument.createElement('link');
    link.rel = 'stylesheet';
    link.href = interfacePlanStyleHref;
    link.dataset['julosInterfacePlan'] = 'true';
    this.#root.append(link);
  }

  #ensureEditToolbar(): void {
    const existing = this.#root.querySelector<HTMLDivElement>('.desktop-edit-toolbar');
    if (existing !== null) {
      this.#toolbar = existing;
      return;
    }

    const toolbar = this.#host.ownerDocument.createElement('div');
    toolbar.className = 'desktop-edit-toolbar';
    toolbar.hidden = true;
    toolbar.setAttribute('role', 'toolbar');
    toolbar.setAttribute('aria-label', this.#host.ownerDocument.documentElement.lang === 'de'
      ? 'Desktop bearbeiten'
      : 'Edit desktop');

    const label = this.#host.ownerDocument.createElement('span');
    label.className = 'desktop-edit-label';
    label.textContent = this.#host.ownerDocument.documentElement.lang === 'de'
      ? 'Desktop bearbeiten'
      : 'Edit desktop';

    const done = this.#host.ownerDocument.createElement('button');
    done.type = 'button';
    done.className = 'desktop-edit-done';
    done.textContent = this.#host.ownerDocument.documentElement.lang === 'de' ? 'Fertig' : 'Done';
    done.addEventListener('click', () => this.exitEditMode());

    toolbar.append(label, done);
    this.#desktop.append(toolbar);
    this.#toolbar = toolbar;
    this.#syncEditToolbarLanguage();
  }

  #syncEditToolbarLanguage(): void {
    if (this.#toolbar === null) {
      return;
    }

    const german = this.#host.ownerDocument.documentElement.lang === 'de';
    this.#toolbar.setAttribute('aria-label', german ? 'Desktop bearbeiten' : 'Edit desktop');
    this.#toolbar.querySelector<HTMLElement>('.desktop-edit-label')!.textContent = german
      ? 'Desktop bearbeiten'
      : 'Edit desktop';
    this.#toolbar.querySelector<HTMLButtonElement>('.desktop-edit-done')!.textContent = german
      ? 'Fertig'
      : 'Done';
  }

  #syncViewport(): void {
    const width = Math.max(this.#host.getBoundingClientRect().width, this.#window.innerWidth, 320);
    this.#host.dataset['interfaceViewport'] = classifyInterfaceViewport(width);
  }

  #onPointerDown(event: PointerEvent): void {
    if (
      this.editMode
      || !event.isPrimary
      || event.button !== 0
      || !canStartDesktopEditMode(asElement(event.target))
    ) {
      return;
    }

    this.#cancelLongPress();
    this.#pointerId = event.pointerId;
    this.#pointerX = event.clientX;
    this.#pointerY = event.clientY;
    this.#timer = globalThis.setTimeout(() => {
      this.#timer = null;
      this.#pointerId = null;
      this.enterEditMode();
    }, desktopEditLongPressMs);
  }

  #onPointerMove(event: PointerEvent): void {
    if (this.#pointerId !== event.pointerId) {
      return;
    }

    const distance = Math.hypot(event.clientX - this.#pointerX, event.clientY - this.#pointerY);
    if (distance > desktopEditMovementTolerance) {
      this.#cancelLongPress();
    }
  }

  #onPointerEnd(event: PointerEvent): void {
    if (this.#pointerId === event.pointerId) {
      this.#cancelLongPress();
    }
  }

  #cancelLongPress(): void {
    if (this.#timer !== null) {
      globalThis.clearTimeout(this.#timer);
      this.#timer = null;
    }
    this.#pointerId = null;
  }
}

function asElement(target: EventTarget | null): Element | null {
  if (target === null || typeof (target as Element).closest !== 'function') {
    return null;
  }
  return target as Element;
}

export function installInterfacePlan(document: Document = globalThis.document): InterfacePlanController | null {
  const shell = document.querySelector<HTMLElement>('julos-shell');
  if (shell === null || shell.shadowRoot === null) {
    return null;
  }

  const controller = new InterfacePlanController(shell);
  controller.connect();
  return controller;
}
