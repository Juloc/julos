// End-to-end smoke test for the Adaptive Browser server path.
//
// It uses the images produced by the repository container-build stage (and builds
// them itself when invoked standalone), then verifies the package-owned provider,
// authenticated browser stream, bounded input protocol, real Chromium rendering,
// SwiftShader WebGL baseline and terminal provider failure behavior.

import { randomBytes, randomUUID } from 'node:crypto';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';

const streamProtocol = 'julos-browser-stream.v1';
const runtimeImage = 'julos-validate/packages-julos.adaptivebrowser-runtime-dockerfile';
const providerImage = 'julos-validate/packages-julos.adaptivebrowser-provider-dockerfile';
const suffix = `${process.pid}-${randomBytes(4).toString('hex')}`;
const networkName = `julos-adaptive-browser-smoke-${suffix}`;
const runtimeName = `julos-adaptive-browser-runtime-${suffix}`;
const providerName = `julos-adaptive-browser-provider-${suffix}`;
const unavailableProviderName = `julos-adaptive-browser-unavailable-${suffix}`;
const streamToken = randomBytes(32).toString('base64url');
const callbackToken = randomBytes(32).toString('base64url');
const callbacks = [];
let server;
let browserSocket;

function fail(message) {
  throw new Error(`Adaptive Browser smoke test failed: ${message}`);
}

function dockerCapture(args, label, allowFailure = false) {
  const result = spawnSync('docker', args, {
    cwd: process.cwd(),
    encoding: 'utf8',
    maxBuffer: 4 * 1024 * 1024,
  });
  if (result.error) {
    if (allowFailure) return null;
    fail(`${label} could not start: ${result.error.message}`);
  }
  if (result.status !== 0) {
    if (allowFailure) return null;
    fail(`${label} failed: ${(result.stderr || result.stdout || '').trim()}`);
  }
  return result.stdout.trim();
}

function dockerInherit(args, label) {
  const result = spawnSync('docker', args, { cwd: process.cwd(), stdio: 'inherit' });
  if (result.error || result.status !== 0) {
    fail(`${label} failed.`);
  }
}

function imageExists(image) {
  return dockerCapture(['image', 'inspect', image], `inspect ${image}`, true) !== null;
}

async function ensureImages() {
  const version = (await readFile('VERSION', 'utf8')).trim();
  if (!imageExists(runtimeImage)) {
    dockerInherit([
      'build', '--file', 'packages/JulOS.AdaptiveBrowser/runtime/Dockerfile',
      '--build-arg', `JULOS_VERSION=${version}`, '--tag', runtimeImage, '.',
    ], 'Adaptive Browser runtime build');
  }
  if (!imageExists(providerImage)) {
    dockerInherit([
      'build', '--file', 'packages/JulOS.AdaptiveBrowser/provider/Dockerfile',
      '--build-arg', `JULOS_VERSION=${version}`, '--tag', providerImage, '.',
    ], 'Adaptive Browser provider build');
  }
}

