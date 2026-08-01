// Copies the static Desktop assets next to the compiled ES modules.
// The TypeScript compiler emits scripts only, and JulOS deliberately has no bundler.

import { cp, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const projectDirectory = dirname(fileURLToPath(import.meta.url));
const staticDirectory = join(projectDirectory, 'static');
const outputDirectory = join(projectDirectory, 'dist');

await mkdir(outputDirectory, { recursive: true });
await cp(staticDirectory, outputDirectory, { recursive: true });

console.log(`Copied static assets to ${outputDirectory}`);
