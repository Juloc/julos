export interface ShellKeyboardActions {
  readonly openLauncher: () => void;
  readonly openCommandPalette: () => void;
  readonly openNotifications: () => void;
  readonly openProblems: () => void;
  readonly beginWindowSwitcher: () => void;
  readonly nextWindow: () => void;
  readonly previousWindow: () => void;
  readonly commitWindowSwitcher: () => void;
  readonly cancelWindowSwitcher: () => void;
  readonly closeActiveWindow: () => void;
  readonly restoreFocus: () => void;
}

/** Centralizes global shortcuts so package modules cannot intercept shell ownership keys. */
export class ShellKeyboardController {
  readonly #actions: ShellKeyboardActions;
  #switching = false;

  public constructor(actions: ShellKeyboardActions) {
    this.#actions = actions;
  }

  public handleKeyDown(event: KeyboardEvent): boolean {
    if (event.defaultPrevented || isEditableTarget(event.target)) {
      return false;
    }

    if (event.altKey && event.key === 'Tab') {
      event.preventDefault();
      if (!this.#switching) {
        this.#switching = true;
        this.#actions.beginWindowSwitcher();
      } else if (event.shiftKey) {
        this.#actions.previousWindow();
      } else {
        this.#actions.nextWindow();
      }
      return true;
    }

    if (event.key === 'Escape' && this.#switching) {
      event.preventDefault();
      this.#switching = false;
      this.#actions.cancelWindowSwitcher();
      this.#actions.restoreFocus();
      return true;
    }

    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.#actions.openCommandPalette();
      return true;
    }

    if (event.metaKey && event.key.toLowerCase() === 'l') {
      event.preventDefault();
      this.#actions.openLauncher();
      return true;
    }

    if (event.metaKey && event.key.toLowerCase() === 'n') {
      event.preventDefault();
      this.#actions.openNotifications();
      return true;
    }

    if (event.metaKey && event.key.toLowerCase() === 'p') {
      event.preventDefault();
      this.#actions.openProblems();
      return true;
    }

    if (event.altKey && event.key === 'F4') {
      event.preventDefault();
      this.#actions.closeActiveWindow();
      return true;
    }

    return false;
  }

  public handleKeyUp(event: KeyboardEvent): boolean {
    if (!this.#switching || event.key !== 'Alt') {
      return false;
    }
    event.preventDefault();
    this.#switching = false;
    this.#actions.commitWindowSwitcher();
    this.#actions.restoreFocus();
    return true;
  }
}

/** Restores focus only to connected elements and otherwise uses the shell fallback. */
export class FocusReturnController {
  readonly #fallback: () => HTMLElement | null;
  #returnTarget: HTMLElement | null = null;

  public constructor(fallback: () => HTMLElement | null) {
    this.#fallback = fallback;
  }

  public capture(element: Element | null = document.activeElement): void {
    this.#returnTarget = element instanceof HTMLElement ? element : null;
  }

  public restore(): void {
    const target = this.#returnTarget?.isConnected === true
      ? this.#returnTarget
      : this.#fallback();
    this.#returnTarget = null;
    target?.focus({ preventScroll: true });
  }
}

export function applyAccessibleDialogState(
  dialog: HTMLDialogElement,
  open: boolean,
  focusReturn: FocusReturnController,
): void {
  if (open) {
    focusReturn.capture();
    if (!dialog.open) {
      dialog.showModal();
    }
    const first = dialog.querySelector<HTMLElement>(
      'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
    );
    first?.focus({ preventScroll: true });
  } else if (dialog.open) {
    dialog.close();
    focusReturn.restore();
  }
}

function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  return target.isContentEditable
    || target instanceof HTMLInputElement
    || target instanceof HTMLTextAreaElement
    || target instanceof HTMLSelectElement;
}