function wait(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

async function waitFor(predicate, timeoutMilliseconds, description) {
  const deadline = Date.now() + timeoutMilliseconds;
  while (Date.now() < deadline) {
    const value = await predicate();
    if (value) return value;
    await wait(100);
  }
  fail(`timed out waiting for ${description}.`);
}

function listen(httpServer) {
  return new Promise((resolve, reject) => {
    httpServer.once('error', reject);
    httpServer.listen(0, '0.0.0.0', () => {
      const address = httpServer.address();
      if (!address || typeof address === 'string') {
        reject(new Error('HTTP smoke server did not expose a TCP port.'));
        return;
      }
      resolve(address.port);
    });
  });
}

function closeServer(httpServer) {
  return new Promise((resolve) => httpServer.close(resolve));
}

function startHostServer() {
  return createServer((request, response) => {
    if (request.method === 'GET' && request.url === '/page') {
      response.writeHead(200, {
        'content-type': 'text/html; charset=utf-8',
        'cache-control': 'no-store',
      });
      response.end(`<!doctype html>
<meta charset="utf-8">
<title>starting</title>
<body>JulOS Adaptive Browser smoke</body>
<script>
  const canvas = document.createElement('canvas');
  const gl = canvas.getContext('webgl');
  document.title = 'viewport:' + innerWidth + 'x' + innerHeight + '@' + devicePixelRatio + ';webgl:' + (gl ? 'yes' : 'no');
</script>`);
      return;
    }

    if (request.method === 'POST' && request.url === '/callback') {
      if (request.headers['x-julos-remote-token'] !== callbackToken) {
        response.writeHead(401).end();
        return;
      }
      let body = '';
      request.setEncoding('utf8');
      request.on('data', (chunk) => {
        body += chunk;
        if (body.length > 64 * 1024) request.destroy();
      });
      request.on('end', () => {
        try {
          callbacks.push(JSON.parse(body));
          response.writeHead(204).end();
        } catch {
          response.writeHead(400).end();
        }
      });
      return;
    }

    response.writeHead(404).end();
  });
}

function mappedPort(containerName, containerPort) {
  const output = dockerCapture(
    ['port', containerName, `${containerPort}/tcp`],
    `read ${containerName} mapped port`,
  );
  const line = output.split(/\r?\n/u).find((entry) => entry.startsWith('127.0.0.1:'));
  if (!line) fail(`${containerName} has no loopback port mapping for ${containerPort}.`);
  const value = Number(line.slice(line.lastIndexOf(':') + 1));
  if (!Number.isInteger(value) || value < 1 || value > 65535) {
    fail(`${containerName} exposed an invalid host port.`);
  }
  return value;
}

async function waitForHealth(port) {
  await waitFor(async () => {
    try {
      const response = await fetch(`http://127.0.0.1:${port}/health`, { signal: AbortSignal.timeout(1000) });
      return response.ok;
    } catch {
      return false;
    }
  }, 30000, `runtime health on port ${port}`);
}

function expectWebSocketRejected(url, protocol, description) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url, protocol);
    let settled = false;
    const timer = setTimeout(() => {
      if (!settled) {
        settled = true;
        socket.close();
        reject(new Error(`Adaptive Browser smoke test failed: ${description} did not finish.`));
      }
    }, 5000);
    socket.addEventListener('open', () => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      socket.close();
      reject(new Error(`Adaptive Browser smoke test failed: ${description} was accepted.`));
    });
    const rejected = () => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      resolve();
    };
    socket.addEventListener('error', rejected);
    socket.addEventListener('close', rejected);
  });
}

function openWebSocket(url, protocol) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url, protocol);
    const timer = setTimeout(() => {
      socket.close();
      reject(new Error('Adaptive Browser WebSocket connection timed out.'));
    }, 10000);
    socket.addEventListener('open', () => {
      clearTimeout(timer);
      resolve(socket);
    }, { once: true });
    socket.addEventListener('error', () => {
      clearTimeout(timer);
      reject(new Error('Adaptive Browser WebSocket connection failed.'));
    }, { once: true });
  });
}

