// PKG-013: the isolated path for frontend code JulOS does not trust.
//
// A trusted package frontend runs in the Shell realm behind a closed shadow root. That is
// an encapsulation boundary, not a security one — `docs/PACKAGES.md` says so explicitly —
// and it is not enough for code whose publisher this installation does not trust.
//
// Unknown code therefore runs in a sandboxed frame with no `allow-same-origin`, which
// gives it an opaque origin: no JulOS session cookie, no Shell DOM, no same-origin fetch
// against Core, and no access to anything the Shell holds. Everything it may do goes
// through one typed message bridge, and every message is checked here before the Shell
// acts on it. The frame decides nothing; it asks.
//
// This is deliberately not the default application runtime, which `AGENTS.md` forbids. It
// is the path a package takes precisely because it is not trusted.

/** What the Shell will do on behalf of an isolated frontend. */
export type IsolatedRequestKind = 'capability' | 'open-application';

/** One request from an isolated frontend. */
export interface IsolatedRequest {
  readonly kind: IsolatedRequestKind;
  /** Correlates the reply; opaque to the Shell. */
  readonly requestId: string;
  readonly capability?: string;
  readonly operation?: string;
  readonly payload?: unknown;
  readonly applicationId?: string;
  readonly targetId?: string;
}

/** What the Shell is willing to do for one isolated package. */
export interface IsolatedGrants {
  readonly packageId: string;
  /** Capability names the package's manifest declares it requires. */
  readonly capabilities: readonly string[];
}

export interface IsolatedBridgeOptions {
  readonly grants: IsolatedGrants;
  readonly invokeCapability: (name: string, operation: string, payload: unknown) => Promise<unknown>;
  readonly openApplication: (applicationId: string, targetId?: string) => void | Promise<void>;
  readonly onRejected?: (rejection: IsolatedRejection) => void;
}

/** A request the Shell refused, with the stable reason. */
export interface IsolatedRejection {
  readonly packageId: string;
  readonly code: string;
  readonly detail: string;
}

/** Stable isolation errors. */
export const isolationErrorCodes = Object.freeze({
  /** The message did not match the bridge schema. */
  malformed: 'package.bridge_message_invalid',
  /** The package asked for something its manifest does not declare. */
  notGranted: 'package.bridge_not_granted',
  /** The message did not come from the frame the Shell created. */
  foreignSource: 'package.bridge_source_invalid',
});

/** How long an isolated frontend has to load before the Shell gives up on it. */
export const isolatedFrontendLoadTimeoutMs = 10_000;

/**
 * Validates one message from an isolated frontend.
 *
 * Returns the request when it is both well formed and something the package is allowed to
 * ask for. This is the whole authorization surface of the bridge, which is why it refuses
 * by default: anything not explicitly recognised and granted is rejected.
 */
export function readIsolatedRequest(
  message: unknown,
  grants: IsolatedGrants,
): { readonly request: IsolatedRequest } | { readonly rejection: IsolatedRejection } {
  if (typeof message !== 'object' || message === null) {
    return { rejection: reject(grants, isolationErrorCodes.malformed, 'The message is not an object.') };
  }

  const candidate = message as Record<string, unknown>;
  const requestId = candidate['requestId'];
  if (typeof requestId !== 'string' || requestId.length === 0 || requestId.length > 128) {
    return { rejection: reject(grants, isolationErrorCodes.malformed, 'The request identity is invalid.') };
  }

  const kind = candidate['kind'];
  if (kind === 'capability') {
    const capability = candidate['capability'];
    const operation = candidate['operation'];
    if (typeof capability !== 'string' || typeof operation !== 'string') {
      return {
        rejection: reject(grants, isolationErrorCodes.malformed, 'A capability request needs a name and an operation.'),
      };
    }
    if (!grants.capabilities.includes(capability)) {
      // The manifest is the only source of what a package may reach. A frontend cannot
      // widen it by asking, because the Shell checks the manifest and not the message.
      return {
        rejection: reject(
          grants,
          isolationErrorCodes.notGranted,
          `The package does not declare capability '${capability}'.`,
        ),
      };
    }

    return {
      request: {
        kind: 'capability',
        requestId,
        capability,
        operation,
        payload: candidate['payload'],
      },
    };
  }

  if (kind === 'open-application') {
    const applicationId = candidate['applicationId'];
    if (typeof applicationId !== 'string' || applicationId.length === 0) {
      return {
        rejection: reject(grants, isolationErrorCodes.malformed, 'An open request needs an application identity.'),
      };
    }

    const targetId = candidate['targetId'];
    return {
      request: {
        kind: 'open-application',
        requestId,
        applicationId,
        ...(typeof targetId === 'string' ? { targetId } : {}),
      },
    };
  }

  return { rejection: reject(grants, isolationErrorCodes.malformed, 'The request kind is not supported.') };
}

