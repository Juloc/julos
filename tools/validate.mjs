#!/usr/bin/env node
// The single repository validation implementation.
//
// tools/validate.ps1 and tools/validate.sh are thin wrappers around this file, so
// both platforms run exactly the same checks rather than two similar scripts.

import { spawnSync } from 'node:child_process';
import { access } from 'node:fs/promises';
import { join } from 'node:path';

import { findViolations } from './lib/encoding-policy.mjs';
import { findBrokenLinks } from './lib/markdown-links.mjs';
import { repositoryRoot, toRepositoryPath, walkFiles } from './lib/repository.mjs';

const desktopDirectory = join(repositoryRoot, 'src', 'JulOS.Desktop');

/** Outcome helpers. A stage either passes, is skipped for a stated reason, or fails. */
const passed = (detail) => ({ status: 'passed', detail });
const skipped = (reason) => ({ status: 'skipped', detail: reason });
const failed = (detail) => ({ status: 'failed', detail });

/** Runs a command and fails the stage on a non-zero exit code. */
function run(command, args, cwd = repositoryRoot) {
  const result = spawnSync(command, args, { cwd, stdio: 'inherit', shell: true });

  if (result.error) {
    return failed(`'${command}' could not be started: ${result.error.message}`);
  }

  return result.status === 0
    ? passed(`${command} ${args.join(' ')}`)
    : failed(`'${command} ${args.join(' ')}' exited with code ${result.status}`);
}

async function exists(path) {
  try {
    await access(path);
    return true;
  } catch {
    return false;
  }
}

/** Returns the repository-relative paths of files matching a predicate. */
async function findFiles(predicate) {
  const found = [];

  for await (const absolutePath of walkFiles()) {
    if (predicate(absolutePath)) {
      found.push(toRepositoryPath(absolutePath));
    }
  }

  return found;
}

const stages = [
  {
    name: 'policy',
    title: 'Repository encoding policy',
    async run() {
      const violations = await findViolations();

      return violations.length === 0
        ? passed('every file matches decision D012')
        : failed(
            `${violations.length} file(s) violate the encoding policy:\n  ${violations.join('\n  ')}\n` +
              'Run tools/normalize-encoding.mjs to correct them.',
          );
    },
  },
  {
    name: 'restore',
    title: 'Restore .NET dependencies',
    run: () => run('dotnet', ['restore', 'JulOS.slnx']),
  },
  {
    name: 'build',
    title: 'Build the .NET solution',
    run: () => run('dotnet', ['build', 'JulOS.slnx', '--no-restore']),
  },
  {
    name: 'dotnet-test',
    title: 'Run .NET unit and architecture tests',
    run: () => run('dotnet', ['test', '--solution', 'JulOS.slnx', '--no-build']),
  },
  {
    name: 'desktop-install',
    title: 'Install Desktop dependencies',
    async run() {
      if (await exists(join(desktopDirectory, 'node_modules'))) {
        return skipped('node_modules is already present');
      }

      return run('npm', ['ci'], desktopDirectory);
    },
  },
  {
    name: 'desktop-typecheck',
    title: 'Type check the Desktop sources',
    run: () => run('npm', ['run', 'typecheck'], desktopDirectory),
  },
  {
    name: 'desktop-test',
    title: 'Run Desktop logic tests',
    run: () => run('npm', ['test'], desktopDirectory),
  },
  {
    name: 'desktop-build',
    title: 'Build the Desktop production assets',
    run: () => run('npm', ['run', 'build'], desktopDirectory),
  },
  {
    name: 'markdown-links',
    title: 'Validate relative Markdown links',
    async run() {
      const broken = await findBrokenLinks();

      return broken.length === 0
        ? passed('every relative link resolves')
        : failed(`${broken.length} broken link(s):\n  ${broken.join('\n  ')}`);
    },
  },
  {
    name: 'package-manifests',
    title: 'Validate package manifests',
    async run() {
      const manifests = await findFiles((path) => path.endsWith('julos-package.json'));

      return manifests.length === 0
        ? skipped('no package manifest exists yet; the schema is defined by PKG-001')
        : failed(
            `${manifests.length} manifest(s) found but no validator exists yet. Implement this stage with PKG-001:\n  ${manifests.join('\n  ')}`,
          );
    },
  },
  {
    name: 'container-build',
    title: 'Build the container images',
    async run() {
      const dockerfiles = await findFiles((path) => /(^|[\\/])Dockerfile(\.|$)/.test(path));

      return dockerfiles.length === 0
        ? skipped('no Dockerfile exists yet; the deployment stack is FND-005')
        : failed(
            `${dockerfiles.length} Dockerfile(s) found but this stage is not implemented yet. Implement it with FND-005:\n  ${dockerfiles.join('\n  ')}`,
          );
    },
  },
];

function printUsage() {
  console.log('Usage: node tools/validate.mjs [--list] [--stage <name>]...');
  console.log('');
  console.log('Stages:');

  for (const stage of stages) {
    console.log(`  ${stage.name.padEnd(20)} ${stage.title}`);
  }
}

function parseArguments(argv) {
  const requested = [];

  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];

    if (argument === '--list' || argument === '--help') {
      return { action: argument === '--list' ? 'list' : 'help' };
    }

    if (argument === '--stage') {
      const name = argv[++index];

      if (name === undefined) {
        return { action: 'error', message: '--stage requires a stage name.' };
      }

      if (!stages.some((stage) => stage.name === name)) {
        return { action: 'error', message: `Unknown stage '${name}'. Use --list to see the stages.` };
      }

      requested.push(name);
      continue;
    }

    return { action: 'error', message: `Unknown argument '${argument}'.` };
  }

  return { action: 'run', requested };
}

const parsed = parseArguments(process.argv.slice(2));

if (parsed.action === 'error') {
  console.error(parsed.message);
  printUsage();
  process.exit(2);
}

if (parsed.action === 'list' || parsed.action === 'help') {
  printUsage();
  process.exit(0);
}

const selected = parsed.requested.length === 0
  ? stages
  : stages.filter((stage) => parsed.requested.includes(stage.name));

const summary = [];

for (const stage of selected) {
  console.log(`\n=== ${stage.name}: ${stage.title} ===`);

  const outcome = await stage.run();

  summary.push({ name: stage.name, ...outcome });

  if (outcome.status === 'failed') {
    console.error(`\nValidation stage '${stage.name}' failed: ${outcome.detail}`);
    console.error('\nSummary:');

    for (const entry of summary) {
      console.error(`  ${entry.status.padEnd(7)} ${entry.name}`);
    }

    process.exit(1);
  }

  console.log(`${outcome.status}: ${outcome.detail}`);
}

console.log('\nSummary:');

for (const entry of summary) {
  console.log(`  ${entry.status.padEnd(7)} ${entry.name}${entry.status === 'skipped' ? ` (${entry.detail})` : ''}`);
}

console.log('\nValidation succeeded.');