function createMessageQueue(socket) {
  const entries = [];
  const waiters = [];
  socket.binaryType = 'arraybuffer';

  function deliver(entry) {
    const waiterIndex = waiters.findIndex((waiter) => waiter.predicate(entry));
    if (waiterIndex >= 0) {
      const [waiter] = waiters.splice(waiterIndex, 1);
      clearTimeout(waiter.timer);
      waiter.resolve(entry);
      return;
    }
    entries.push(entry);
  }

  socket.addEventListener('message', (event) => {
    if (typeof event.data === 'string') {
      try {
        deliver({ kind: 'json', value: JSON.parse(event.data) });
      } catch {
        deliver({ kind: 'text', value: event.data });
      }
      return;
    }
    if (event.data instanceof ArrayBuffer) {
      deliver({ kind: 'binary', byteLength: event.data.byteLength });
      return;
    }
    deliver({ kind: 'binary', byteLength: Number(event.data?.size ?? 0) });
  });

  return {
    clear() {
      entries.length = 0;
    },
    waitFor(predicate, timeoutMilliseconds, description) {
      const existingIndex = entries.findIndex(predicate);
      if (existingIndex >= 0) {
        return Promise.resolve(entries.splice(existingIndex, 1)[0]);
      }
      return new Promise((resolve, reject) => {
        const waiter = { predicate, resolve, timer: null };
        waiter.timer = setTimeout(() => {
          const index = waiters.indexOf(waiter);
          if (index >= 0) waiters.splice(index, 1);
          reject(new Error(`Adaptive Browser smoke test failed: timed out waiting for ${description}.`));
        }, timeoutMilliseconds);
        waiters.push(waiter);
      });
    },
  };
}

function providerEnvironment(sessionId, hostPort, targetHost, targetPort) {
  return [
    '-e', `JULOS_REMOTE_SESSION_ID=${sessionId}`,
    '-e', `JULOS_REMOTE_TARGET_HOST=${targetHost}`,
    '-e', `JULOS_REMOTE_TARGET_PORT=${targetPort}`,
    '-e', `JULOS_REMOTE_CALLBACK_ENDPOINT=http://host.docker.internal:${hostPort}/callback`,
    '-e', `JULOS_REMOTE_CALLBACK_TOKEN=${callbackToken}`,
    '-e', 'JULOS_REMOTE_EXPECTED_REVISION=1',
    '-e', `JULOS_REMOTE_TARGET_CREDENTIAL=${Buffer.from(streamToken, 'utf8').toString('base64')}`,
  ];
}

