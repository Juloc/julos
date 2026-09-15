import { PwaUpdateController, type LayoutFlushPort, type UpdateAvailability } from './pwa-update.js';

/**
 * Registers the JulOS Shell service worker so the Desktop is installable as a
 * PWA and shows a truthful disconnected document when opened offline, and wires the
 * section-14 update handshake for this page.
 *
 * MOB-002. Registration is best-effort: a browser without service-worker support (or a
 * non-secure context) simply keeps running as a normal Shell tab. This module never blocks
 * Shell startup and never reloads the page on its own — the returned controller reloads
 * only after the page's layout is clean or successfully flushed, or after the user
 * explicitly discards this page's pending changes.
 */
export interface ServiceWorkerRegistrationOptions {
  /** Reports and flushes this page's layout. */
  readonly layout: LayoutFlushPort;
  /** Shows the update-available surface. */
  readonly onUpdateAvailable: (availability: UpdateAvailability) => void;
}

/**
 * Registers the worker and returns the update controller once a worker container exists.
 *
 * Returns `null` when the browser has no service-worker support, which is a supported
 * configuration rather than an error.
 */
export function registerServiceWorker(
  options: ServiceWorkerRegistrationOptions,
  navigatorLike: Navigator = navigator,
): PwaUpdateController | null {
  if (!('serviceWorker' in navigatorLike)) {
    return null;
  }

  const controller = new PwaUpdateController({
    messaging: navigatorLike.serviceWorker,
    layout: options.layout,
    onAvailable: options.onUpdateAvailable,
    reload: () => globalThis.location.reload(),
    clientId: crypto.randomUUID(),
  });

  const register = (): void => {
    // Served by a dedicated server endpoint (uncompressed, no-cache,
    // Service-Worker-Allowed: /) rather than the fingerprinted static-asset pipeline,
    // which a service-worker script registration cannot consume.
    navigatorLike.serviceWorker.register('/sw.js', { scope: '/' }).then(
      () => {
        // The waiting worker announces itself with JULOS_UPDATE_READY; the controller
        // created above is already listening, so nothing else is wired here.
      },
      () => {
        // A failed registration must never break the Shell.
      },
    );
  };

  // Register once the page has loaded so the worker never competes with first paint. When
  // the Shell script runs after load has already fired (a late module evaluation),
  // register immediately instead of waiting for an event that will never arrive.
  if (document.readyState === 'complete') {
    register();
  } else {
    globalThis.addEventListener('load', register, { once: true });
  }

  return controller;
}
