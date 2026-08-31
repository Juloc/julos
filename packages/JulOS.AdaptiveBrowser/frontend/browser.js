export async function register(context) {
  class JulOsAdaptiveBrowserApp extends HTMLElement {
    launchTarget = null;
    #session = null;
    #pollTimer = null;
    #socket = null;
    #resizeObserver = null;
    #executionPreference = 'auto';
    #userPreferenceKey = null;
    #currentUrl = 'https://www.google.com/';

    connectedCallback() {
      if (this.shadowRoot !== null) return;
      this.#render();
      this.#bind();
      void this.#initialize();
    }

    disconnectedCallback() {
      this.#stopPolling();
      this.#disconnectStream();
      void this.#terminateSession(this.#session);
    }

    #render() {
      const de = context.language === 'de';
      const shadow = this.attachShadow({ mode: 'open' });
      shadow.innerHTML = `
        <style>
          :host { display:block; width:100%; height:100%; min-height:26rem; color:CanvasText; font:14px/1.35 system-ui,sans-serif; }
          * { box-sizing:border-box; }
          .browser { display:grid; grid-template-rows:auto minmax(0,1fr) auto; width:100%; height:100%; min-height:26rem; background:Canvas; }
          .toolbar { display:grid; grid-template-columns:auto auto auto minmax(8rem,1fr) auto auto; gap:.35rem; align-items:center; padding:.45rem; border-bottom:1px solid color-mix(in srgb,CanvasText 14%,transparent); }
          button,input,select { min-height:2.35rem; border:1px solid color-mix(in srgb,CanvasText 22%,transparent); border-radius:.5rem; font:inherit; }
          button { min-width:2.4rem; padding:.3rem .6rem; background:color-mix(in srgb,Canvas 92%,CanvasText 8%); color:CanvasText; cursor:pointer; }
          button:disabled { opacity:.45; cursor:default; }
          input { width:100%; min-width:0; padding:.35rem .65rem; background:Canvas; color:CanvasText; }
          select { max-width:10.5rem; padding:.35rem .55rem; background:Canvas; color:CanvasText; }
          .stage { position:relative; min-width:0; min-height:0; overflow:hidden; background:#111; outline:none; touch-action:none; }
          .stage canvas,.stage iframe { display:block; width:100%; height:100%; border:0; }
          .stage canvas { object-fit:contain; background:#111; }
          .empty { position:absolute; inset:0; display:grid; place-items:center; padding:2rem; color:#ddd; text-align:center; }
          .status { display:flex; justify-content:space-between; gap:1rem; margin:0; padding:.4rem .65rem; border-top:1px solid color-mix(in srgb,CanvasText 12%,transparent); font-size:.86rem; }
          .status[data-state='error'] { color:#b10e1e; }
          .mode { color:color-mix(in srgb,CanvasText 68%,transparent); white-space:nowrap; }
          @media (max-width:760px) {
            .toolbar { grid-template-columns:auto auto auto minmax(5rem,1fr) auto; }
            #execution { grid-column:1 / -1; max-width:none; }
          }
        </style>
        <section class="browser">
          <form id="toolbar" class="toolbar">
            <button id="back" type="button" aria-label="${de ? 'Zurück' : 'Back'}" disabled>←</button>
            <button id="forward" type="button" aria-label="${de ? 'Vor' : 'Forward'}" disabled>→</button>
            <button id="reload" type="button" aria-label="${de ? 'Neu laden' : 'Reload'}">↻</button>
            <input id="address" type="text" inputmode="url" autocomplete="off" spellcheck="false" aria-label="${de ? 'Adresse' : 'Address'}" />
            <select id="execution" aria-label="${de ? 'Browser-Ausführung' : 'Browser execution'}">
              <option value="auto">${de ? 'Automatisch' : 'Automatic'}</option>
              <option value="device">${de ? 'Dieses Gerät' : 'This device'}</option>
              <option value="server">${de ? 'JulOS-Server' : 'JulOS server'}</option>
            </select>
            <button id="go" type="submit">${de ? 'Öffnen' : 'Open'}</button>
          </form>
          <div id="stage" class="stage" tabindex="0" aria-label="${de ? 'Browser-Inhalt' : 'Browser content'}">
            <div class="empty">${de ? 'Adresse eingeben oder eine gespeicherte App öffnen.' : 'Enter an address or open a saved app.'}</div>
          </div>
          <p id="status" class="status" role="status"><span id="status-text">${de ? 'Bereit' : 'Ready'}</span><span id="mode" class="mode"></span></p>
        </section>`;
    }

    #bind() {
      this.#required('toolbar').addEventListener('submit', (event) => {
        event.preventDefault();
        void this.#navigate(this.#required('address').value);
      });
      this.#required('execution').addEventListener('change', () => {
        const value = this.#required('execution').value;
        if (value !== 'auto' && value !== 'device' && value !== 'server') return;
        this.#executionPreference = value;
        this.#persistPreference();
        if (this.#required('address').value.trim().length > 0) void this.#navigate(this.#required('address').value);
      });
      this.#required('back').addEventListener('click', () => this.#sendControl({ type: 'back' }));
      this.#required('forward').addEventListener('click', () => this.#sendControl({ type: 'forward' }));
      this.#required('reload').addEventListener('click', () => {
        if (this.#socket?.readyState === WebSocket.OPEN) this.#sendControl({ type: 'reload' });
        else void this.#navigate(this.#currentUrl);
      });
    }

    async #initialize() {
      await this.#loadPreference();
      const target = this.launchTarget?.externalIdentity;
      const initial = typeof target === 'string' && target.length > 0 ? target : this.#currentUrl;
      this.#required('address').value = initial;
      await this.#navigate(initial);
    }

    async #loadPreference() {
      try {
        const response = await fetch('/api/v1/profile', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
        if (!response.ok) return;
        const profile = await response.json();
        if (typeof profile.userId !== 'string' || profile.userId.length === 0) return;
        this.#userPreferenceKey = `julos.adaptive-browser.execution.${profile.userId}`;
        const saved = localStorage.getItem(this.#userPreferenceKey);
        if (saved === 'auto' || saved === 'device' || saved === 'server') this.#executionPreference = saved;
      } catch {
      }
      this.#required('execution').value = this.#executionPreference;
    }

    #persistPreference() {
      if (this.#userPreferenceKey === null) return;
      try { localStorage.setItem(this.#userPreferenceKey, this.#executionPreference); } catch { }
    }

    async #navigate(raw) {
      let url;
      try {
        url = normalizeUrl(raw);
      } catch {
        this.#setStatus(context.language === 'de' ? 'Ungültige Adresse.' : 'Invalid address.', 'error');
        return;
      }
      this.#currentUrl = url;
      this.#required('address').value = url;
      const mode = resolveExecutionMode(this.#executionPreference, url);
      this.#required('mode').textContent = executionLabel(mode, context.language);
      if (mode === 'device') {
        await this.#startDevice(url);
      } else {
        await this.#startServer(url);
      }
    }

    async #startDevice(url) {
      this.#stopPolling();
      this.#disconnectStream();
      await this.#terminateSession(this.#session);
      this.#session = null;
      const frame = document.createElement('iframe');
      frame.src = url;
      frame.allow = 'accelerometer; autoplay; clipboard-read; clipboard-write; encrypted-media; fullscreen; geolocation; gyroscope; picture-in-picture; web-share';
      frame.referrerPolicy = 'strict-origin-when-cross-origin';
      frame.addEventListener('load', () => this.#setStatus(
        context.language === 'de'
          ? 'Lokal geladen. Seiten mit Frame-Schutz benötigen den Servermodus.'
          : 'Loaded locally. Sites that block framing require server mode.',
      ));
      this.#required('stage').replaceChildren(frame);
      this.#required('back').disabled = true;
      this.#required('forward').disabled = true;
      this.#setStatus(context.language === 'de' ? 'Lade auf diesem Gerät …' : 'Loading on this device …');
    }

    async #startServer(url) {
      this.#stopPolling();
      this.#disconnectStream();
      await this.#terminateSession(this.#session);
      this.#session = null;
      const stage = this.#required('stage');
      stage.replaceChildren(this.#empty(context.language === 'de' ? 'Chromium wird gestartet …' : 'Starting Chromium …'));
      this.#setStatus(context.language === 'de' ? 'Chromium auf dem JulOS-Server wird gestartet …' : 'Starting Chromium on the JulOS server …');
      try {
        const bounds = stage.getBoundingClientRect();
        const session = await context.invokeCapability('interactive.session', 'create', {
          operationKey: crypto.randomUUID(),
          request: {
            initialUrl: url,
            executionMode: 'server',
            network: null,
            viewportWidth: Math.max(320, Math.round(bounds.width || 1280)),
            viewportHeight: Math.max(240, Math.round(bounds.height || 800)),
            deviceScaleFactor: Math.min(3, Math.max(0.5, globalThis.devicePixelRatio || 1)),
          },
        });
        await this.#consumeSession(validateSession(session));
      } catch (error) {
        this.#setStatus(errorMessage(error, context.language === 'de' ? 'Server-Browser konnte nicht gestartet werden.' : 'Server browser could not be started.'), 'error');
      }
    }

    async #consumeSession(session) {
      this.#session = session;
      if (terminalStates.has(session.state)) {
        this.#setStatus(session.failure?.detail ?? session.state, 'error');
        return;
      }
      if (session.state === 'connected' && session.display !== null) {
        this.#connectStream(validateDisplay(session.display));
        return;
      }
      this.#setStatus(`${session.state} …`);
      this.#pollTimer = globalThis.setTimeout(() => void this.#readSession(), 650);
    }

    async #readSession() {
      if (this.#session === null) return;
      try {
        const value = await context.invokeCapability('interactive.session', 'read', { sessionId: this.#session.sessionId });
        await this.#consumeSession(validateSession(value));
      } catch (error) {
        this.#setStatus(errorMessage(error, context.language === 'de' ? 'Sitzungsstatus nicht verfügbar.' : 'Session status unavailable.'), 'error');
      }
    }

    #connectStream(display) {
      this.#disconnectStream();
      const endpoint = sameOriginWebSocketUrl(display.endpoint);
      const socket = new WebSocket(endpoint, 'julos-browser-stream.v1');
      socket.binaryType = 'blob';
      const canvas = document.createElement('canvas');
      const stage = this.#required('stage');
      stage.replaceChildren(canvas);
      stage.focus();
      const ctx = canvas.getContext('2d', { alpha: false });
      if (ctx === null) throw new Error('Canvas 2D is unavailable.');

      socket.addEventListener('open', () => {
        this.#setStatus(context.language === 'de' ? 'Verbunden' : 'Connected');
        this.#sendViewport();
      });
      socket.addEventListener('message', (event) => {
        if (typeof event.data === 'string') {
          this.#handleStreamMessage(event.data);
          return;
        }
        const blob = event.data instanceof Blob ? event.data : new Blob([event.data]);
        void createImageBitmap(blob).then((bitmap) => {
          if (canvas.width !== bitmap.width || canvas.height !== bitmap.height) {
            canvas.width = bitmap.width;
            canvas.height = bitmap.height;
          }
          ctx.drawImage(bitmap, 0, 0);
          bitmap.close();
        }).catch(() => this.#setStatus(context.language === 'de' ? 'Frame konnte nicht dargestellt werden.' : 'Frame could not be rendered.', 'error'));
      });
      socket.addEventListener('close', () => this.#setStatus(context.language === 'de' ? 'Browser-Stream getrennt.' : 'Browser stream disconnected.', 'error'));
      socket.addEventListener('error', () => this.#setStatus(context.language === 'de' ? 'Browser-Stream fehlgeschlagen.' : 'Browser stream failed.', 'error'));

      const pointer = (event, kind) => {
        const rect = canvas.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        const x = (event.clientX - rect.left) * (canvas.width / rect.width);
        const y = (event.clientY - rect.top) * (canvas.height / rect.height);
        this.#sendControl({ type: 'pointer', kind, x, y, button: mouseButton(event.button), buttons: event.buttons });
      };
      canvas.addEventListener('pointermove', (event) => pointer(event, 'move'));
      canvas.addEventListener('pointerdown', (event) => { canvas.setPointerCapture?.(event.pointerId); stage.focus(); pointer(event, 'down'); event.preventDefault(); });
      canvas.addEventListener('pointerup', (event) => { pointer(event, 'up'); event.preventDefault(); });
      canvas.addEventListener('wheel', (event) => {
        const rect = canvas.getBoundingClientRect();
        this.#sendControl({
          type: 'wheel',
          x: (event.clientX - rect.left) * (canvas.width / Math.max(1, rect.width)),
          y: (event.clientY - rect.top) * (canvas.height / Math.max(1, rect.height)),
          deltaX: event.deltaX,
          deltaY: event.deltaY,
        });
        event.preventDefault();
      }, { passive: false });
      stage.onkeydown = (event) => {
        this.#sendControl({ type: 'key', kind: 'down', key: event.key, code: event.code, text: printableText(event), modifiers: modifierMask(event) });
        if (!browserShortcutAllowed(event)) event.preventDefault();
      };
      stage.onkeyup = (event) => {
        this.#sendControl({ type: 'key', kind: 'up', key: event.key, code: event.code, text: '', modifiers: modifierMask(event) });
        if (!browserShortcutAllowed(event)) event.preventDefault();
      };
      this.#resizeObserver = new ResizeObserver(() => this.#sendViewport());
      this.#resizeObserver.observe(stage);
      this.#socket = socket;
    }

    #handleStreamMessage(raw) {
      let message;
      try { message = JSON.parse(raw); } catch { return; }
      if (message?.type === 'state') {
        if (typeof message.url === 'string' && message.url.length > 0) {
          this.#currentUrl = message.url;
          this.#required('address').value = message.url;
        }
        this.#required('back').disabled = message.canGoBack !== true;
        this.#required('forward').disabled = message.canGoForward !== true;
        if (typeof message.title === 'string' && message.title.length > 0) this.#setStatus(message.title);
      } else if (message?.type === 'error') {
        this.#setStatus(typeof message.detail === 'string' ? message.detail : 'Browser stream error.', 'error');
      }
    }

    #sendViewport() {
      if (this.#socket?.readyState !== WebSocket.OPEN) return;
      const rect = this.#required('stage').getBoundingClientRect();
      this.#sendControl({
        type: 'resize',
        width: Math.max(320, Math.round(rect.width)),
        height: Math.max(240, Math.round(rect.height)),
        deviceScaleFactor: Math.min(3, Math.max(0.5, globalThis.devicePixelRatio || 1)),
      });
    }

    #sendControl(message) {
      if (this.#socket?.readyState === WebSocket.OPEN) this.#socket.send(JSON.stringify(message));
    }

    async #terminateSession(session) {
      if (session === null || terminalStates.has(session.state)) return;
      try {
        await context.invokeCapability('interactive.session', 'terminate', {
          sessionId: session.sessionId,
          expectedRevision: session.revision,
        });
      } catch {
      }
    }

    #stopPolling() {
      if (this.#pollTimer !== null) globalThis.clearTimeout(this.#pollTimer);
      this.#pollTimer = null;
    }

    #disconnectStream() {
      this.#resizeObserver?.disconnect();
      this.#resizeObserver = null;
      const stage = this.shadowRoot?.getElementById('stage');
      if (stage instanceof HTMLElement) {
        stage.onkeydown = null;
        stage.onkeyup = null;
      }
      try { this.#socket?.close(1000, 'surface closed'); } catch { }
      this.#socket = null;
    }

    #empty(text) {
      const element = document.createElement('div');
      element.className = 'empty';
      element.textContent = text;
      return element;
    }

    #setStatus(message, state = '') {
      this.#required('status-text').textContent = message;
      const status = this.#required('status');
      if (state) status.dataset.state = state;
      else delete status.dataset.state;
    }

    #required(id) {
      const element = this.shadowRoot?.getElementById(id);
      if (element === null || element === undefined) throw new Error(`Adaptive Browser frontend is missing '${id}'.`);
      return element;
    }
  }

  if (!customElements.get('julos-adaptive-browser-app')) customElements.define('julos-adaptive-browser-app', JulOsAdaptiveBrowserApp);
}

