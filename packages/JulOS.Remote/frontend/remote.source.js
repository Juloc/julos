const displayKind = 'graphical';
const displayContractVersion = '1.0.0';
const terminalStates = new Set(['cancelled', 'disconnected', 'expired', 'failed']);
const defaultPorts = Object.freeze({ rdp: 3389, ssh: 22, vnc: 5900 });
const launchTargetPrefix = 'remote:v1:';

export async function register(context) {
  class JulOsRemoteApp extends HTMLElement {
    launchTarget = null;
    #connected = false;
    #savedTarget = null;
    #temporaryCredential = null;
    #session = null;
    #pollTimer = null;
    #pollAttempt = 0;
    #client = null;
    #tunnel = null;
    #keyboard = null;
    #pointer = null;
    #resizeObserver = null;
    #resizeScheduler = null;

    connectedCallback() {
      if (this.#connected) {
        return;
      }
      this.#connected = true;
      this.#render();
      this.#bindActions();
      this.#applyLaunchTarget();
    }

    disconnectedCallback() {
      this.#connected = false;
      this.#clearPoll();
      this.#stopClient();
      void this.#cancelAndDispose();
    }

    #render() {
      const de = context.language === 'de';
      const shadow = this.attachShadow({ mode: 'open' });
      shadow.innerHTML = `
        <style>
          :host { display: block; min-height: 28rem; color: CanvasText; font: 14px/1.4 system-ui, sans-serif; }
          * { box-sizing: border-box; }
          .layout { display: grid; grid-template-columns: minmax(16rem, 21rem) minmax(0, 1fr); gap: 1rem; min-height: 28rem; }
          form, .viewer { border: 1px solid color-mix(in srgb, CanvasText 16%, transparent); border-radius: .75rem; background: Canvas; }
          form { display: grid; align-content: start; gap: .7rem; padding: 1rem; }
          label { display: grid; gap: .3rem; font-weight: 600; }
          input, select, button { min-height: 2.25rem; border-radius: .4rem; border: 1px solid color-mix(in srgb, CanvasText 24%, transparent); font: inherit; }
          input, select { width: 100%; padding: .35rem .55rem; background: Canvas; color: CanvasText; }
          button { padding: .35rem .75rem; background: ButtonFace; color: ButtonText; cursor: pointer; }
          button:disabled { cursor: default; opacity: .55; }
          .row { display: grid; grid-template-columns: 1fr auto; gap: .5rem; align-items: end; }
          .remember { display: flex; grid-template-columns: none; align-items: center; gap: .5rem; font-weight: 400; }
          .remember input { width: auto; min-height: auto; }
          .actions { display: flex; gap: .5rem; flex-wrap: wrap; }
          .actions button { flex: 1; }
          .viewer { min-width: 0; overflow: hidden; display: grid; grid-template-rows: auto minmax(18rem, 1fr) auto; }
          .toolbar { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; padding: .65rem; border-bottom: 1px solid color-mix(in srgb, CanvasText 12%, transparent); }
          .toolbar .spacer { flex: 1; }
          .keyboard-hint { font-size: .85rem; opacity: .75; }
          .stage { min-width: 0; min-height: 18rem; overflow: hidden; position: relative; background: #111; outline: none; touch-action: none; }
          .stage > div { transform-origin: top left; }
          .status { margin: 0; padding: .65rem; min-height: 2.5rem; border-top: 1px solid color-mix(in srgb, CanvasText 12%, transparent); }
          .status[data-state='error'] { color: #b10e1e; }
          .status[data-state='connected'] { color: #107c10; }
          @media (max-width: 760px) { .layout { grid-template-columns: 1fr; } form { order: 2; } .viewer { min-height: 24rem; } }
        </style>
        <section class="layout">
          <form id="connection-form">
            <strong>${de ? 'Remote-Verbindung' : 'Remote connection'}</strong>
            <label>${de ? 'Name' : 'Name'}<input id="connection-name" autocomplete="off" placeholder="Windows Server" /></label>
            <label>${de ? 'Protokoll' : 'Protocol'}<select id="protocol"><option value="rdp">RDP</option><option value="ssh">SSH</option><option value="vnc">VNC</option></select></label>
            <label>${de ? 'Ziel' : 'Target'}<input id="target" required autocomplete="off" placeholder="192.168.1.10:3389" /></label>
            <label>${de ? 'Benutzer' : 'User'}<input id="user-name" autocomplete="username" /></label>
            <label>${de ? 'Domäne (optional)' : 'Domain (optional)'}<input id="domain" autocomplete="organization" /></label>
            <label>${de ? 'Passwort' : 'Password'}<input id="password" type="password" autocomplete="current-password" /></label>
            <label class="remember"><input id="remember-password" type="checkbox" checked />${de ? 'Passwort verschlüsselt speichern' : 'Store password encrypted'}</label>
            <div class="actions">
              <button id="connect" type="submit">${de ? 'Verbinden' : 'Connect'}</button>
              <button id="save" type="button">${de ? 'Als App speichern' : 'Save as app'}</button>
            </div>
          </form>
          <section class="viewer" aria-label="${de ? 'Remote-Anzeige' : 'Remote display'}">
            <div class="toolbar">
              <button id="reconnect" type="button" disabled>${de ? 'Neu verbinden' : 'Reconnect'}</button>
              <button id="fullscreen" type="button" disabled>${de ? 'Vollbild' : 'Full screen'}</button>
              <span class="keyboard-hint">${de ? 'Tastatur freigeben: Strg+Alt+Umschalt+Esc' : 'Release keyboard: Ctrl+Alt+Shift+Esc'}</span>
              <span class="spacer"></span>
              <button id="disconnect" type="button" disabled>${de ? 'Trennen' : 'Disconnect'}</button>
            </div>
            <div id="stage" class="stage" tabindex="0" aria-label="${de ? 'Interaktive Remote-Anzeige' : 'Interactive remote display'}"></div>
            <p id="status" class="status" role="status">${de ? 'Nicht verbunden' : 'Not connected'}</p>
          </section>
        </section>`;
    }

    #bindActions() {
      this.#required('connection-form').addEventListener('submit', (event) => {
        event.preventDefault();
        void this.#connectFromForm();
      });
      this.#required('save').addEventListener('click', () => void this.#saveConnection());
      this.#required('reconnect').addEventListener('click', () => void this.#resumeSession());
      this.#required('disconnect').addEventListener('click', () => void this.#disconnectSession());
      this.#required('fullscreen').addEventListener('click', () => void this.#enterFullscreen());
    }

    #applyLaunchTarget() {
      const identity = this.launchTarget?.externalIdentity;
      if (typeof identity !== 'string' || !identity.startsWith(launchTargetPrefix)) {
        return;
      }
      try {
        const saved = decodeRemoteLaunchTarget(identity);
        this.#savedTarget = saved;
        this.#required('connection-name').value = this.launchTarget?.displayName ?? '';
        this.#required('protocol').value = saved.protocol;
        this.#required('target').value = formatTarget(saved.host, saved.port, saved.protocol);
        this.#required('user-name').value = saved.userName;
        this.#required('domain').value = saved.domain;
        this.#required('remember-password').checked = saved.secretReferenceId !== null;
        if (saved.secretReferenceId !== null) {
          void this.#createSession(saved.secretReferenceId);
        } else {
          this.#setStatus(context.language === 'de' ? 'Passwort eingeben und verbinden.' : 'Enter the password and connect.');
        }
      } catch {
        this.#setStatus(context.language === 'de' ? 'Gespeicherte Verbindung ist ungültig.' : 'Saved connection is invalid.', 'error');
      }
    }

    async #connectFromForm() {
      let credential = null;
      try {
        const savedReference = this.#savedTarget?.secretReferenceId ?? null;
        const password = this.#required('password').value;
        if (password.length > 0) {
          credential = await this.#createCredential();
          this.#temporaryCredential = credential;
        } else if (savedReference !== null) {
          credential = { secretReferenceId: savedReference };
        } else {
          throw new Error('credential-required');
        }
        await this.#createSession(credential.secretReferenceId);
      } catch {
        if (credential !== null && this.#temporaryCredential === credential) {
          await this.#deleteTemporaryCredential();
        }
        this.#setStatus(
          context.language === 'de' ? 'Passwort fehlt oder Verbindung konnte nicht erstellt werden.' : 'Password is missing or the connection could not be created.',
          'error',
        );
      }
    }

    async #saveConnection() {
      this.#setBusy(true);
      let newlyCreated = null;
      try {
        const settings = readConnectionSettings(this.shadowRoot);
        const remember = this.#required('remember-password').checked;
        const password = this.#required('password').value;
        let secretReferenceId = remember ? this.#savedTarget?.secretReferenceId ?? null : null;

        if (remember && password.length > 0) {
          if (secretReferenceId === null) {
            newlyCreated = await this.#createCredential();
            secretReferenceId = newlyCreated.secretReferenceId;
          } else {
            await this.#rotateCredential(secretReferenceId);
          }
        }
        if (remember && secretReferenceId === null) {
          throw new Error('credential-required');
        }

        const externalIdentity = encodeRemoteLaunchTarget({ ...settings, secretReferenceId });
        const displayName = this.#required('connection-name').value.trim() || `${settings.protocol.toUpperCase()} ${settings.host}`;
        const previous = this.launchTarget;
        const saved = await context.saveLaunchTarget('remote', externalIdentity, displayName);
        if (previous?.launchTargetId && previous.externalIdentity !== externalIdentity) {
          await context.deleteLaunchTarget(previous.launchTargetId);
        }
        if (!remember && this.#savedTarget?.secretReferenceId) {
          await this.#deleteCredential(this.#savedTarget.secretReferenceId);
        }
        this.launchTarget = saved;
        this.#savedTarget = decodeRemoteLaunchTarget(saved.externalIdentity);
        this.#required('connection-name').value = saved.displayName;
        this.#required('password').value = '';
        this.#setStatus(context.language === 'de' ? 'Verbindung als App gespeichert.' : 'Connection saved as app.');
      } catch {
        if (newlyCreated !== null) {
          await this.#deleteCredential(newlyCreated.secretReferenceId).catch(() => {});
        }
        this.#setStatus(context.language === 'de' ? 'Verbindung konnte nicht gespeichert werden.' : 'Connection could not be saved.', 'error');
      } finally {
        this.#setBusy(false);
      }
    }

    async #createCredential() {
      const settings = readConnectionSettings(this.shadowRoot);
      const password = this.#required('password').value;
      if (password.length === 0) {
        throw new Error('credential-required');
      }
      return validateCredentialResponse(await context.invokeCapability('remote.session', 'credential.create', {
        secretValue: JSON.stringify({
          username: settings.userName || null,
          password,
          domain: settings.domain || null,
          privateKey: null,
          passphrase: null,
        }),
      }));
    }

    async #rotateCredential(secretReferenceId) {
      const settings = readConnectionSettings(this.shadowRoot);
      const password = this.#required('password').value;
      if (password.length === 0) {
        return;
      }
      await context.invokeCapability('remote.session', 'credential.rotate', {
        secretReferenceId,
        secretValue: JSON.stringify({
          username: settings.userName || null,
          password,
          domain: settings.domain || null,
          privateKey: null,
          passphrase: null,
        }),
      });
    }

    async #deleteCredential(secretReferenceId) {
      await context.invokeCapability('remote.session', 'credential.delete', { secretReferenceId });
    }

    async #deleteTemporaryCredential() {
      const current = this.#temporaryCredential;
      this.#temporaryCredential = null;
      if (current !== null) {
        await this.#deleteCredential(current.secretReferenceId).catch(() => {});
      }
    }

    async #createSession(secretReferenceId) {
      this.#clearPoll();
      this.#stopClient();
      this.#session = null;
      this.#pollAttempt = 0;
      this.#setBusy(true);
      this.#setStatus(context.language === 'de' ? 'Verbindung wird erstellt …' : 'Creating session …');
      try {
        const settings = readConnectionSettings(this.shadowRoot);
        const now = new Date();
        const session = await context.invokeCapability('remote.session', 'create', {
          operationKey: crypto.randomUUID(),
          protocol: settings.protocol,
          target: { host: settings.host, port: settings.port },
          secretReferenceId,
          profileId: null,
          networkProfileId: null,
          viewport: viewportFrom(this.#required('stage')),
          idleTimeoutSeconds: 1800,
          maximumSessionSeconds: 28800,
          requestedAtUtc: now.toISOString(),
          deadlineUtc: new Date(now.getTime() + 30000).toISOString(),
        });
        await this.#consumeSession(session);
      } finally {
        this.#setBusy(false);
      }
    }

    async #consumeSession(value) {
      const session = validateSessionResponse(value);
      this.#session = session;
      this.#updateButtons();
      if (terminalStates.has(session.state)) {
        this.#clearPoll();
        this.#stopClient();
        await this.#deleteTemporaryCredential();
        this.#setStatus(session.failure?.detail ?? session.state, 'error');
        return;
      }
      if (session.state === 'connected') {
        if (session.display === null || session.display === undefined) {
          await this.#resumeSession();
          return;
        }
        this.#attachDisplay(validateDisplayDescriptor(session.display));
        return;
      }
      this.#setStatus(`${session.state} …`);
      this.#scheduleRead();
    }

    #scheduleRead() {
      this.#clearPoll();
      if (!this.#connected || this.#session === null || this.#pollAttempt >= 30) {
        if (this.#pollAttempt >= 30) {
          this.#setStatus(context.language === 'de' ? 'Die Sitzung ist noch nicht bereit. Erneut verbinden.' : 'The session is not ready yet. Reconnect to continue.', 'error');
        }
        return;
      }
      this.#pollAttempt += 1;
      this.#pollTimer = globalThis.setTimeout(() => void this.#readSession(), 1000);
    }

    async #readSession() {
      if (this.#session === null) {
        return;
      }
      try {
        const session = await context.invokeCapability('remote.session', 'read', { sessionId: this.#session.sessionId });
        await this.#consumeSession(session);
      } catch {
        this.#setStatus(context.language === 'de' ? 'Sitzungsstatus nicht verfügbar.' : 'Session status is unavailable.', 'error');
      }
    }

    async #resumeSession() {
      if (this.#session === null) {
        return;
      }
      this.#clearPoll();
      this.#setStatus(context.language === 'de' ? 'Anzeige wird verbunden …' : 'Connecting display …');
      try {
        const session = await context.invokeCapability('remote.session', 'resume', {
          sessionId: this.#session.sessionId,
          expectedRevision: this.#session.revision,
        });
        await this.#consumeSession(session);
      } catch {
        this.#setStatus(context.language === 'de' ? 'Die Anzeige konnte nicht erneut verbunden werden.' : 'The display could not be reconnected.', 'error');
      }
    }

    async #cancelAndDispose() {
      const session = this.#session;
      if (session !== null && !terminalStates.has(session.state)) {
        try {
          await context.invokeCapability('remote.session', 'cancel', {
            sessionId: session.sessionId,
            operationKey: crypto.randomUUID(),
            expectedRevision: session.revision,
            reason: 'window_closed',
          });
        } catch {}
      }
      await this.#deleteTemporaryCredential();
    }

    async #disconnectSession() {
      if (this.#session === null) {
        return;
      }
      this.#clearPoll();
      try {
        const session = await context.invokeCapability('remote.session', 'disconnect', {
          sessionId: this.#session.sessionId,
          expectedRevision: this.#session.revision,
        });
        this.#session = validateSessionResponse(session);
        this.#stopClient();
        await this.#deleteTemporaryCredential();
        this.#setStatus(context.language === 'de' ? 'Verbindung getrennt' : 'Disconnected');
      } catch {
        this.#setStatus(context.language === 'de' ? 'Trennen fehlgeschlagen.' : 'Disconnect failed.', 'error');
      } finally {
        this.#updateButtons();
      }
    }

    #attachDisplay(descriptor) {
      this.#stopClient();
      const stage = this.#required('stage');
      const endpoint = splitDisplayEndpoint(descriptor.endpoint);
      const tunnel = new Guacamole.WebSocketTunnel(endpoint.tunnelUrl);
      const client = new Guacamole.Client(tunnel);
      const display = client.getDisplay();
      const displayElement = display.getElement();
      stage.replaceChildren(displayElement);
      this.#tunnel = tunnel;
      this.#client = client;
      this.#keyboard = createKeyboardPipeline(Guacamole, stage, client, isCoarsePointer(), () => this.#setStatus(
        context.language === 'de'
          ? 'Tastatur freigegeben. Anzeige anklicken, um sie wieder zu erfassen.'
          : 'Keyboard released. Click the display to capture it again.',
      ));
      this.#pointer = createPointerPipeline(Guacamole, displayElement, client, isCoarsePointer());
      stage.onpointerdown = () => stage.focus();
      client.onstatechange = (state) => {
        if (state === Guacamole.Client.State.CONNECTED) {
          this.#setStatus(context.language === 'de' ? 'Verbunden' : 'Connected', 'connected');
          this.#required('fullscreen').disabled = false;
          this.#required('reconnect').disabled = true;
        } else if (state === Guacamole.Client.State.DISCONNECTED) {
          this.#setStatus(context.language === 'de' ? 'Anzeige getrennt' : 'Display disconnected', 'error');
          this.#required('reconnect').disabled = this.#session === null;
          this.#required('fullscreen').disabled = true;
        }
      };
      client.onerror = () => {
        this.#setStatus(context.language === 'de' ? 'Fehler in der Remote-Anzeige.' : 'Remote display error.', 'error');
        this.#required('reconnect').disabled = this.#session === null;
      };
      this.#resizeScheduler = createResizeScheduler(() => resizeDisplay(stage, display, client));
      this.#resizeObserver = new ResizeObserver(() => this.#resizeScheduler?.schedule());
      this.#resizeObserver.observe(stage);
      this.#resizeScheduler.flush();
      client.connect(endpoint.connectData);
      stage.focus();
      this.#updateButtons();
    }

    async #enterFullscreen() {
      const stage = this.#required('stage');
      if (typeof stage.requestFullscreen !== 'function') {
        this.#setStatus(context.language === 'de' ? 'Vollbild wird nicht unterstützt.' : 'Full screen is not supported.', 'error');
        return;
      }
      try {
        await stage.requestFullscreen();
      } catch {
        this.#setStatus(context.language === 'de' ? 'Vollbild konnte nicht geöffnet werden.' : 'Full screen could not be opened.', 'error');
      }
    }

    #stopClient() {
      this.#resizeObserver?.disconnect();
      this.#resizeObserver = null;
      this.#resizeScheduler?.dispose();
      this.#resizeScheduler = null;
      this.#keyboard?.dispose();
      this.#keyboard = null;
      this.#pointer?.dispose();
      this.#pointer = null;
      if (this.#client !== null) {
        try { this.#client.disconnect(); } catch {}
      }
      this.#client = null;
      this.#tunnel = null;
      const stage = this.shadowRoot?.getElementById('stage');
      if (stage !== null && stage !== undefined) {
        stage.onpointerdown = null;
        stage.replaceChildren();
      }
      const fullscreen = this.shadowRoot?.getElementById('fullscreen');
      if (fullscreen instanceof HTMLButtonElement) {
        fullscreen.disabled = true;
      }
    }

    #clearPoll() {
      if (this.#pollTimer !== null) {
        globalThis.clearTimeout(this.#pollTimer);
        this.#pollTimer = null;
      }
    }

    #setBusy(value) {
      this.#required('connect').disabled = value;
      this.#required('save').disabled = value;
    }

    #setStatus(message, state = '') {
      const status = this.#required('status');
      status.textContent = message;
      if (state.length === 0) delete status.dataset.state;
      else status.dataset.state = state;
    }

    #updateButtons() {
      const hasSession = this.#session !== null;
      this.#required('disconnect').disabled = !hasSession || terminalStates.has(this.#session.state);
      this.#required('reconnect').disabled = !hasSession || this.#client !== null || terminalStates.has(this.#session.state);
    }

    #required(id) {
      const element = this.shadowRoot?.getElementById(id);
      if (element === null || element === undefined) throw new Error(`Remote frontend is missing '${id}'.`);
      return element;
    }
  }

  class JulOsRemoteWidget extends HTMLElement {
    connectedCallback() {
      if (this.shadowRoot !== null) return;
      const shadow = this.attachShadow({ mode: 'open' });
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = context.language === 'de' ? 'Remote öffnen' : 'Open Remote';
      button.addEventListener('click', () => context.openApplication('remote'));
      shadow.append(button);
    }
  }

  if (!customElements.get('julos-remote-app')) customElements.define('julos-remote-app', JulOsRemoteApp);
  if (!customElements.get('julos-remote-widget')) customElements.define('julos-remote-widget', JulOsRemoteWidget);
}

