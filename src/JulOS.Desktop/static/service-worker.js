// JulOS Shell service worker.
//
// Scope: the whole origin. It exists to make JulOS an installable PWA and to
// show a truthful disconnected document when the Shell is opened offline. It is
// deliberately conservative: it never persistently caches authenticated API
// responses, HTML containing user state, antiforgery/authentication responses,
// secret material, or operation/session/runtime/display traffic. Only versioned
// immutable Shell assets and the static disconnected document are cached.
//
// MOB-002. The full update handshake and layout-flush-gated reload (MOBILE_PWA
// section 14) are layered on once device layouts exist (MOB-004); this worker
// provides installability, offline shell delivery and safe update activation.

const CACHE_VERSION = 'julos-shell-v1';
const OFFLINE_DOCUMENT = '/offline.html';
const PRECACHE = [OFFLINE_DOCUMENT, '/manifest.webmanifest', '/icons/julos.svg', '/icons/julos-maskable.svg'];

// Same-origin directories that only ever hold versioned immutable assets.
const IMMUTABLE_PREFIXES = ['/scripts/', '/styles/', '/vendor/', '/icons/'];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_VERSION).then((cache) => cache.addAll(PRECACHE)),
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const names = await caches.keys();
      await Promise.all(names.filter((name) => name !== CACHE_VERSION).map((name) => caches.delete(name)));
      await self.clients.claim();
    })(),
  );
});

self.addEventListener('message', (event) => {
  // An explicit, user-approved activation from the Shell. The worker never
  // reloads pages itself; each page reloads only after its own layout flush.
  if (event.data && event.data.type === 'JULOS_ACTIVATE_UPDATE') {
    self.skipWaiting();
  }
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') {
    return; // Never intercept or cache mutations.
  }

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) {
    return; // Cross-origin (e.g. proxied apps) is never cached here.
  }

  // Authenticated API, auth and antiforgery traffic is always live, never cached.
  if (url.pathname.startsWith('/api/')) {
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

  // Versioned immutable Shell assets: cache-first, then populate the cache.
  if (IMMUTABLE_PREFIXES.some((prefix) => url.pathname.startsWith(prefix)) || url.pathname === '/manifest.webmanifest') {
    event.respondWith(
      caches.open(CACHE_VERSION).then(async (cache) => {
        const cached = await cache.match(request);
        if (cached) {
          return cached;
        }
        const response = await fetch(request);
        if (response.ok) {
          cache.put(request, response.clone());
        }
        return response;
      }),
    );
  }
});
