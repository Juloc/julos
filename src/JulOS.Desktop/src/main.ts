import { findMissingPlatformFeatures, probeBrowser } from './platform-support.js';

/**
 * JulOS Desktop entry module.
 *
 * The shell surface, window manager and package host are added by the DESK work
 * items. Startup currently verifies the browser platform and reveals the static
 * bilingual notice in the document when a required feature is missing, because a
 * shell that cannot host Custom Elements must say so rather than render nothing.
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
}
