const displayKind = 'graphical';
const displayContractVersion = '1.0.0';
const terminalStates = new Set(['cancelled', 'disconnected', 'expired', 'failed']);

export async function register(context) {
  class JulOsRemoteApp extends HTMLElement {
    #connected = false;
    #session = null;
    #pollTimer = null;
    #pollAttempt = 0;
    #client = null;
    #tunnel = null;
    #keyboard = null;
    #pointer = null;
    #resizeObserver = null;

    connectedCallback() {
      if (this.#connected) {
        return;
      }
      this.#connected = true;
      this.#render();
      this.#bindActions();
    }

    disconnectedCallback() {
      this.#connected = false;
      this.#clearPoll();
      this.#stopClient();
    }

    #render() {
      const de = context.language === 'de';
      const shadow = this.attachShadow({ mode: 'open' });
      shadow.innerHTML = `
        <style>
          :host { display: block; min-height: 28rem; color: CanvasText; font: 14px/1.4 system-ui, sans-serif; }
          * { box-sizing: border-box; }
          .layout { display: grid; grid-template-columns: minmax(15rem, 20rem) minmax(0, 1fr); gap: 1rem; min-height: 28rem; }
          form, .viewer { border: 1px solid color-mix(in srgb, CanvasText 16%, transparent); border-radius: .75rem; background: Canvas; }
          form { display: grid; align-content: start; gap: .75rem; padding: 1rem; }
          label { display: grid; gap: .3rem; font-weight: 600; }
          input, select, button { min-height: 2.25rem; border-radius: .4rem; border: 1px solid color-mix(in srgb, CanvasText 24%, transparent); font: inherit; }
          input, select { width: 100%; padding: .35rem .55rem; background: Canvas; color: CanvasText; }
          button { padding: .35rem .75rem; background: ButtonFace; color: ButtonText; cursor: pointer; }
          button:disabled { cursor: default; opacity: .55; }
          .viewer { min-width: 0; overflow: hidden; display: grid; grid-template-rows: auto minmax(18rem, 1fr) auto; }
          .toolbar { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; padding: .65rem; border-bottom: 1px solid color-mix(in srgb, CanvasText 12%, transparent); }
          .toolbar .spacer { flex: 1; }
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
            <label>${de ? 'Protokoll' : 'Protocol'}<select id="protocol"><option value="rdp">RDP</option><option value="ssh">SSH</option><option value="vnc">VNC</option></select></label>
            <label>${de ? 'Ziel' : 'Target'}<input id="target" required autocomplete="off" placeholder="server.example.test" /></label>
            <label>${de ? 'Benutzer' : 'User'}<input id="user-name" required autocomplete="username" /></label>
            <label>${de ? 'Secret-Referenz' : 'Secret reference'}<input id="secret-reference" required autocomplete="off" /></label>
            <button id="connect" type="submit">${de ? 'Verbinden' : 'Connect'}</button>
          </form>
          <section class="viewer" aria-label="${de ? 'Remote-Anzeige' : 'Remote display'}">
            <div class="toolbar">
              <button id="reconnect" type="button" disabled>${de ? 'Neu verbinden' : 'Reconnect'}</button>
              <button id="fullscreen" type="button" disabled>${de ? 'Vollbild' : 'Full screen'}</button>
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
        void this.#createSession();
      });
      this.#required('reconnect').addEventListener('click', () => void this.#resumeSession());
      this.#required('disconnect').addEventListener('click', () => void this.#disconnectSession());
      this.#required('fullscreen').addEventListener('click', () => void this.#enterFullscreen());
    }

    async #createSession() {
      this.#clearPoll();
      this.#stopClient();
      this.#session = null;
      this.#pollAttempt = 0;
      this.#setBusy(true);
      this.#setStatus(context.language === 'de' ? 'Verbindung wird erstellt …' : 'Creating session …');
      try {
        const session = await context.invokeCapability('remote.session', 'create', {
          protocol: this.#required('protocol').value,
          target: this.#required('target').value.trim(),
          userName: this.#required('user-name').value.trim(),
          secretReferenceId: this.#required('secret-reference').value.trim(),
        });
        await this.#consumeSession(session);
      } catch {
        this.#setStatus(
          context.language === 'de'
            ? 'Verbindung fehlgeschlagen oder nicht erlaubt.'
            : 'Connection failed or is not permitted.',
          'error',
        );
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
          this.#setStatus(
            context.language === 'de'
              ? 'Die Sitzung ist noch nicht bereit. Erneut verbinden.'
              : 'The session is not ready yet. Reconnect to continue.',
            'error',
          );
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
        const session = await context.invokeCapability('remote.session', 'read', {
          sessionId: this.#session.sessionId,
        });
        await this.#consumeSession(session);
      } catch {
        this.#setStatus(
          context.language === 'de' ? 'Sitzungsstatus nicht verfügbar.' : 'Session status is unavailable.',
          'error',
        );
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
        this.#setStatus(
          context.language === 'de'
            ? 'Die Anzeige konnte nicht erneut verbunden werden.'
            : 'The display could not be reconnected.',
          'error',
        );
      }
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
        this.#setStatus(context.language === 'de' ? 'Verbindung getrennt' : 'Disconnected');
      } catch {
        this.#setStatus(
          context.language === 'de' ? 'Trennen fehlgeschlagen.' : 'Disconnect failed.',
          'error',
        );
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
      this.#keyboard = createKeyboardPipeline(
        Guacamole,
        stage,
        client,
        isCoarsePointer(),
      );
      this.#pointer = createPointerPipeline(
        Guacamole,
        displayElement,
        client,
        isCoarsePointer(),
      );
      stage.addEventListener('pointerdown', () => stage.focus(), { passive: true });

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
        this.#setStatus(
          context.language === 'de' ? 'Fehler in der Remote-Anzeige.' : 'Remote display error.',
          'error',
        );
        this.#required('reconnect').disabled = this.#session === null;
      };

      this.#resizeObserver = new ResizeObserver(() => resizeDisplay(stage, display, client));
      this.#resizeObserver.observe(stage);
      resizeDisplay(stage, display, client);
      client.connect(endpoint.connectData);
      stage.focus();
      this.#updateButtons();
    }

    async #enterFullscreen() {
      const stage = this.#required('stage');
      if (typeof stage.requestFullscreen !== 'function') {
        this.#setStatus(
          context.language === 'de' ? 'Vollbild wird nicht unterstützt.' : 'Full screen is not supported.',
          'error',
        );
        return;
      }
      try {
        await stage.requestFullscreen();
      } catch {
        this.#setStatus(
          context.language === 'de' ? 'Vollbild konnte nicht geöffnet werden.' : 'Full screen could not be opened.',
          'error',
        );
      }
    }

    #stopClient() {
      this.#resizeObserver?.disconnect();
      this.#resizeObserver = null;
      this.#keyboard?.dispose();
      this.#keyboard = null;
      this.#pointer?.dispose();
      this.#pointer = null;
      if (this.#client !== null) {
        try {
          this.#client.disconnect();
        } catch {
        }
      }
      this.#client = null;
      this.#tunnel = null;
      const stage = this.shadowRoot?.getElementById('stage');
      stage?.replaceChildren();
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
    }

    #setStatus(message, state = '') {
      const status = this.#required('status');
      status.textContent = message;
      if (state.length === 0) {
        delete status.dataset.state;
      } else {
        status.dataset.state = state;
      }
    }

    #updateButtons() {
      const hasSession = this.#session !== null;
      this.#required('disconnect').disabled = !hasSession || terminalStates.has(this.#session.state);
      this.#required('reconnect').disabled = !hasSession || this.#client !== null || terminalStates.has(this.#session.state);
    }

    #required(id) {
      const element = this.shadowRoot?.getElementById(id);
      if (element === null || element === undefined) {
        throw new Error(`Remote frontend is missing '${id}'.`);
      }
      return element;
    }
  }

  class JulOsRemoteWidget extends HTMLElement {
    connectedCallback() {
      if (this.shadowRoot !== null) {
        return;
      }
      const shadow = this.attachShadow({ mode: 'open' });
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = context.language === 'de' ? 'Remote öffnen' : 'Open Remote';
      button.addEventListener('click', () => context.openApplication('remote'));
      shadow.append(button);
    }
  }

  if (!customElements.get('julos-remote-app')) {
    customElements.define('julos-remote-app', JulOsRemoteApp);
  }
  if (!customElements.get('julos-remote-widget')) {
    customElements.define('julos-remote-widget', JulOsRemoteWidget);
  }
}

export function validateDisplayDescriptor(value) {
  if (
    value === null
    || typeof value !== 'object'
    || value.kind !== displayKind
    || value.contractVersion !== displayContractVersion
    || typeof value.endpoint !== 'string'
    || value.endpoint.length === 0
    || typeof value.expiresAtUtc !== 'string'
  ) {
    throw new Error('Remote display descriptor is invalid.');
  }
  const endpoint = new URL(value.endpoint, currentOrigin());
  if (endpoint.origin !== currentOrigin()) {
    throw new Error('Remote display endpoint must be same-origin.');
  }
  for (const name of endpoint.searchParams.keys()) {
    if (/token|secret|password|credential/iu.test(name)) {
      throw new Error('Remote display endpoint contains a forbidden credential selector.');
    }
  }
  return value;
}

export function splitDisplayEndpoint(endpoint, origin = currentOrigin()) {
  const url = new URL(endpoint, origin);
  if (url.origin !== origin) {
    throw new Error('Remote display endpoint must be same-origin.');
  }
  return {
    tunnelUrl: url.pathname,
    connectData: url.search.startsWith('?') ? url.search.slice(1) : url.search,
  };
}

export function createKeyboardPipeline(api, target, client, mobile) {
  const inputSink = mobile ? new api.InputSink() : null;
  const sinkElement = inputSink?.getElement() ?? null;
  if (sinkElement !== null) {
    target.append(sinkElement);
  }

  const keyboard = new api.Keyboard(target);
  keyboard.onkeydown = (keysym) => {
    client.sendKeyEvent(1, keysym);
  };
  keyboard.onkeyup = (keysym) => {
    client.sendKeyEvent(0, keysym);
  };

  return {
    keyboard,
    inputSink,
    dispose() {
      keyboard.onkeydown = null;
      keyboard.onkeyup = null;
      if (typeof keyboard.reset === 'function') {
        keyboard.reset();
      }
      sinkElement?.remove();
    },
  };
}

export function createPointerPipeline(api, element, client, coarsePointer) {
  const pointer = coarsePointer
    ? new api.Mouse.Touchscreen(element)
    : new api.Mouse(element);
  const send = (state) => client.sendMouseState(state);
  pointer.onmousedown = send;
  pointer.onmouseup = send;
  pointer.onmousemove = send;
  return {
    pointer,
    dispose() {
      pointer.onmousedown = null;
      pointer.onmouseup = null;
      pointer.onmousemove = null;
    },
  };
}

function validateSessionResponse(value) {
  if (
    value === null
    || typeof value !== 'object'
    || typeof value.sessionId !== 'string'
    || typeof value.state !== 'string'
    || !Number.isInteger(value.revision)
    || value.revision < 1
  ) {
    throw new Error('Remote session response is invalid.');
  }
  return value;
}

function resizeDisplay(stage, display, client) {
  const rect = stage.getBoundingClientRect();
  if (rect.width < 1 || rect.height < 1) {
    return;
  }
  const deviceScale = Math.min(3, Math.max(1, globalThis.devicePixelRatio || 1));
  client.sendSize(
    Math.max(1, Math.floor(rect.width * deviceScale)),
    Math.max(1, Math.floor(rect.height * deviceScale)),
  );
  const remoteWidth = display.getWidth();
  const remoteHeight = display.getHeight();
  if (remoteWidth > 0 && remoteHeight > 0) {
    display.scale(Math.min(rect.width / remoteWidth, rect.height / remoteHeight));
  }
}

function isCoarsePointer() {
  return (globalThis.navigator?.maxTouchPoints ?? 0) > 0
    || globalThis.matchMedia?.('(pointer: coarse)').matches === true;
}

function currentOrigin() {
  return globalThis.location?.origin ?? 'https://julos.invalid';
}
