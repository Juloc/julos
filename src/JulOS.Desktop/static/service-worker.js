// JulOS Shell service worker.
//
// Scope: the whole origin. It makes JulOS an installable PWA and shows a truthful
// disconnected document when the Shell is opened offline.
//
// The cache policy is deny-by-default and mirrors docs/MOBILE_PWA.md section 14:
// only versioned immutable Shell assets and the static disconnected document are
// persistently cached. Authenticated API responses, HTML containing user state,
// antiforgery and authentication responses, secret material, operation, session,
// runtime and display traffic, and proxied application responses never are.
//
// `__JULOS_BUILD_ID__` is replaced by build.mjs with the repository VERSION. The cache
// name therefore changes with every release, which is what makes the activate handler
// actually evict the previous build's immutable assets.

const BUILD_ID = '__JULOS_BUILD_ID__';
const CACHE_VERSION = `julos-shell-${BUILD_ID}`;
const OFFLINE_DOCUMENT = '/offline.html';
const PRECACHE = [
  OFFLINE_DOCUMENT,
  '/manifest.webmanifest',
  '/icons/julos.svg',
  '/icons/julos-maskable.svg',
];

// Same-origin directories that only ever hold versioned immutable assets.
const IMMUTABLE_PREFIXES = ['/scripts/', '/styles/', '/vendor/', '/icons/'];

// Prefixes that must never enter the persistent cache, whatever else matches.
const NEVER_CACHED_PREFIXES = ['/api/', '/hubs/', '/webapps/', '/sw.js'];

self.addEventListener('install', (event) => {
  event.waitUntil((async () => {
    const cache = await caches.open(CACHE_VERSION);
    await cache.addAll(PRECACHE);

    // Installed while an older worker still controls pages: announce the update instead
    // of taking over. Activation is driven by the Shell, never by the worker, so an
    // update can never discard a page's unsaved presentation state.
    const clients = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const client of clients) {
      client.postMessage({ type: 'JULOS_UPDATE_READY', buildId: BUILD_ID });
    }
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const names = await caches.keys();
      await Promise.all(
        names.filter((name) => name !== CACHE_VERSION).map((name) => caches.delete(name)),
      );
      await self.clients.claim();
    })(),
  );
});

self.addEventListener('message', (event) => {
  const data = event.data;
  if (!data || typeof data.type !== 'string') {
    return;
  }

  switch (data.type) {
    case 'JULOS_UPDATE_STATUS':
      // A client reported its layout state. The worker records nothing and decides
      // nothing: the Shell owns whether and when each page reloads.
      break;

    case 'JULOS_ACTIVATE_UPDATE':
      // Explicit, user-approved activation. The worker may change the controller but
      // never calls location.reload() on a page.
      if (data.buildId === BUILD_ID) {
        self.skipWaiting();
      }
      break;

    case 'JULOS_CLIENT_READY_TO_RELOAD':
      // Acknowledged for symmetry; the page reloads itself once it has flushed.
      break;

    default:
      break;
  }
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') {
    return; // Never intercept or cache mutations.
  }

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) {
    return; // Cross-origin, including proxied applications, is never cached here.
  }

  if (NEVER_CACHED_PREFIXES.some((prefix) => url.pathname.startsWith(prefix))) {
    return;
  }

  // Navigations: serve the live Shell when online; fall back to the honest
  // disconnected document when the network is unreachable.
  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request).catch(async () => {
        const cache = await caches.open(CACHE_VERSION);
        return (await cache.match(OFFLINE_DOCUMENT)) ?? Response.error();
      }),
    );
    return;
  }

  const cacheable = IMMUTABLE_PREFIXES.some((prefix) => url.pathname.startsWith(prefix))
    || url.pathname === '/manifest.webmanifest'
    || url.pathname === OFFLINE_DOCUMENT;

  if (!cacheable) {
    return;
  }

  event.respondWith(
    caches.open(CACHE_VERSION).then(async (cache) => {
      const cached = await cache.match(request);
      if (cached) {
        return cached;
      }
      const response = await fetch(request);
      // Only a same-origin success is stored; an opaque or error response would poison
      // the cache for the lifetime of this build.
      if (response.ok && response.type === 'basic') {
        cache.put(request, response.clone());
      }
      return response;
    }),
  );
});