const terminalStates = new Set(['cancelled', 'disconnected', 'expired', 'failed']);

export function normalizeUrl(raw) {
  let value = String(raw ?? '').trim();
  if (!/^[a-z][a-z0-9+.-]*:/iu.test(value)) value = `https://${value}`;
  const url = new URL(value);
  if (url.protocol !== 'http:' && url.protocol !== 'https:') throw new Error('Only HTTP and HTTPS are supported.');
  return url.href;
}

export function resolveExecutionMode(preference, url) {
  if (preference === 'device' || preference === 'server') return preference;
  const target = new URL(url);
  return target.origin === globalThis.location?.origin ? 'device' : 'server';
}

function executionLabel(mode, language) {
  if (language === 'de') return mode === 'device' ? 'Dieses Gerät · lokale GPU' : 'JulOS-Server · Chromium';
  return mode === 'device' ? 'This device · local GPU' : 'JulOS server · Chromium';
}

function validateSession(value) {
  if (!value || typeof value !== 'object' || typeof value.sessionId !== 'string' || typeof value.state !== 'string' || typeof value.revision !== 'number') {
    throw new Error('Adaptive Browser session response is invalid.');
  }
  return value;
}

function validateDisplay(value) {
  if (!value || typeof value !== 'object' || typeof value.endpoint !== 'string') throw new Error('Adaptive Browser display response is invalid.');
  return value;
}

function sameOriginWebSocketUrl(endpoint) {
  const url = new URL(endpoint, globalThis.location.origin);
  if (url.origin !== globalThis.location.origin) throw new Error('Display endpoint must be same-origin.');
  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
  return url.href;
}

function mouseButton(button) {
  return button === 1 ? 'middle' : button === 2 ? 'right' : 'left';
}

function modifierMask(event) {
  return (event.altKey ? 1 : 0) | (event.ctrlKey ? 2 : 0) | (event.metaKey ? 4 : 0) | (event.shiftKey ? 8 : 0);
}

function printableText(event) {
  return event.key.length === 1 && !event.ctrlKey && !event.metaKey ? event.key : '';
}

function browserShortcutAllowed(event) {
  return (event.ctrlKey || event.metaKey) && ['l', 't', 'w'].includes(event.key.toLowerCase());
}

function errorMessage(error, fallback) {
  if (error instanceof Error && error.message.trim().length > 0) return error.message;
  if (error && typeof error === 'object' && typeof error.detail === 'string') return error.detail;
  return fallback;
}