export function encodeRemoteLaunchTarget(value) {
  const settings = validateRemoteLaunchTarget(value);
  const json = JSON.stringify(settings);
  return `${launchTargetPrefix}${base64UrlEncode(new TextEncoder().encode(json))}`;
}

export function decodeRemoteLaunchTarget(value) {
  if (typeof value !== 'string' || !value.startsWith(launchTargetPrefix)) throw new Error('Remote launch target is invalid.');
  const bytes = base64UrlDecode(value.slice(launchTargetPrefix.length));
  return validateRemoteLaunchTarget(JSON.parse(new TextDecoder().decode(bytes)));
}

function validateRemoteLaunchTarget(value) {
  if (value === null || typeof value !== 'object') throw new Error('Remote launch target is invalid.');
  const protocol = normalizeProtocol(value.protocol);
  const host = normalizeHost(value.host);
  const port = normalizePort(value.port, protocol);
  const userName = typeof value.userName === 'string' ? value.userName.trim() : '';
  const domain = typeof value.domain === 'string' ? value.domain.trim() : '';
  const secretReferenceId = value.secretReferenceId === null || value.secretReferenceId === undefined
    ? null
    : validateGuid(value.secretReferenceId);
  return { protocol, host, port, userName, domain, secretReferenceId };
}

