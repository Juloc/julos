import type { WebProxyConfig } from './shell-api.js';

/** Returns whether the dynamic local web-app browser can be offered to the user. */
export function isDynamicWebAppBrowserAvailable(config: WebProxyConfig): boolean {
  return config.enabled && config.proxyZone.trim().length > 0;
}