/**
 * The document an isolated frontend is loaded into.
 *
 * It is written as a `srcdoc`, so the frame has no URL of its own on the JulOS origin and
 * nothing can be fetched from it with ambient credentials. The module is imported from the
 * same-origin URL the Shell verified; the frame's opaque origin is what keeps that import
 * from carrying cookies.
 */
export function isolatedFrontendDocument(moduleUrl: string, packageId: string): string {
  const module = JSON.stringify(moduleUrl);
  const identity = JSON.stringify(packageId);
  const origin = moduleOrigin(moduleUrl);
  return `<!DOCTYPE html>
<html><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline' ${origin}; style-src 'unsafe-inline'; img-src data: blob:; connect-src 'none'">
</head><body><div id="surface"></div>
<script type="module">
const packageId = ${identity};
const pending = new Map();
let nextRequest = 0;

function request(message) {
  const requestId = String(++nextRequest);
  return new Promise((resolve, reject) => {
    pending.set(requestId, { resolve, reject });
    parent.postMessage({ ...message, requestId, packageId }, '*');
  });
}

addEventListener('message', (event) => {
  const reply = event.data;
  if (typeof reply !== 'object' || reply === null) return;
  const entry = pending.get(reply.requestId);
  if (entry === undefined) return;
  pending.delete(reply.requestId);
  if (reply.ok === true) entry.resolve(reply.result);
  else entry.reject(new Error(String(reply.code ?? 'package.bridge_failed')));
});

const context = {
  packageId,
  language: ${JSON.stringify('en')},
  theme: ${JSON.stringify('light')},
  invokeCapability: (capability, operation, payload) =>
    request({ kind: 'capability', capability, operation, payload }),
  openApplication: (applicationId, targetId) =>
    request({ kind: 'open-application', applicationId, targetId }),
};

try {
  const module = await import(${module});
  await module.register(context);
  parent.postMessage({ kind: 'ready', packageId }, '*');
} catch (error) {
  parent.postMessage({ kind: 'failed', packageId, detail: String(error) }, '*');
}
</script></body></html>`;
}

/**
 * Creates the sandboxed frame an isolated frontend runs in.
 *
 * `allow-scripts` without `allow-same-origin` is the whole point: the frame can run its
 * own code and can reach nothing of ours. Adding `allow-same-origin` beside
 * `allow-scripts` would let the frame remove its own sandbox, so the two are never
 * combined here.
 */
export function createIsolatedFrame(moduleUrl: string, packageId: string): HTMLIFrameElement {
  const frame = document.createElement('iframe');
  frame.className = 'package-surface package-surface-isolated';
  frame.setAttribute('sandbox', 'allow-scripts');
  frame.setAttribute('referrerpolicy', 'no-referrer');
  frame.setAttribute('loading', 'eager');
  frame.title = packageId;
  frame.srcdoc = isolatedFrontendDocument(moduleUrl, packageId);
  return frame;
}

function reject(grants: IsolatedGrants, code: string, detail: string): IsolatedRejection {
  return { packageId: grants.packageId, code, detail };
}

/**
 * The origin a module may be imported from, for the frame's content policy.
 *
 * Only the origin is placed in the policy, never the path. A path is attacker-shaped
 * input: spaces, quotes or semicolons in it would otherwise add source expressions to
 * the directive and widen exactly the thing the policy is there to narrow. An origin
 * that survived URL parsing cannot contain any of them.
 */
function moduleOrigin(moduleUrl: string): string {
  const base = globalThis.location?.origin ?? 'https://localhost';
  const parsed = new URL(moduleUrl, base);
  if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
    throw new TypeError('A package frontend module is loaded over HTTP or HTTPS.');
  }
  return parsed.origin;
}
