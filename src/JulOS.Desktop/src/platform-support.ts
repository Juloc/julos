/**
 * Browser platform features the JulOS Desktop depends on.
 *
 * The Desktop hosts package applications as Custom Elements with Shadow DOM and
 * applies themes through CSS custom properties. Without these the shell cannot
 * render correctly, so startup reports the missing feature instead of failing
 * later in an unclear way.
 */
export const requiredPlatformFeatures = [
  'customElements',
  'shadowDom',
  'cssCustomProperties',
] as const;

export type PlatformFeature = (typeof requiredPlatformFeatures)[number];

/** The parts of the browser environment the detection reads. */
export interface PlatformProbe {
  readonly hasCustomElements: boolean;
  readonly hasShadowDom: boolean;
  readonly hasCssCustomProperties: boolean;
}

/** Returns the required features the probed environment does not provide. */
export function findMissingPlatformFeatures(probe: PlatformProbe): readonly PlatformFeature[] {
  const missing: PlatformFeature[] = [];

  if (!probe.hasCustomElements) {
    missing.push('customElements');
  }

  if (!probe.hasShadowDom) {
    missing.push('shadowDom');
  }

  if (!probe.hasCssCustomProperties) {
    missing.push('cssCustomProperties');
  }

  return missing;
}

/**
 * Probes the live browser environment.
 *
 * Each check is defensive because the point of the probe is a browser whose
 * type declarations promise more than the runtime provides.
 */
export function probeBrowser(view: Window & typeof globalThis): PlatformProbe {
  return {
    hasCustomElements: typeof view.customElements?.define === 'function',
    hasShadowDom: typeof view.Element?.prototype?.attachShadow === 'function',
    hasCssCustomProperties:
      typeof view.CSS?.supports === 'function' && view.CSS.supports('--julos-probe', '0'),
  };
}
