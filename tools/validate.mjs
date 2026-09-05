#!/usr/bin/env node
// The single repository validation implementation used locally and in CI.

import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { access, readFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';

import { findUnpinnedExtensions, findViolations } from './lib/encoding-policy.mjs';
import { findBrokenLinks } from './lib/markdown-links.mjs';
import { readAndValidatePackageManifest } from './lib/package-manifest.mjs';
import { repositoryRoot, toRepositoryPath, walkFiles } from './lib/repository.mjs';

const desktopDirectory = join(repositoryRoot, 'src', 'JulOS.Desktop');
const remoteFrontendDirectory = join(repositoryRoot, 'packages', 'JulOS.Remote', 'frontend');
const semanticVersion = /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/;

const passed = (detail) => ({ status: 'passed', detail });
const skipped = (reason) => ({ status: 'skipped', detail: reason });
const failed = (detail) => ({ status: 'failed', detail });

async function readRepositoryVersion() {
  return (await readFile(join(repositoryRoot, 'VERSION'), 'utf8')).trim();
}

function needsShell(command) {
  return process.platform === 'win32' && command === 'npm';
}

function run(command, args, cwd = repositoryRoot, env = {}) {
  const result = spawnSync(command, args, {
    cwd,
    stdio: 'inherit',
    shell: needsShell(command),
    env: { ...process.env, ...env },
  });

  if (result.error) {
    return failed(`'${command}' could not be started: ${result.error.message}`);
  }

  return result.status === 0
    ? passed(`${command} ${args.join(' ')}`)
    : failed(`'${command} ${args.join(' ')}' exited with code ${result.status}`);
}

function isDockerAvailable() {
  const result = spawnSync('docker', ['info', '--format', '{{.ServerVersion}}'], {
    cwd: repositoryRoot,
    stdio: 'ignore',
  });
  return !result.error && result.status === 0;
}

async function exists(path) {
  try {
    await access(path);
    return true;
  } catch {
    return false;
  }
}

/** Returns repository-relative paths and always evaluates predicates on that same shape. */
async function findFiles(predicate) {
  const found = [];
  for await (const absolutePath of walkFiles()) {
    const repositoryPath = toRepositoryPath(absolutePath);
    if (predicate(repositoryPath)) {
      found.push(repositoryPath);
    }
  }
  return found;
}

async function validateCommittedPackageManifest(manifestPath) {
  const absoluteManifestPath = join(repositoryRoot, manifestPath);
  const errors = await readAndValidatePackageManifest(absoluteManifestPath);
  if (errors.length > 0) {
    return errors;
  }

  const text = (await readFile(absoluteManifestPath, 'utf8')).replace(/^\uFEFF/, '');
  const manifest = JSON.parse(text);
  if (manifest.Frontend === null) {
    return errors;
  }

  const modulePath = join(dirname(absoluteManifestPath), manifest.Frontend.ModulePath);
  if (!(await exists(modulePath))) {
    errors.push(`${manifestPath}: Frontend.ModulePath does not exist`);
    return errors;
  }

  const moduleBytes = await readFile(modulePath);
  const actualDigest = createHash('sha256').update(moduleBytes).digest('hex');
  if (actualDigest !== manifest.Frontend.Sha256) {
    errors.push(
      `${manifestPath}: Frontend.Sha256 is '${manifest.Frontend.Sha256}' but ${manifest.Frontend.ModulePath} is '${actualDigest}'`,
    );
  }
  return errors;
}

const stages = [
  {
    name: 'policy',
    title: 'Repository encoding policy',
    async run() {
      const unpinned = await findUnpinnedExtensions();
      if (unpinned.length > 0) {
        return failed(
          'git would check these out with the platform line ending, so the policy would '
            + `hold on one operating system and fail on another:\n  ${unpinned.join('\n  ')}`,
        );
      }

      const violations = await findViolations();
      return violations.length === 0
        ? passed('every file matches decision D012')
        : failed(
            `${violations.length} file(s) violate the encoding policy:\n  ${violations.join('\n  ')}\n`
              + 'Run tools/normalize-encoding.mjs to correct them.',
          );
    },
  },
  {
    name: 'version',
    title: 'Verify the single version source',
    async run() {
      const version = await readRepositoryVersion();
      if (!semanticVersion.test(version)) {
        return failed(`VERSION contains '${version}', which is not a semantic version.`);
      }

      const query = spawnSync(
        'dotnet',
        ['msbuild', 'src/JulOS.Server/JulOS.Server.csproj', '-getProperty:Version'],
        { cwd: repositoryRoot, encoding: 'utf8' },
      );
      if (query.status !== 0) {
        return failed(`The project version could not be read: ${query.stderr?.trim() ?? 'unknown error'}`);
      }

      const projectVersion = query.stdout.trim();
      return projectVersion === version
        ? passed(`VERSION and the built assemblies both report ${version}`)
        : failed(`VERSION says '${version}' but the project builds as '${projectVersion}'.`);
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
    run: () => run('dotnet', ['test', '--solution', 'JulOS.slnx', '--no-build', '--no-restore']),
  },
  {
    name: 'server-smoke',
    title: 'Real-host routing smoke test',
    run: () => run('node', ['tools/smoke-server.mjs']),
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
    name: 'remote-frontend-install',
    title: 'Install Remote frontend build dependencies',
    async run() {
      if (await exists(join(remoteFrontendDirectory, 'node_modules'))) {
        return skipped('node_modules is already present');
      }
      return run('npm', ['ci'], remoteFrontendDirectory);
    },
  },
  {
    name: 'remote-frontend-build',
    title: 'Build the Remote package frontend',
    run: () => run('npm', ['run', 'build'], remoteFrontendDirectory),
  },
  {
    name: 'remote-frontend-test',
    title: 'Run Remote package frontend tests',
    run: () => run('npm', ['test'], remoteFrontendDirectory),
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
    title: 'Validate package manifests and frontend integrity',
    async run() {
      const fixtureRoot = join(repositoryRoot, 'tests', 'fixtures', 'package-manifests');
      const validErrors = await readAndValidatePackageManifest(join(fixtureRoot, 'valid.json'));
      if (validErrors.length > 0) {
        return failed(`the valid manifest fixture failed:\n  ${validErrors.join('\n  ')}`);
      }

      const unsupportedErrors = await readAndValidatePackageManifest(
        join(fixtureRoot, 'unsupported-schema.json'),
      );
      if (!unsupportedErrors.some((error) => error.includes('unsupported SchemaVersion'))) {
        return failed('the unsupported schema fixture was not rejected with a clear schema-version error');
      }

      const manifests = await findFiles(
        (path) => path.startsWith('packages/') && path.endsWith('/manifest.json'),
      );
      if (manifests.length === 0) {
        return failed('packages exist but no package manifest was discovered');
      }

      const manifestErrors = [];
      for (const manifest of manifests) {
        manifestErrors.push(...await validateCommittedPackageManifest(manifest));
      }

      return manifestErrors.length === 0
        ? passed(`${manifests.length} package manifest(s), frontend digests and compatibility fixtures validate`)
        : failed(`${manifestErrors.length} package manifest error(s):\n  ${manifestErrors.join('\n  ')}`);
    },
  },
  {
    name: 'container-build',
    title: 'Validate the Compose stack and build the container images',
    async run() {
      const dockerfiles = await findFiles((path) => /(^|\/)Dockerfile(?:\.|$)/.test(path));
      if (dockerfiles.length === 0) {
        return skipped('no Dockerfile exists yet');
      }
      if (!isDockerAvailable()) {
        return skipped('no reachable container runtime; the images are built by continuous integration');
      }

      const composeCheck = run(
        'docker',
        ['compose', '--file', 'deploy/compose/compose.yaml', 'config', '--quiet'],
        repositoryRoot,
        {
          JULOS_POSTGRES_PASSWORD: 'validation-placeholder',
          JULOS_SECRET_KEY_RING_PATH: './secret-keys',
        },
      );
      if (composeCheck.status === 'failed') {
        return composeCheck;
      }

      const version = await readRepositoryVersion();
      for (const dockerfile of dockerfiles) {
        const tag = `julos-validate/${dockerfile.replaceAll('/', '-').toLowerCase()}`;
        const build = run('docker', [
          'build',
          '--file', dockerfile,
          '--build-arg', `JULOS_VERSION=${version}`,
          '--tag', tag,
          '.',
        ]);
        if (build.status === 'failed') {
          return build;
        }
      }

      return passed(`compose configuration and ${dockerfiles.length} image(s) build`);
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