async function main() {
  if (typeof WebSocket !== 'function') {
    fail('Node.js does not provide the WebSocket client required by this smoke test.');
  }
  await ensureImages();

  server = startHostServer();
  const hostPort = await listen(server);
  dockerCapture(['network', 'create', networkName], 'create smoke network');

  dockerCapture([
    'create', '--name', runtimeName,
    '--network', networkName,
    '--add-host', 'host.docker.internal:host-gateway',
    '--publish', '127.0.0.1::8080',
    '-e', `JULOS_BROWSER_STREAM_TOKEN=${streamToken}`,
    runtimeImage,
  ], 'create Adaptive Browser runtime');

  const sessionId = randomUUID();
  dockerCapture([
    'run', '--detach', '--name', providerName,
    '--network', networkName,
    '--add-host', 'host.docker.internal:host-gateway',
    '--publish', '127.0.0.1::8081',
    ...providerEnvironment(sessionId, hostPort, runtimeName, 8080),
    providerImage,
  ], 'start Adaptive Browser provider');

  await wait(750);
  if (callbacks.some((entry) => entry.sessionId === sessionId)) {
    fail('provider reported a terminal event before the runtime became ready.');
  }

  dockerCapture(['start', runtimeName], 'start Adaptive Browser runtime');
  const runtimePort = mappedPort(runtimeName, 8080);
  await waitForHealth(runtimePort);
  const connected = await waitFor(
    () => callbacks.find((entry) => entry.sessionId === sessionId && entry.event === 'connected'),
    10000,
    'provider connected callback',
  );
  if (connected.expectedRevision !== 1) {
    fail('provider connected callback did not preserve the expected revision.');
  }

  await expectWebSocketRejected(
    `ws://127.0.0.1:${runtimePort}/stream`,
    streamProtocol,
    'runtime stream without bearer authentication',
  );
  await expectWebSocketRejected(
    `ws://127.0.0.1:${runtimePort}/stream`,
    'invalid-browser-stream.v1',
    'runtime stream with an invalid subprotocol',
  );

  const providerPort = mappedPort(providerName, 8081);
  await expectWebSocketRejected(
    `ws://127.0.0.1:${providerPort}/`,
    'invalid-browser-stream.v1',
    'provider stream with an invalid subprotocol',
  );

  browserSocket = await openWebSocket(`ws://127.0.0.1:${providerPort}/`, streamProtocol);
  const messages = createMessageQueue(browserSocket);

  browserSocket.send(JSON.stringify({ type: 'Runtime.evaluate', expression: '1+1' }));
  await messages.waitFor(
    (entry) => entry.kind === 'json' && entry.value?.type === 'error'
      && entry.value?.code === 'adaptive-browser.command_invalid',
    5000,
    'raw CDP command rejection',
  );

  browserSocket.send(JSON.stringify({
    type: 'resize', width: 1, height: 999999, deviceScaleFactor: 100,
  }));
  browserSocket.send(JSON.stringify({
    type: 'pointer', kind: 'down', button: 'left', buttons: 1, x: -1, y: 10,
  }));
  await messages.waitFor(
    (entry) => entry.kind === 'json' && entry.value?.type === 'error'
      && entry.value?.code === 'adaptive-browser.command_invalid',
    5000,
    'out-of-viewport pointer rejection',
  );

  browserSocket.send(JSON.stringify({
    type: 'key', kind: 'invalid', key: 'a', code: 'KeyA', text: 'a', modifiers: 0,
  }));
  await messages.waitFor(
    (entry) => entry.kind === 'json' && entry.value?.type === 'error'
      && entry.value?.code === 'adaptive-browser.command_invalid',
    5000,
    'invalid keyboard event rejection',
  );

  messages.clear();
  browserSocket.send(JSON.stringify({
    type: 'navigate', url: `http://host.docker.internal:${hostPort}/page`,
  }));
  const state = await messages.waitFor(
    (entry) => entry.kind === 'json' && entry.value?.type === 'state'
      && typeof entry.value?.title === 'string' && entry.value.title.includes(';webgl:'),
    15000,
    'rendered page state',
  );
  if (state.value.title !== 'viewport:320x2160@3;webgl:yes') {
    fail(`unexpected Chromium/WebGL state: ${state.value.title}`);
  }
  const frame = await messages.waitFor(
    (entry) => entry.kind === 'binary' && entry.byteLength > 0,
    15000,
    'Chromium screencast frame',
  );
  if (frame.byteLength < 100) {
    fail('Chromium screencast frame was unexpectedly small.');
  }

  browserSocket.close();
  browserSocket = null;

  const unavailableSessionId = randomUUID();
  dockerCapture([
    'run', '--detach', '--name', unavailableProviderName,
    '--network', networkName,
    '--add-host', 'host.docker.internal:host-gateway',
    ...providerEnvironment(unavailableSessionId, hostPort, '127.0.0.1', 65534),
    providerImage,
  ], 'start unavailable-target provider');
  const failed = await waitFor(
    () => callbacks.find((entry) => entry.sessionId === unavailableSessionId && entry.event === 'failed'),
    25000,
    'provider target-unavailable callback',
  );
  if (failed.failureCode !== 'remote.provider_target_unavailable') {
    fail(`unexpected provider failure code: ${failed.failureCode ?? '<missing>'}`);
  }
  if (callbacks.some((entry) => entry.sessionId === unavailableSessionId && entry.event === 'connected')) {
    fail('unavailable provider incorrectly reported connected.');
  }

  console.log('Adaptive Browser container smoke test passed.');
}

try {
  await main();
} finally {
  try { browserSocket?.close(); } catch {}
  for (const container of [unavailableProviderName, providerName, runtimeName]) {
    dockerCapture(['rm', '--force', container], `remove ${container}`, true);
  }
  dockerCapture(['network', 'rm', networkName], 'remove smoke network', true);
  if (server) await closeServer(server);
}