function readConnectionSettings(root) {
  const protocol = normalizeProtocol(root?.getElementById('protocol')?.value);
  const target = parseTarget(root?.getElementById('target')?.value ?? '', protocol);
  const userName = (root?.getElementById('user-name')?.value ?? '').trim();
  const domain = (root?.getElementById('domain')?.value ?? '').trim();
  if ((protocol === 'rdp' || protocol === 'ssh') && userName.length === 0) throw new Error('User name is required.');
  return { protocol, host: target.host, port: target.port, userName, domain };
}

export function parseTarget(value, protocol) {
  const text = String(value ?? '').trim();
  if (text.length === 0 || /[\s/@?#]/u.test(text)) throw new Error('Remote target is invalid.');
  if (text.startsWith('[')) {
    const closing = text.indexOf(']');
    if (closing < 2) throw new Error('Remote target is invalid.');
    const host = normalizeHost(text.slice(1, closing));
    const suffix = text.slice(closing + 1);
    const port = suffix.length === 0 ? defaultPorts[normalizeProtocol(protocol)] : parseExplicitPort(suffix);
    return { host, port };
  }
  const colon = text.lastIndexOf(':');
  if (colon > 0 && text.indexOf(':') === colon) {
    return { host: normalizeHost(text.slice(0, colon)), port: parseExplicitPort(text.slice(colon)) };
  }
  return { host: normalizeHost(text), port: defaultPorts[normalizeProtocol(protocol)] };
}

function parseExplicitPort(suffix) {
  if (!/^:\d{1,5}$/u.test(suffix)) throw new Error('Remote port is invalid.');
  const port = Number(suffix.slice(1));
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Remote port is invalid.');
  return port;
}

function normalizeProtocol(value) {
  const protocol = String(value ?? '').trim().toLowerCase();
  if (!(protocol in defaultPorts)) throw new Error('Remote protocol is invalid.');
  return protocol;
}

function normalizeHost(value) {
  const host = String(value ?? '').trim();
  if (host.length === 0 || host.length > 253 || /[\s/@?#]/u.test(host)) throw new Error('Remote host is invalid.');
  return host;
}

function normalizePort(value, protocol) {
  if (value === null || value === undefined || value === '') return defaultPorts[protocol];
  const port = Number(value);
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Remote port is invalid.');
  return port;
}

function formatTarget(host, port, protocol) {
  const wrapped = host.includes(':') ? `[${host}]` : host;
  return port === defaultPorts[protocol] ? wrapped : `${wrapped}:${port}`;
}

function viewportFrom(stage) {
  const rect = stage.getBoundingClientRect();
  return {
    width: Math.max(320, Math.floor(rect.width || 1024)),
    height: Math.max(200, Math.floor(rect.height || 720)),
    deviceScaleFactor: Math.min(3, Math.max(1, globalThis.devicePixelRatio || 1)),
  };
}

function validateCredentialResponse(value) {
  if (value === null || typeof value !== 'object' || typeof value.secretReferenceId !== 'string') throw new Error('Remote credential response is invalid.');
  validateGuid(value.secretReferenceId);
  return value;
}

function validateGuid(value) {
  const text = String(value);
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(text)) throw new Error('Identifier is invalid.');
  return text;
}

function base64UrlEncode(bytes) {
  let binary = '';
  for (const value of bytes) binary += String.fromCharCode(value);
  return btoa(binary).replace(/\+/gu, '-').replace(/\//gu, '_').replace(/=+$/gu, '');
}

function base64UrlDecode(value) {
  if (!/^[A-Za-z0-9_-]+$/u.test(value)) throw new Error('Remote launch target is invalid.');
  const padded = value.replace(/-/gu, '+').replace(/_/gu, '/') + '='.repeat((4 - value.length % 4) % 4);
  const binary = atob(padded);
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

export function validateDisplayDescriptor(value) {
  if (value === null || typeof value !== 'object' || value.kind !== displayKind || value.contractVersion !== displayContractVersion || typeof value.endpoint !== 'string' || value.endpoint.length === 0 || typeof value.expiresAtUtc !== 'string') {
    throw new Error('Remote display descriptor is invalid.');
  }
  const endpoint = new URL(value.endpoint, currentOrigin());
  if (endpoint.origin !== currentOrigin()) throw new Error('Remote display endpoint must be same-origin.');
  for (const name of endpoint.searchParams.keys()) {
    if (/token|secret|password|credential/iu.test(name)) throw new Error('Remote display endpoint contains a forbidden credential selector.');
  }
  return value;
}

export function splitDisplayEndpoint(endpoint, origin = currentOrigin()) {
  const url = new URL(endpoint, origin);
  if (url.origin !== origin) throw new Error('Remote display endpoint must be same-origin.');
  return { tunnelUrl: url.pathname, connectData: url.search.startsWith('?') ? url.search.slice(1) : url.search };
}

export function isKeyboardReleaseShortcut(event) {
  return event.key === 'Escape' && event.ctrlKey === true && event.altKey === true && event.shiftKey === true;
}

export function createKeyboardPipeline(api, target, client, mobile, onRelease = () => {}) {
  const inputSink = mobile ? new api.InputSink() : null;
  const sinkElement = inputSink?.getElement() ?? null;
  if (sinkElement !== null) target.append(sinkElement);
  const keyboard = new api.Keyboard(target);
  keyboard.onkeydown = (keysym) => client.sendKeyEvent(1, keysym);
  keyboard.onkeyup = (keysym) => client.sendKeyEvent(0, keysym);
  const releaseKeyboard = (event) => {
    if (!isKeyboardReleaseShortcut(event)) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    if (typeof keyboard.reset === 'function') keyboard.reset();
    target.blur?.();
    onRelease();
  };
  target.addEventListener?.('keydown', releaseKeyboard, true);
  return {
    keyboard,
    inputSink,
    dispose() {
      target.removeEventListener?.('keydown', releaseKeyboard, true);
      keyboard.onkeydown = null;
      keyboard.onkeyup = null;
      if (typeof keyboard.reset === 'function') keyboard.reset();
      sinkElement?.remove();
    },
  };
}

export function createResizeScheduler(callback, delay = 150, timers = globalThis) {
  let timeout = null;
  let disposed = false;
  const run = () => { timeout = null; if (!disposed) callback(); };
  return {
    schedule() {
      if (disposed) return;
      if (timeout !== null) timers.clearTimeout(timeout);
      timeout = timers.setTimeout(run, delay);
    },
    flush() {
      if (disposed) return;
      if (timeout !== null) { timers.clearTimeout(timeout); timeout = null; }
      callback();
    },
    dispose() {
      disposed = true;
      if (timeout !== null) { timers.clearTimeout(timeout); timeout = null; }
    },
  };
}

export function createPointerPipeline(api, element, client, coarsePointer) {
  const pointer = coarsePointer ? new api.Mouse.Touchscreen(element) : new api.Mouse(element);
  const send = (state) => client.sendMouseState(state);
  pointer.onmousedown = send;
  pointer.onmouseup = send;
  pointer.onmousemove = send;
  return { pointer, dispose() { pointer.onmousedown = null; pointer.onmouseup = null; pointer.onmousemove = null; } };
}

function validateSessionResponse(value) {
  if (value === null || typeof value !== 'object' || typeof value.sessionId !== 'string' || typeof value.state !== 'string' || !Number.isInteger(value.revision) || value.revision < 1) {
    throw new Error('Remote session response is invalid.');
  }
  return value;
}

function resizeDisplay(stage, display, client) {
  const rect = stage.getBoundingClientRect();
  if (rect.width < 1 || rect.height < 1) return;
  const deviceScale = Math.min(3, Math.max(1, globalThis.devicePixelRatio || 1));
  client.sendSize(Math.max(1, Math.floor(rect.width * deviceScale)), Math.max(1, Math.floor(rect.height * deviceScale)));
  const remoteWidth = display.getWidth();
  const remoteHeight = display.getHeight();
  if (remoteWidth > 0 && remoteHeight > 0) display.scale(Math.min(rect.width / remoteWidth, rect.height / remoteHeight));
}

function isCoarsePointer() {
  return (globalThis.navigator?.maxTouchPoints ?? 0) > 0 || globalThis.matchMedia?.('(pointer: coarse)').matches === true;
}

function currentOrigin() {
  return globalThis.location?.origin ?? 'https://julos.invalid';
}
