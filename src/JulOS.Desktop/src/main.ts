import { installInterfacePlan } from './interface-plan.js';
import { findMissingPlatformFeatures, probeBrowser } from './platform-support.js';
import { registerServiceWorker } from './pwa.js';
import { defineJulOsShell } from './shell.js';

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
  registerServiceWorker();
}
