import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  normalizeLanguage,
  shellMessageKeys,
  shellMessages,
  translate,
} from './localization.js';

test('English and German contain every shell message', () => {
  for (const language of ['en', 'de'] as const) {
    assert.deepEqual(Object.keys(shellMessages[language]).sort(), [...shellMessageKeys].sort());
    for (const key of shellMessageKeys) {
      assert.notEqual(shellMessages[language][key].trim(), '');
    }
  }
});

test('language normalization supports German variants and defaults to English', () => {
  assert.equal(normalizeLanguage('de-DE'), 'de');
  assert.equal(normalizeLanguage('de-CH'), 'de');
  assert.equal(normalizeLanguage('en-GB'), 'en');
  assert.equal(normalizeLanguage('fr-FR'), 'en');
  assert.equal(normalizeLanguage(undefined), 'en');
});

test('translation reads the selected language', () => {
  assert.equal(translate('en', 'settings'), 'Settings');
  assert.equal(translate('de', 'settings'), 'Einstellungen');
});
