// Real-host routing smoke test.
//
// Regression guard for the endpoint route-table bug where a routed MapFallback
// catch-all suppressed sibling parameter routes (package enable/disable/remove
// and update) under a real Kestrel host. The in-memory TestServer used by the
// WebApplicationFactory integration tests did NOT reproduce it, so this boots
// the real published entry point over HTTP and asserts those routes are
// reachable, while a genuinely unknown route still returns the JulOS 404.
//
// Run after the `build` stage. Exits non-zero on the first failed assertion.

import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomBytes } from 'node:crypto';

const port = 8577;
const baseUrl = `http://127.0.0.1:${port}`;
const password = 'Smoke-Test-Password-42!';

function fail(message) {
  console.error(`Server smoke test failed: ${message}`);
  process.exitCode = 1;
}

async function waitForReady(deadlineMs) {
  while (Date.now() < deadlineMs) {
    try {
      const response = await fetch(`${baseUrl}/api/v1/auth/status`);
      if (response.ok) {
        return true;
      }
    } catch {
      // not listening yet
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  return false;
}

function cookiesFrom(response, jar) {
  const setCookie = response.headers.getSetCookie?.() ?? [];
  for (const value of setCookie) {
    const [pair] = value.split(';', 1);
    const index = pair.indexOf('=');
    if (index > 0) {
      jar.set(pair.slice(0, index).trim(), pair.slice(index + 1).trim());
    }
  }
}

function cookieHeader(jar) {
  return [...jar.entries()].map(([name, value]) => `${name}=${value}`).join('; ');
}

async function codeOf(response) {
  try {
    return (await response.json()).code ?? '';
  } catch {
    return '';
  }
}

const workDirectory = await mkdtemp(join(tmpdir(), 'julos-smoke-'));
const keyRingPath = join(workDirectory, 'keys');
await mkdir(keyRingPath, { recursive: true });
await writeFile(join(keyRingPath, 'primary.key'), randomBytes(32).toString('base64'));

const server = spawn(
  'dotnet',
  [
    'run',
    '--project',
    'src/JulOS.Server',
    '--no-build',
    '--no-launch-profile',
    '--urls',
    baseUrl,
  ],
  {
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Production',
      'Database__Provider': 'sqlite',
      'ConnectionStrings__CoreDatabase': `Data Source=${join(workDirectory, 'core.db')};Cache=Shared`,
      'Packages__Root': join(workDirectory, 'packages'),
      'DataProtection__KeyRingPath': join(workDirectory, 'data-protection'),
      'Secrets__ActiveKeyId': 'primary',
      'Secrets__KeyRingPath': keyRingPath,
    },
    stdio: ['ignore', 'inherit', 'inherit'],
  });

try {
  if (!(await waitForReady(Date.now() + 90_000))) {
    fail('the server did not become ready within 90 seconds.');
  } else {
    const jar = new Map();

    const setup = await fetch(`${baseUrl}/api/v1/auth/setup`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userName: 'admin', displayName: 'Administrator', password }),
    });
    cookiesFrom(setup, jar);
    if (setup.status !== 201) {
      fail(`administrator setup returned ${setup.status}.`);
    }

    const antiforgery = await fetch(`${baseUrl}/api/v1/auth/antiforgery`, {
      headers: { Cookie: cookieHeader(jar) },
    });
    cookiesFrom(antiforgery, jar);
    const token = await antiforgery.json();
    const authHeaders = {
      'Content-Type': 'application/json',
      Cookie: cookieHeader(jar),
      [token.headerName]: token.token,
    };

    // The package action routes must be reachable. Without an installed package
    // the handler answers package.not_found; the bug produced request.not_found
    // because the route was missing from the table entirely.
    const checks = [
      ['DELETE', '/api/v1/packages/smoke-regression', '{"revision":1,"deletePackageData":true}'],
      ['POST', '/api/v1/packages/smoke-regression/enable', '{"revision":1}'],
      ['POST', '/api/v1/packages/smoke-regression/disable', '{"revision":1}'],
    ];
    for (const [method, path, body] of checks) {
      const response = await fetch(`${baseUrl}${path}`, { method, headers: authHeaders, body });
      const code = await codeOf(response);
      if (code === 'request.not_found') {
        fail(`${method} ${path} is not routed (code=request.not_found); the route table dropped it.`);
      } else {
        console.log(`ok: ${method} ${path} is routed (code=${code || response.status}).`);
      }
    }

    // A genuinely unknown route must still return the JulOS not-found problem.
    const unknown = await fetch(`${baseUrl}/api/v1/this-route-does-not-exist`, {
      headers: { Cookie: cookieHeader(jar) },
    });
    const unknownCode = await codeOf(unknown);
    if (unknown.status !== 404 || unknownCode !== 'request.not_found') {
      fail(`unknown route returned ${unknown.status}/${unknownCode}, expected 404/request.not_found.`);
    } else {
      console.log('ok: unknown route returns 404 request.not_found.');
    }

    if (!process.exitCode) {
      console.log('Server smoke test passed.');
    }
  }
} finally {
  server.kill('SIGINT');
  await new Promise((resolve) => setTimeout(resolve, 1000));
  if (!server.killed) {
    server.kill('SIGKILL');
  }
  await rm(workDirectory, { recursive: true, force: true });
}
