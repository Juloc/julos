#!/usr/bin/env node
// Rewrites every repository file into the encoding required by decision D012.
//
// The 'policy' stage of tools/validate.mjs reports violations; this corrects them.

import { fixViolations } from './lib/encoding-policy.mjs';

const fixed = await fixViolations();

if (fixed.length === 0) {
  console.log('Every file already matches the encoding policy.');
} else {
  console.log(`Corrected ${fixed.length} file(s):`);

  for (const path of fixed) {
    console.log(`  ${path}`);
  }
}
