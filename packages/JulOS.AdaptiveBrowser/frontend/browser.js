export async function register(context) {
  class JulOsAdaptiveBrowserApp extends HTMLElement {
    launchTarget = null;
    #tabs = [];
    #activeTabId = null;
    #resizeObserver = null;
    #executionPreference = 'auto';
    #userPreferenceKey = null;

    connectedCallback() {
      if (this.shadowRoot !== null) return;
      this.#render();
      this.#bind();
      void this.#initialize();
    }

    disconnectedCallback() {
      this.#resizeObserver?.disconnect();
      this.#resizeObserver = null;
      for (const tab of this.#tabs) void this.#disposeTab(tab);
    }

    #render() {
      const de = context.language === 'de';
      const shadow = this.attachShadow({ mode: 'open' });
      shadow.innerHTML = `
        <style>
          :host { display:block; width:100%; height:100%; min-height:26rem; color:CanvasText; font:14px/1.35 system-ui,sans-serif; }
          * { box-sizing:border-box; }
          .browser { display:grid; grid-template-rows:auto auto minmax(0,1fr) auto; width:100%; height:100%; min-height:26rem; background:Canvas; }
          .tabs { display:flex; align-items:stretch; gap:.2rem; min-width:0; padding:.3rem .4rem 0; overflow-x:auto; border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent); }
          .tab { display:flex; align-items:center; min-width:7rem; max-width:14rem; border:1px solid transparent; border-bottom:0; border-radius:.55rem .55rem 0 0; background:transparent; }
          .tab[data-active='true'] { background:color-mix(in srgb,Canvas 92%,CanvasText 8%); border-color:color-mix(in srgb,CanvasText 14%,transparent); }
          .tab-select { min-width:0; flex:1; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; border:0; background:transparent; text-align:left; }
          .tab-close { min-width:1.8rem; border:0; background:transparent; }
          .new-tab { flex:0 0 auto; }
          .toolbar { display:grid; grid-template-columns:auto auto auto auto minmax(8rem,1fr) auto auto; gap:.35rem; align-items:center; padding:.45rem; border-bottom:1px solid color-mix(in srgb,CanvasText 14%,transparent); }
          button,input,select { min-height:2.35rem; border:1px solid color-mix(in srgb,CanvasText 22%,transparent); border-radius:.5rem; font:inherit; }
          button { min-width:2.4rem; padding:.3rem .6rem; background:color-mix(in srgb,Canvas 92%,CanvasText 8%); color:CanvasText; cursor:pointer; }
          button:disabled { opacity:.45; cursor:default; }
          input { width:100%; min-width:0; padding:.35rem .65rem; background:Canvas; color:CanvasText; }
          select { max-width:10.5rem; padding:.35rem .55rem; background:Canvas; color:CanvasText; }
          .stage { position:relative; min-width:0; min-height:0; overflow:hidden; background:#111; outline:none; touch-action:none; }
          .pane { position:absolute; inset:0; }
          .pane[hidden] { display:none; }
          .pane canvas,.pane iframe { display:block; width:100%; height:100%; border:0; }
          .pane canvas { object-fit:contain; background:#111; }
          .empty,.device-notice { color:#ddd; text-align:center; }
          .empty { position:absolute; inset:0; display:grid; place-items:center; padding:2rem; }
          .device-notice { position:absolute; left:.75rem; right:.75rem; bottom:.75rem; z-index:2; display:flex; justify-content:center; align-items:center; gap:.7rem; padding:.55rem .7rem; border-radius:.55rem; background:rgba(20,20,20,.88); }
          .device-notice button { min-height:1.9rem; color:white; background:#333; border-color:#666; }
          .status { display:flex; justify-content:space-between; gap:1rem; margin:0; padding:.4rem .65rem; border-top:1px solid color-mix(in srgb,CanvasText 12%,transparent); font-size:.86rem; }
          .status[data-state='error'] { color:#b10e1e; }
          .mode { color:color-mix(in srgb,CanvasText 68%,transparent); white-space:nowrap; }
          @media (max-width:760px) {
            .toolbar { grid-template-columns:auto auto auto auto minmax(5rem,1fr); }
            #execution,#go { grid-column:auto; }
            #execution { grid-column:1 / 5; max-width:none; }
          }
        </style>
        <section class="browser">
          <div id="tabs" class="tabs" role="tablist"><button id="new-tab" class="new-tab" type="button" aria-label="${de ? 'Neuer Tab' : 'New tab'}">＋</button></div>
          <form id="toolbar" class="toolbar">
            <button id="back" type="button" aria-label="${de ? 'Zurück' : 'Back'}" disabled>←</button>
            <button id="forward" type="button" aria-label="${de ? 'Vor' : 'Forward'}" disabled>→</button>
            <button id="reload" type="button" aria-label="${de ? 'Neu laden' : 'Reload'}">↻</button>
            <button id="stop" type="button" aria-label="${de ? 'Laden stoppen' : 'Stop loading'}" disabled>×</button>
            <input id="address" type="text" inputmode="url" autocomplete="off" spellcheck="false" aria-label="${de ? 'Adresse' : 'Address'}" />
            <select id="execution" aria-label="${de ? 'Browser-Ausführung' : 'Browser execution'}">
              <option value="auto">${de ? 'Automatisch' : 'Automatic'}</option>
              <option value="device">${de ? 'Dieses Gerät' : 'This device'}</option>
              <option value="server">${de ? 'JulOS-Server' : 'JulOS server'}</option>
            </select>
            <button id="go" type="submit">${de ? 'Öffnen' : 'Open'}</button>
          </form>
          <div id="stage" class="stage" tabindex="0" aria-label="${de ? 'Browser-Inhalt' : 'Browser content'}"></div>
          <p id="status" class="status" role="status"><span id="status-text">${de ? 'Bereit' : 'Ready'}</span><span id="mode" class="mode"></span></p>
        </section>`;
    }

    #bind() {
      this.#required('toolbar').addEventListener('submit', (event) => {
        event.preventDefault();
        void this.#navigateActive(this.#required('address').value);
      });
      this.#required('new-tab').addEventListener('click', () => {
        const tab = this.#createTab('');
        this.#activateTab(tab.id);
        this.#required('address').focus();
      });
      this.#required('execution').addEventListener('change', () => {
        const value = this.#required('execution').value;
        if (!['auto', 'device', 'server'].includes(value)) return;
        this.#executionPreference = value;
        this.#persistPreference();
        const tab = this.#activeTab();
        if (tab?.url) void this.#navigateActive(tab.url, true);
        else this.#syncToolbar();
      });
      this.#required('back').addEventListener('click', () => this.#sendActiveControl({ type: 'back' }, true));
      this.#required('forward').addEventListener('click', () => this.#sendActiveControl({ type: 'forward' }, true));
      this.#required('reload').addEventListener('click', () => {
        const tab = this.#activeTab();
        if (!tab?.url) return;
        if (tab.mode === 'server' && tab.socket?.readyState === WebSocket.OPEN) this.#sendActiveControl({ type: 'reload' }, true);
        else void this.#navigateActive(tab.url);
      });
      this.#required('stop').addEventListener('click', () => {
        const tab = this.#activeTab();
        if (tab?.mode === 'server') this.#sendActiveControl({ type: 'stop' }, false);
        if (tab) { tab.loading = false; this.#syncToolbar(); }
      });
      this.#required('address').addEventListener('keydown', (event) => {
        if (event.key === 'Escape') { this.#required('address').value = this.#activeTab()?.url ?? ''; this.#required('stage').focus(); }
      });
      const stage = this.#required('stage');
      stage.addEventListener('keydown', (event) => {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'l') {
          this.#required('address').focus();
          this.#required('address').select();
          event.preventDefault();
          return;
        }
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 't') {
          this.#required('new-tab').click();
          event.preventDefault();
          return;
        }
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'w') {
          const tab = this.#activeTab();
          if (tab) void this.#closeTab(tab.id);
          event.preventDefault();
          return;
        }
        this.#sendKey(event, 'down');
      });
      stage.addEventListener('keyup', (event) => this.#sendKey(event, 'up'));
      this.#resizeObserver = new ResizeObserver(() => this.#sendViewport());
      this.#resizeObserver.observe(stage);
    }

    async #initialize() {
      await this.#loadPreference();
      const target = this.launchTarget?.externalIdentity;
      const initial = typeof target === 'string' && target.length > 0 ? target : 'https://www.google.com/';
      const tab = this.#createTab(initial);
      this.#activateTab(tab.id);
      await this.#navigateActive(initial);
    }

    async #loadPreference() {
      try {
        const response = await fetch('/api/v1/profile', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
        if (response.ok) {
          const profile = await response.json();
          if (typeof profile.userId === 'string' && profile.userId.length > 0) {
            this.#userPreferenceKey = `julos.adaptive-browser.execution.${profile.userId}`;
            const saved = localStorage.getItem(this.#userPreferenceKey);
            if (saved === 'auto' || saved === 'device' || saved === 'server') this.#executionPreference = saved;
          }
        }
      } catch { }
      this.#required('execution').value = this.#executionPreference;
    }

    #persistPreference() {
      if (this.#userPreferenceKey === null) return;
      try { localStorage.setItem(this.#userPreferenceKey, this.#executionPreference); } catch { }
    }

    #createTab(initialUrl) {
      const id = crypto.randomUUID();
      const pane = document.createElement('div');
      pane.className = 'pane';
      pane.hidden = true;
      pane.dataset.tabId = id;
      pane.replaceChildren(this.#empty(context.language === 'de' ? 'Adresse eingeben.' : 'Enter an address.'));
      this.#required('stage').append(pane);
      const tab = { id, url: initialUrl, title: context.language === 'de' ? 'Neuer Tab' : 'New tab', mode: null, session: null, socket: null, pollTimer: null, pane, canvas: null, loading: false, canGoBack: false, canGoForward: false };
      this.#tabs.push(tab);
      this.#renderTabs();
      return tab;
    }

    #renderTabs() {
      const tabs = this.#required('tabs');
      for (const item of [...tabs.querySelectorAll('.tab')]) item.remove();
      const newTab = this.#required('new-tab');
      for (const tab of this.#tabs) {
        const wrapper = document.createElement('div');
        wrapper.className = 'tab';
        wrapper.dataset.active = String(tab.id === this.#activeTabId);
        wrapper.setAttribute('role', 'tab');
        const select = document.createElement('button');
        select.type = 'button';
        select.className = 'tab-select';
        select.textContent = tab.title || shortUrl(tab.url) || (context.language === 'de' ? 'Neuer Tab' : 'New tab');
        select.title = tab.title || tab.url;
        select.addEventListener('click', () => this.#activateTab(tab.id));
        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'tab-close';
        close.textContent = '×';
        close.setAttribute('aria-label', context.language === 'de' ? 'Tab schließen' : 'Close tab');
        close.addEventListener('click', () => void this.#closeTab(tab.id));
        wrapper.append(select, close);
        tabs.insertBefore(wrapper, newTab);
      }
    }

    #activateTab(id) {
      if (!this.#tabs.some((tab) => tab.id === id)) return;
      this.#activeTabId = id;
      for (const tab of this.#tabs) tab.pane.hidden = tab.id !== id;
      this.#renderTabs();
      this.#syncToolbar();
      this.#sendViewport();
      this.#required('stage').focus();
    }

    async #closeTab(id) {
      const index = this.#tabs.findIndex((tab) => tab.id === id);
      if (index < 0) return;
      const [tab] = this.#tabs.splice(index, 1);
      await this.#disposeTab(tab);
      tab.pane.remove();
      if (this.#tabs.length === 0) this.#createTab('');
      if (this.#activeTabId === id) {
        const next = this.#tabs[Math.min(index, this.#tabs.length - 1)];
        this.#activeTabId = next.id;
        for (const candidate of this.#tabs) candidate.pane.hidden = candidate.id !== next.id;
      }
      this.#renderTabs();
      this.#syncToolbar();
    }

    async #navigateActive(raw, forceRestart = false) {
      const tab = this.#activeTab();
      if (!tab) return;
      let url;
      try { url = normalizeUrl(raw); }
      catch {
        this.#setStatus(context.language === 'de' ? 'Ungültige Adresse.' : 'Invalid address.', 'error');
        return;
      }
      const mode = resolveExecutionMode(this.#executionPreference, url);
      tab.url = url;
      tab.loading = true;
      this.#required('address').value = url;
      if (!forceRestart && tab.mode === 'server' && mode === 'server' && tab.socket?.readyState === WebSocket.OPEN) {
        this.#sendActiveControl({ type: 'navigate', url }, true);
        this.#syncToolbar();
        return;
      }
      if (!forceRestart && tab.mode === 'device' && mode === 'device' && tab.pane.querySelector('iframe')) {
        tab.pane.querySelector('iframe').src = url;
        this.#syncToolbar();
        return;
      }
      await this.#disposeRuntime(tab);
      tab.mode = mode;
      tab.canGoBack = false;
      tab.canGoForward = false;
      if (mode === 'device') await this.#startDevice(tab, url);
      else await this.#startServer(tab, url);
      this.#renderTabs();
      this.#syncToolbar();
    }

    async #startDevice(tab, url) {
      const frame = document.createElement('iframe');
      frame.src = url;
      frame.allow = 'accelerometer; autoplay; clipboard-read; clipboard-write; encrypted-media; fullscreen; geolocation; gyroscope; picture-in-picture; web-share';
      frame.referrerPolicy = 'strict-origin-when-cross-origin';
      frame.addEventListener('load', () => {
        tab.loading = false;
        if (this.#activeTabId === tab.id) this.#setStatus(context.language === 'de' ? 'Lokal geladen.' : 'Loaded locally.');
        this.#syncToolbar();
      });
      const notice = document.createElement('div');
      notice.className = 'device-notice';
      const text = document.createElement('span');
      text.textContent = context.language === 'de'
        ? 'Bleibt die Seite leer, blockiert sie wahrscheinlich Browser-Einbettung. JulOS umgeht CSP/X-Frame-Options nicht.'
        : 'If the page stays blank, it likely blocks browser framing. JulOS does not bypass CSP or X-Frame-Options.';
      const server = document.createElement('button');
      server.type = 'button';
      server.textContent = context.language === 'de' ? 'Auf JulOS-Server öffnen' : 'Open on JulOS server';
      server.addEventListener('click', () => void this.#openActiveOnServer());
      notice.append(text, server);
      tab.pane.replaceChildren(frame, notice);
      this.#setStatus(context.language === 'de' ? 'Lade auf diesem Gerät …' : 'Loading on this device …');
    }

    async #openActiveOnServer() {
      const tab = this.#activeTab();
      if (!tab?.url) return;
      await this.#disposeRuntime(tab);
      tab.mode = 'server';
      tab.loading = true;
      await this.#startServer(tab, tab.url);
      this.#syncToolbar();
    }

    async #startServer(tab, url) {
      tab.pane.replaceChildren(this.#empty(context.language === 'de' ? 'Chromium wird gestartet …' : 'Starting Chromium …'));
      this.#setStatus(context.language === 'de' ? 'Chromium auf dem JulOS-Server wird gestartet …' : 'Starting Chromium on the JulOS server …');
      try {
        const bounds = this.#required('stage').getBoundingClientRect();
        const session = validateSessionResponse(await context.invokeCapability('interactive.session', 'create', {
          operationKey: crypto.randomUUID(),
          request: {
            initialUrl: url,
            executionMode: 'server',
            network: null,
            viewportWidth: Math.max(320, Math.round(bounds.width || 1280)),
            viewportHeight: Math.max(240, Math.round(bounds.height || 800)),
            deviceScaleFactor: boundedScale(globalThis.devicePixelRatio || 1),
          },
        }));
        await this.#consumeSession(tab, session);
      } catch (error) {
        tab.loading = false;
        tab.pane.replaceChildren(this.#empty(errorMessage(error, context.language === 'de' ? 'Server-Browser konnte nicht gestartet werden.' : 'Server browser could not be started.')));
        this.#setStatus(errorMessage(error, context.language === 'de' ? 'Server-Browser konnte nicht gestartet werden.' : 'Server browser could not be started.'), 'error');
        this.#syncToolbar();
      }
    }

    async #consumeSession(tab, session) {
      if (!this.#tabs.includes(tab)) return;
      tab.session = session;
      if (terminalStates.has(session.state)) {
        tab.loading = false;
        const detail = session.failure?.detail ?? session.state;
        tab.pane.replaceChildren(this.#empty(detail));
        if (this.#activeTabId === tab.id) this.#setStatus(detail, 'error');
        this.#syncToolbar();
        return;
      }
      if (session.state === 'connected' && session.display !== null) {
        this.#connectStream(tab, validateDisplayResponse(session.display));
        return;
      }
      if (this.#activeTabId === tab.id) this.#setStatus(`${session.state} …`);
      this.#stopPolling(tab);
      tab.pollTimer = globalThis.setTimeout(() => void this.#readSession(tab), 650);
    }

    async #readSession(tab) {
      if (!this.#tabs.includes(tab) || tab.session === null) return;
      try {
        const value = await context.invokeCapability('interactive.session', 'read', { sessionId: tab.session.sessionId });
        await this.#consumeSession(tab, validateSessionResponse(value));
      } catch (error) {
        tab.loading = false;
        if (this.#activeTabId === tab.id) this.#setStatus(errorMessage(error, context.language === 'de' ? 'Sitzungsstatus nicht verfügbar.' : 'Session status unavailable.'), 'error');
        this.#syncToolbar();
      }
    }

    #connectStream(tab, display) {
      this.#disconnectStream(tab);
      const endpoint = sameOriginWebSocketUrl(display.endpoint);
      const socket = new WebSocket(endpoint, 'julos-browser-stream.v1');
      socket.binaryType = 'blob';
      const canvas = document.createElement('canvas');
      tab.canvas = canvas;
      tab.pane.replaceChildren(canvas);
      const ctx = canvas.getContext('2d', { alpha: false });
      if (ctx === null) throw new Error('Canvas 2D is unavailable.');
      socket.addEventListener('open', () => {
        if (!this.#tabs.includes(tab)) return;
        if (this.#activeTabId === tab.id) this.#setStatus(context.language === 'de' ? 'Verbunden' : 'Connected');
        this.#sendViewport();
      });
      socket.addEventListener('message', (event) => {
        if (!this.#tabs.includes(tab)) return;
        if (typeof event.data === 'string') { this.#handleStreamMessage(tab, event.data); return; }
        const blob = event.data instanceof Blob ? event.data : new Blob([event.data]);
        void createImageBitmap(blob).then((bitmap) => {
          if (!this.#tabs.includes(tab)) { bitmap.close(); return; }
          if (canvas.width !== bitmap.width || canvas.height !== bitmap.height) { canvas.width = bitmap.width; canvas.height = bitmap.height; }
          ctx.drawImage(bitmap, 0, 0);
          bitmap.close();
        }).catch(() => { if (this.#activeTabId === tab.id) this.#setStatus(context.language === 'de' ? 'Frame konnte nicht dargestellt werden.' : 'Frame could not be rendered.', 'error'); });
      });
      socket.addEventListener('close', () => {
        if (this.#tabs.includes(tab) && this.#activeTabId === tab.id && !terminalStates.has(tab.session?.state)) this.#setStatus(context.language === 'de' ? 'Browser-Stream getrennt.' : 'Browser stream disconnected.', 'error');
      });
      socket.addEventListener('error', () => { if (this.#activeTabId === tab.id) this.#setStatus(context.language === 'de' ? 'Browser-Stream fehlgeschlagen.' : 'Browser stream failed.', 'error'); });
      const pointer = (event, kind) => {
        if (this.#activeTabId !== tab.id) return;
        const rect = canvas.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        const x = event.clientX - rect.left;
        const y = event.clientY - rect.top;
        this.#sendActiveControl({ type: 'pointer', kind, x, y, button: mouseButton(event.button), buttons: event.buttons }, false);
      };
      canvas.addEventListener('pointermove', (event) => pointer(event, 'move'));
      canvas.addEventListener('pointerdown', (event) => { canvas.setPointerCapture?.(event.pointerId); this.#required('stage').focus(); pointer(event, 'down'); event.preventDefault(); });
      canvas.addEventListener('pointerup', (event) => { pointer(event, 'up'); event.preventDefault(); });
      canvas.addEventListener('pointercancel', (event) => pointer(event, 'up'));
      canvas.addEventListener('wheel', (event) => {
        if (this.#activeTabId !== tab.id) return;
        const rect = canvas.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        this.#sendActiveControl({ type: 'wheel', x: event.clientX - rect.left, y: event.clientY - rect.top, deltaX: event.deltaX, deltaY: event.deltaY }, false);
        event.preventDefault();
      }, { passive: false });
      tab.socket = socket;
    }

    #handleStreamMessage(tab, raw) {
      let message;
      try { message = JSON.parse(raw); } catch { return; }
      if (message?.type === 'state') {
        tab.loading = false;
        if (typeof message.url === 'string' && message.url.length > 0) tab.url = message.url;
        tab.canGoBack = message.canGoBack === true;
        tab.canGoForward = message.canGoForward === true;
        if (typeof message.title === 'string' && message.title.length <= 1024) tab.title = message.title || shortUrl(tab.url);
        this.#renderTabs();
        if (this.#activeTabId === tab.id) this.#setStatus(tab.title || (context.language === 'de' ? 'Verbunden' : 'Connected'));
        this.#syncToolbar();
      } else if (message?.type === 'error') {
        tab.loading = false;
        if (this.#activeTabId === tab.id) this.#setStatus(typeof message.detail === 'string' ? message.detail : 'Browser stream error.', 'error');
        this.#syncToolbar();
      }
    }

    #sendKey(event, kind) {
      const tab = this.#activeTab();
      if (tab?.mode !== 'server' || tab.socket?.readyState !== WebSocket.OPEN) return;
      this.#sendActiveControl({ type: 'key', kind, key: event.key, code: event.code, text: kind === 'down' ? printableText(event) : '', modifiers: modifierMask(event) }, false);
      event.preventDefault();
    }

    #sendViewport() {
      const tab = this.#activeTab();
      if (tab?.mode !== 'server' || tab.socket?.readyState !== WebSocket.OPEN) return;
      const rect = this.#required('stage').getBoundingClientRect();
      this.#sendActiveControl({ type: 'resize', width: Math.max(320, Math.round(rect.width)), height: Math.max(240, Math.round(rect.height)), deviceScaleFactor: boundedScale(globalThis.devicePixelRatio || 1) }, false);
    }

    #sendActiveControl(message, loading) {
      const tab = this.#activeTab();
      if (tab?.socket?.readyState !== WebSocket.OPEN) return;
      if (loading) tab.loading = true;
      tab.socket.send(JSON.stringify(message));
      this.#syncToolbar();
    }

    #syncToolbar() {
      const tab = this.#activeTab();
      this.#required('execution').value = this.#executionPreference;
      this.#required('address').value = tab?.url ?? '';
      this.#required('back').disabled = tab?.mode !== 'server' || tab.canGoBack !== true;
      this.#required('forward').disabled = tab?.mode !== 'server' || tab.canGoForward !== true;
      this.#required('stop').disabled = tab?.loading !== true;
      this.#required('mode').textContent = tab?.mode ? executionLabel(tab.mode, context.language) : '';
    }

    #activeTab() { return this.#tabs.find((tab) => tab.id === this.#activeTabId) ?? null; }

    async #disposeRuntime(tab) {
      this.#stopPolling(tab);
      this.#disconnectStream(tab);
      const session = tab.session;
      tab.session = null;
      if (session !== null && !terminalStates.has(session.state)) {
        try { await context.invokeCapability('interactive.session', 'terminate', { sessionId: session.sessionId, expectedRevision: session.revision }); } catch { }
      }
    }

    async #disposeTab(tab) {
      await this.#disposeRuntime(tab);
    }

    #stopPolling(tab) {
      if (tab.pollTimer !== null) globalThis.clearTimeout(tab.pollTimer);
      tab.pollTimer = null;
    }

    #disconnectStream(tab) {
      try { tab.socket?.close(1000, 'surface closed'); } catch { }
      tab.socket = null;
      tab.canvas = null;
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
  if (!value) throw new Error('Address is required.');
  if (!/^[a-z][a-z0-9+.-]*:/iu.test(value)) value = `https://${value}`;
  const url = new URL(value);
  if (url.protocol !== 'http:' && url.protocol !== 'https:') throw new Error('Only HTTP and HTTPS are supported.');
  if (url.username || url.password) throw new Error('Credentials in URLs are not supported.');
  return url.href;
}

export function resolveExecutionMode(preference, url) {
  if (preference === 'device' || preference === 'server') return preference;
  if (preference !== 'auto') throw new Error('Execution preference is invalid.');
  const target = new URL(url);
  return target.origin === globalThis.location?.origin ? 'device' : 'server';
}

export function validateSessionResponse(value) {
  if (!value || typeof value !== 'object'
      || typeof value.sessionId !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-7][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(value.sessionId)
      || !['requested', 'provisioning', 'connecting', 'connected', 'disconnecting', 'disconnected', 'cancelled', 'expired', 'failed'].includes(value.state)
      || !Number.isSafeInteger(value.revision) || value.revision < 1
      || (value.display !== null && value.display !== undefined && typeof value.display !== 'object')) {
    throw new Error('Adaptive Browser session response is invalid.');
  }
  return value;
}

export function validateDisplayResponse(value) {
  if (!value || typeof value !== 'object' || typeof value.endpoint !== 'string' || value.endpoint.length === 0 || value.endpoint.length > 2048) {
    throw new Error('Adaptive Browser display response is invalid.');
  }
  return value;
}

export function sameOriginWebSocketUrl(endpoint) {
  const url = new URL(endpoint, globalThis.location.origin);
  if (url.origin !== globalThis.location.origin || (url.protocol !== 'http:' && url.protocol !== 'https:')) throw new Error('Display endpoint must be same-origin HTTP(S).');
  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
  return url.href;
}

function boundedScale(value) { return Math.min(3, Math.max(0.5, Number.isFinite(value) ? value : 1)); }
function mouseButton(button) { return button === 1 ? 'middle' : button === 2 ? 'right' : 'left'; }
function modifierMask(event) { return (event.altKey ? 1 : 0) | (event.ctrlKey ? 2 : 0) | (event.metaKey ? 4 : 0) | (event.shiftKey ? 8 : 0); }
function printableText(event) { return event.key.length === 1 && !event.ctrlKey && !event.metaKey ? event.key : ''; }
function shortUrl(url) { try { return new URL(url).hostname || url; } catch { return url; } }
function executionLabel(mode, language) {
  if (language === 'de') return mode === 'device' ? 'Dieses Gerät · lokale GPU' : 'JulOS-Server · Chromium';
  return mode === 'device' ? 'This device · local GPU' : 'JulOS server · Chromium';
}
function errorMessage(error, fallback) {
  if (error instanceof Error && error.message.trim().length > 0) return error.message;
  if (error && typeof error === 'object' && typeof error.detail === 'string') return error.detail;
  return fallback;
}
