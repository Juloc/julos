// MOB-002: safe-area, dynamic viewport height and software-keyboard handling from
// docs/MOBILE_PWA.md section 14.
//
// The rule that matters for correctness: the software keyboard changes the *visual*
// viewport, never the layout viewport. Workspace identity is resolved from the layout
// viewport (see workspace-contract.ts), so opening the keyboard must not reclassify a
// tablet as a phone. This module therefore exposes the two independently and publishes
// the keyboard inset as a CSS variable rather than resizing anything itself.

/** The dynamic metrics the Shell publishes to CSS. */
export interface ViewportMetrics {
  /** `document.documentElement.clientWidth` — workspace identity comes from this. */
  readonly layoutWidthCssPx: number;
  /** `document.documentElement.clientHeight`. */
  readonly layoutHeightCssPx: number;
  /** Height actually visible right now; shrinks when a software keyboard opens. */
  readonly visualHeightCssPx: number;
  /** Pixels currently hidden behind a software keyboard, never negative. */
  readonly keyboardInsetCssPx: number;
  /** True while a software keyboard is covering part of the visual viewport. */
  readonly keyboardVisible: boolean;
}

/** The subset of `VisualViewport` this module reads. */
export interface VisualViewportLike {
  readonly height: number;
  readonly offsetTop: number;
  addEventListener(type: 'resize' | 'scroll', listener: () => void): void;
  removeEventListener(type: 'resize' | 'scroll', listener: () => void): void;
}

/** CSS custom properties the Shell sets from these metrics. */
export const viewportCssVariables = Object.freeze({
  /** Usable height, equivalent to `100dvh` where that is supported. */
  viewportHeight: '--julos-viewport-height',
  /** Height hidden behind a software keyboard. */
  keyboardInset: '--julos-keyboard-inset',
});

/**
 * A keyboard is only considered visible once it hides a meaningful strip. Browser
 * chrome collapsing on scroll also shrinks the visual viewport by a few pixels, and that
 * is not a keyboard.
 */
const keyboardThresholdCssPx = 120;

/** Computes the metrics from a layout size and an optional visual viewport. */
export function readViewportMetrics(
  layoutWidthCssPx: number,
  layoutHeightCssPx: number,
  visualViewport: VisualViewportLike | null,
): ViewportMetrics {
  const visualHeightCssPx = visualViewport?.height ?? layoutHeightCssPx;
  const hidden = Math.max(0, Math.round(layoutHeightCssPx - visualHeightCssPx));

  return {
    layoutWidthCssPx,
    layoutHeightCssPx,
    visualHeightCssPx,
    keyboardInsetCssPx: hidden,
    keyboardVisible: hidden >= keyboardThresholdCssPx,
  };
}

/** Minimal element view used to publish the metrics. */
export interface StyleTarget {
  readonly style: { setProperty(property: string, value: string): void };
}

/** Publishes the metrics as CSS custom properties. */
export function applyViewportMetrics(target: StyleTarget, metrics: ViewportMetrics): void {
  target.style.setProperty(
    viewportCssVariables.viewportHeight,
    `${metrics.visualHeightCssPx}px`,
  );
  target.style.setProperty(
    viewportCssVariables.keyboardInset,
    `${metrics.keyboardInsetCssPx}px`,
  );
}

export interface ViewportObserverOptions {
  readonly target: StyleTarget;
  readonly layoutSize: () => { width: number; height: number };
  readonly visualViewport: VisualViewportLike | null;
  /** Notified whenever the metrics change, so the Shell can react to the keyboard. */
  readonly onChange?: (metrics: ViewportMetrics) => void;
}

/**
 * Keeps the CSS variables in step with the visual viewport.
 *
 * A browser without `VisualViewport` degrades to the layout height, which simply means a
 * keyboard inset of zero — the Shell stays usable rather than guessing.
 */
export class ViewportObserver {
  readonly #options: ViewportObserverOptions;
  readonly #listener: () => void;
  #metrics: ViewportMetrics;

  public constructor(options: ViewportObserverOptions) {
    this.#options = options;
    this.#listener = () => this.refresh();
    this.#metrics = this.#compute();

    options.visualViewport?.addEventListener('resize', this.#listener);
    options.visualViewport?.addEventListener('scroll', this.#listener);
  }

  /** The most recently published metrics. */
  public get metrics(): ViewportMetrics {
    return this.#metrics;
  }

  /** Recomputes and republishes the metrics. */
  public refresh(): ViewportMetrics {
    this.#metrics = this.#compute();
    applyViewportMetrics(this.#options.target, this.#metrics);
    this.#options.onChange?.(this.#metrics);
    return this.#metrics;
  }

  /** Stops observing. */
  public dispose(): void {
    this.#options.visualViewport?.removeEventListener('resize', this.#listener);
    this.#options.visualViewport?.removeEventListener('scroll', this.#listener);
  }

  #compute(): ViewportMetrics {
    const { width, height } = this.#options.layoutSize();
    return readViewportMetrics(width, height, this.#options.visualViewport);
  }
}
