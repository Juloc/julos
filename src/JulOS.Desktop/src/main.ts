import { installInterfacePlan } from './interface-plan.js';
import { findMissingPlatformFeatures, probeBrowser } from './platform-support.js';
import { registerServiceWorker } from './pwa.js';
import { defineJulOsShell } from './shell.js';
import { ViewportObserver } from './viewport-metrics.js';

/**
 * JulOS Desktop entry module. The static unsupported notice remains outside the
 * shell because it must work when Custom Elements or Shadow DOM are unavailable.
 */
const unsupportedNoticeId = 'unsupported-browser';
const missingFeatures = findMissingPlatformFeatures(probeBrowser(window));

if (missingFeatures.length > 0) {
  const notice = document.getElementById(unsupportedNoticeId);

  if (notice === null) {
    throw new Error(
      `The document is missing the '${unsupportedNoticeId}' element required to report an unsupported browser.`,
    );
  }

  notice.hidden = false;
  notice.dataset['missingFeatures'] = missingFeatures.join(' ');
} else {
  defineJulOsShell();
  installInterfacePlan(document);

  // Publish the dynamic viewport height and software-keyboard inset as CSS variables.
  // Workspace identity is resolved from the layout viewport, so the keyboard changes
  // presentation without reclassifying the workspace.
  const viewport = new ViewportObserver({
    target: document.documentElement,
    layoutSize: () => ({
      width: document.documentElement.clientWidth,
      height: document.documentElement.clientHeight,
    }),
    visualViewport: window.visualViewport,
  });
  viewport.refresh();
  window.addEventListener('resize', () => viewport.refresh());

  registerServiceWorker({
    // Until device layouts exist (MOB-004) the Shell has no dirty writable layout to
    // flush, so every page reports `clean` and reloads immediately once the user accepts.
    // MOB-004 replaces this port with the real layout state and revision-checked flush.
    layout: { state: () => 'clean', flush: async () => true },
    onUpdateAvailable: (availability) => {
      document.documentElement.dataset['julosUpdate'] = availability.decision;
      globalThis.dispatchEvent(
        new CustomEvent('julos:update-available', { detail: availability }),
      );
    },
  });
}
