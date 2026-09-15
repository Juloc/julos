// MOB-001: the service-worker cache allow/deny matrix from docs/MOBILE_PWA.md section 14.
//
// The rule is expressed as executable policy rather than prose so the worker MOB-002 ships
// cannot drift from it. The policy is deny-by-default: only versioned immutable Shell
// assets and the non-sensitive disconnected document may be persistently cached.

/** What a request is allowed to do with the persistent cache. */
export type CacheDecision = 'cache' | 'never-cache';

/** Why a request may or may not be cached, for diagnostics and tests. */
export interface CachePolicyResult {
  readonly decision: CacheDecision;
  readonly reason: string;
}

/**
 * Request shapes that must never be persistently cached, from section 14.
 *
 * Each entry is a prefix of the Shell-relative path.
 */
export const neverCachedPathPrefixes: readonly string[] = Object.freeze([
  // authenticated API responses, antiforgery and authentication responses, operation,
  // session, runtime and display traffic all live under the versioned API prefix
  '/api/',
  // realtime transport
  '/hubs/',
  // proxied application responses
  '/webapps/',
  // the registrable worker itself is served no-cache so an update is always discovered
  '/sw.js',
]);

/** Immutable, content-addressed Shell asset directories that may be cached. */
export const immutableAssetPathPrefixes: readonly string[] = Object.freeze([
  '/scripts/',
  '/styles/',
  '/vendor/',
  '/icons/',
]);

/** Documents that may be cached because they contain no user state. */
export const cacheableDocuments: readonly string[] = Object.freeze([
  '/offline.html',
  '/manifest.webmanifest',
]);

/** A request as far as the cache policy is concerned. */
export interface CacheablePathRequest {
  /** Shell-relative path, without origin or query string. */
  readonly path: string;
  /** True when the asset name carries a content hash or an explicit version. */
  readonly versioned: boolean;
  /** True when the response varies by authenticated user. */
  readonly authenticated: boolean;
}

/**
 * Decides whether a response may enter the persistent cache.
 *
 * Deny wins: a request that matches both an immutable prefix and a never-cached rule is
 * never cached.
 */
export function cachePolicyFor(request: CacheablePathRequest): CachePolicyResult {
  const path = normalize(request.path);

  if (request.authenticated) {
    return { decision: 'never-cache', reason: 'the response varies by authenticated user' };
  }

  for (const prefix of neverCachedPathPrefixes) {
    if (path === prefix || path.startsWith(prefix)) {
      return { decision: 'never-cache', reason: `'${prefix}' is never persistently cached` };
    }
  }

  if (cacheableDocuments.includes(path)) {
    return { decision: 'cache', reason: 'non-sensitive document without user state' };
  }

  for (const prefix of immutableAssetPathPrefixes) {
    if (path.startsWith(prefix)) {
      return request.versioned
        ? { decision: 'cache', reason: 'versioned immutable Shell asset' }
        : {
            decision: 'never-cache',
            reason: 'a Shell asset without a version or integrity identity is not cached',
          };
    }
  }

  return { decision: 'never-cache', reason: 'deny by default' };
}

/**
 * Update handshake messages from section 14.
 *
 * Activation never calls `location.reload()` from the worker: each page flushes a dirty
 * layout with its expected revision and reloads itself, so an update cannot lose data or
 * deadlock several clients.
 */
export const updateMessages = Object.freeze({
  /** Waiting worker to every controlled client. */
  updateReady: 'JULOS_UPDATE_READY',
  /** Client answer carrying its layout state. */
  updateStatus: 'JULOS_UPDATE_STATUS',
  /** User acceptance. */
  activateUpdate: 'JULOS_ACTIVATE_UPDATE',
  /** Client has flushed and is safe to reload. */
  clientReadyToReload: 'JULOS_CLIENT_READY_TO_RELOAD',
});

/** Layout states a client reports during the update handshake. */
export type UpdateLayoutState = 'clean' | 'dirty' | 'conflict' | 'fresh';

/** Whether a client may reload immediately or must flush its layout first. */
export function mayReloadImmediately(state: UpdateLayoutState): boolean {
  return state === 'clean' || state === 'fresh';
}

function normalize(path: string): string {
  const trimmed = path.trim();
  return trimmed.startsWith('/') ? trimmed : `/${trimmed}`;
}
