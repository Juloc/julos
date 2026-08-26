/**
 * Registers the JulOS Shell service worker so the Desktop is installable as a
 * PWA and shows a truthful disconnected document when opened offline.
 *
 * MOB-002. Registration is best-effort: a browser without service-worker support
 * (or a non-secure context) simply keeps running as a normal Shell tab. This
 * module never blocks Shell startup and never reloads the page on its own; the
 * layout-flush-gated update handshake (MOBILE_PWA section 14) is added with the
 * device-layout work in MOB-004.
 */
export function registerServiceWorker(navigatorLike: Navigator = navigator): void {
  if (!('serviceWorker' in navigatorLike)) {
    return;
  }

  const register = (): void => {
    // Served by a dedicated server endpoint (uncompressed, no-cache,
    // Service-Worker-Allowed: /) rather than the fingerprinted static-asset
    // pipeline, which a service-worker script registration cannot consume.
    navigatorLike.serviceWorker.register('/sw.js', { scope: '/' }).then(
      (registration) => {
        registration.addEventListener('updatefound', () => {
          const installing = registration.installing;
          if (installing === null) {
            return;
          }
          installing.addEventListener('statechange', () => {
            // A new Shell build finished installing while an old one still
            // controls the page. Announce it; the Shell decides when to apply it.
            if (installing.state === 'installed' && navigatorLike.serviceWorker.controller !== null) {
              globalThis.dispatchEvent(new CustomEvent('julos:update-ready'));
            }
          });
        });
      },
      () => {
        // A failed registration must never break the Shell.
      },
    );
  };

  // Register once the page has loaded so the worker never competes with first
  // paint. When the Shell script runs after load has already fired (a late
  // module evaluation), register immediately instead of waiting for an event
  // that will never arrive.
  if (document.readyState === 'complete') {
    register();
  } else {
    globalThis.addEventListener('load', register, { once: true });
  }
}
