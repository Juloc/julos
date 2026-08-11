export async function register(context) {
  class JulOsBrowserApp extends HTMLElement {
    launchTarget = null;
    #profiles = [];
    #networks = [];
    #session = null;
    #pollTimer = null;
    #client = null;
    #keyboard = null;
    #mouse = null;
    #resizeObserver = null;

    connectedCallback() {
      if (this.shadowRoot !== null) {
        return;
      }
      this.#render();
      this.#bind();
      void this.#loadProfiles();
      void this.#loadNetworks();
      const initialUrl = this.launchTarget?.externalIdentity;
      if (typeof initialUrl === 'string' && initialUrl.length > 0) {
        this.#required('address').value = initialUrl;
        void this.#start(initialUrl);
      }
    }

    disconnectedCallback() {
      this.#stopPolling();
      this.#stopClient();
      if (this.#session !== null && !terminalStates.has(this.#session.state)) {
        void context.invokeCapability('interactive.session', 'terminate', {
          sessionId: this.#session.sessionId,
          expectedRevision: this.#session.revision,
        }).catch(() => {});
      }
    }

    #render() {
      const de = context.language === 'de';
      const shadow = this.attachShadow({ mode: 'open' });
      shadow.innerHTML = `
        <style>
          :host { display: block; width: 100%; height: 100%; min-height: 24rem; color: CanvasText; font: 14px/1.4 system-ui,sans-serif; }
          * { box-sizing: border-box; }
          .browser { display: grid; grid-template-rows: auto minmax(20rem,1fr) auto; width: 100%; height: 100%; min-height: 24rem; background: Canvas; }
          form { display: flex; gap: .5rem; padding: .6rem; border-bottom: 1px solid color-mix(in srgb,CanvasText 14%,transparent); }
          input, button, select { min-height: 2.35rem; border: 1px solid color-mix(in srgb,CanvasText 22%,transparent); border-radius: .45rem; font: inherit; }
          select { padding: .35rem .5rem; background: Canvas; color: CanvasText; max-width: 12rem; }
          .header { display: flex; flex-direction: column; }
          #new-profile { display: flex; gap: .5rem; align-items: center; padding: .6rem; border-bottom: 1px solid color-mix(in srgb,CanvasText 14%,transparent); }
          #new-profile[hidden] { display: none; }
          input { flex: 1; min-width: 8rem; padding: .35rem .6rem; background: Canvas; color: CanvasText; }
          button { padding: .35rem .75rem; cursor: pointer; }
          button:disabled { opacity: .55; cursor: default; }
          .stage { min-width: 0; min-height: 20rem; overflow: hidden; position: relative; background: #111; outline: none; touch-action: none; }
          .stage > div { transform-origin: top left; }
          .status { margin: 0; padding: .5rem .7rem; border-top: 1px solid color-mix(in srgb,CanvasText 12%,transparent); }
          .status[data-state='error'] { color: #b10e1e; }
        </style>
        <section class="browser">
          <div class="header">
            <form id="toolbar">
              <input id="address" type="url" required autocomplete="off" placeholder="https://example.org" aria-label="${de ? 'Adresse' : 'Address'}" />
              <select id="profile" aria-label="${de ? 'Profil' : 'Profile'}">
                <option value="">${de ? 'Temporär' : 'Temporary'}</option>
              </select>
              <button id="new-toggle" type="button">${de ? 'Neu' : 'New'}</button>
              <button id="open" type="submit">${de ? 'Öffnen' : 'Open'}</button>
              <button id="save" type="button">${de ? 'Als App' : 'Save app'}</button>
              <button id="stop" type="button" disabled>${de ? 'Stop' : 'Stop'}</button>
            </form>
            <form id="new-profile" hidden>
              <input id="new-name" type="text" required maxlength="96" autocomplete="off" placeholder="${de ? 'Profilname' : 'Profile name'}" aria-label="${de ? 'Profilname' : 'Profile name'}" />
              <select id="new-network" aria-label="${de ? 'Netzwerk' : 'Network'}"></select>
              <button id="new-create" type="submit">${de ? 'Anlegen' : 'Create'}</button>
              <button id="new-cancel" type="button">${de ? 'Abbrechen' : 'Cancel'}</button>
            </form>
          </div>
          <div id="stage" class="stage" tabindex="0" aria-label="${de ? 'Browseranzeige' : 'Browser display'}"></div>
          <p id="status" class="status" role="status">${de ? 'Bereit' : 'Ready'}</p>
        </section>`;
    }

    #bind() {
      this.#required('toolbar').addEventListener('submit', (event) => {
        event.preventDefault();
        void this.#start(this.#required('address').value);
      });
      this.#required('save').addEventListener('click', () => void this.#saveApp());
      this.#required('stop').addEventListener('click', () => void this.#terminate());
      this.#required('new-toggle').addEventListener('click', () => this.#toggleNewProfile());
      this.#required('new-cancel').addEventListener('click', () => this.#toggleNewProfile(false));
      this.#required('new-profile').addEventListener('submit', (event) => {
        event.preventDefault();
        void this.#createProfile();
      });
    }

    async #saveApp() {
      try {
        const url = normalizeUrl(this.#required('address').value);
        const parsed = new URL(url);
        await context.saveLaunchTarget('browser', url, parsed.hostname || url);
        this.#setStatus(context.language === 'de' ? 'App gespeichert.' : 'App saved.');
      } catch {
        this.#setStatus(context.language === 'de' ? 'App konnte nicht gespeichert werden.' : 'App could not be saved.', 'error');
      }
    }

    async #loadProfiles() {
      let profiles;
      try {
        profiles = validateProfileList(await context.invokeCapability('interactive.profiles', 'list', {}));
      } catch {
        this.#profiles = [];
        return;
      }
      this.#profiles = profiles;
      const select = this.shadowRoot?.getElementById('profile');
      if (!(select instanceof HTMLSelectElement)) {
        return;
      }
      const temporary = context.language === 'de' ? 'Temporär' : 'Temporary';
      const options = [`<option value="">${temporary}</option>`];
      for (const profile of profiles) {
        options.push(`<option value="${escapeHtml(profile.profileId)}">${escapeHtml(profile.displayName)}</option>`);
      }
      const previous = select.value;
      select.innerHTML = options.join('');
      if (profiles.some((profile) => profile.profileId === previous)) {
        select.value = previous;
      }
    }

    #profileSelection() {
      const select = this.shadowRoot?.getElementById('profile');
      const id = select instanceof HTMLSelectElement ? select.value : '';
      return id.length === 0
        ? null
        : this.#profiles.find((profile) => profile.profileId === id) ?? null;
    }

    async #loadNetworks() {
      let networks;
      try {
        networks = validateNetworkProfileList(
          await context.invokeCapability('interactive.profiles', 'list-networks', {}),
        );
      } catch {
        this.#networks = [];
        return;
      }
      this.#networks = networks;
      const select = this.shadowRoot?.getElementById('new-network');
      if (!(select instanceof HTMLSelectElement)) {
        return;
      }
      select.innerHTML = networks.length === 0
        ? `<option value="">${context.language === 'de' ? 'Kein Netzwerk konfiguriert' : 'No network configured'}</option>`
        : networks.map((network) => `<option value="${escapeHtml(network.key)}">${escapeHtml(network.key)}</option>`).join('');
      const create = this.shadowRoot?.getElementById('new-create');
      if (create instanceof HTMLButtonElement) {
        create.disabled = networks.length === 0;
      }
    }

    #toggleNewProfile(force) {
      const form = this.shadowRoot?.getElementById('new-profile');
      if (!(form instanceof HTMLElement)) {
        return;
      }
      const show = force ?? form.hidden;
      form.hidden = !show;
      if (show) {
        this.shadowRoot?.getElementById('new-name')?.focus?.();
      }
    }

    async #createProfile() {
      let request;
      try {
        const network = this.shadowRoot?.getElementById('new-network');
        request = toCreateProfileRequest(
          this.#required('new-name').value,
          network instanceof HTMLSelectElement ? network.value : '',
        );
      } catch {
        this.#setStatus(context.language === 'de' ? 'Profil ist ungültig.' : 'Profile is invalid.', 'error');
        return;
      }
      let created;
      try {
        created = validateCreatedProfile(
          await context.invokeCapability('interactive.profiles', 'create', request),
        );
      } catch {
        this.#setStatus(
          context.language === 'de' ? 'Profil konnte nicht angelegt werden.' : 'Profile could not be created.',
          'error',
        );
        return;
      }
      this.#required('new-name').value = '';
      this.#toggleNewProfile(false);
      await this.#loadProfiles();
      const select = this.shadowRoot?.getElementById('profile');
      if (select instanceof HTMLSelectElement && this.#profiles.some((profile) => profile.profileId === created.profileId)) {
        select.value = created.profileId;
      }
      this.#setStatus(context.language === 'de' ? 'Profil angelegt.' : 'Profile created.');
    }

    async #start(rawUrl) {
      this.#stopPolling();
      this.#stopClient();
      this.#session = null;
      this.#required('stop').disabled = false;
      try {
        const url = normalizeUrl(rawUrl);
        this.#required('address').value = url;
        this.#setStatus(context.language === 'de' ? 'Browsersitzung wird gestartet …' : 'Starting browser session …');
        const session = await context.invokeCapability('interactive.session', 'create', {
          operationKey: crypto.randomUUID(),
          request: toSessionRequest(this.#profileSelection(), url),
        });
        await this.#consume(session);
      } catch (error) {
        this.#required('stop').disabled = true;
        this.#setStatus(
          failureDetail(error)
            ?? (context.language === 'de'
              ? 'Browsersitzung konnte nicht gestartet werden.'
              : 'Browser session could not be started.'),
          'error',
        );
      }
    }

    async #consume(value) {
      const session = validateSession(value);
      this.#session = session;
      if (terminalStates.has(session.state)) {
        this.#stopPolling();
        this.#stopClient();
        this.#required('stop').disabled = true;
        this.#setStatus(session.failure?.detail ?? session.state, 'error');
        return;
      }
      if (session.state === 'connected' && session.display !== null && session.display !== undefined) {
        await this.#attachDisplay(validateDisplay(session.display));
        return;
      }
      this.#setStatus(`${session.state} …`);
      this.#pollTimer = globalThis.setTimeout(() => void this.#read(), 750);
    }

    async #read() {
      if (this.#session === null) {
        return;
      }
      try {
        const session = await context.invokeCapability('interactive.session', 'read', {
          sessionId: this.#session.sessionId,
        });
        await this.#consume(session);
      } catch {
        this.#setStatus(context.language === 'de' ? 'Sitzungsstatus nicht verfügbar.' : 'Session status unavailable.', 'error');
      }
    }

    async #terminate() {
      this.#stopPolling();
      if (this.#session === null || terminalStates.has(this.#session.state)) {
        this.#stopClient();
        return;
      }
      try {
        const session = await context.invokeCapability('interactive.session', 'terminate', {
          sessionId: this.#session.sessionId,
          expectedRevision: this.#session.revision,
        });
        this.#session = validateSession(session);
      } catch {
        this.#setStatus(context.language === 'de' ? 'Stoppen fehlgeschlagen.' : 'Stop failed.', 'error');
        return;
      }
      this.#stopClient();
      this.#required('stop').disabled = true;
      this.#setStatus(context.language === 'de' ? 'Gestoppt' : 'Stopped');
    }

    async #attachDisplay(descriptor) {
      await loadGuacamole();
      this.#stopClient();
      const api = globalThis.Guacamole;
      if (api === undefined) {
        throw new Error('Guacamole client is unavailable.');
      }
      const endpoint = splitEndpoint(descriptor.endpoint);
      const stage = this.#required('stage');
      const tunnel = new api.WebSocketTunnel(endpoint.tunnelUrl);
      const client = new api.Client(tunnel);
      const display = client.getDisplay();
      const displayElement = display.getElement();
      stage.replaceChildren(displayElement);

      const keyboard = new api.Keyboard(stage);
      keyboard.onkeydown = (keysym) => client.sendKeyEvent(1, keysym);
      keyboard.onkeyup = (keysym) => client.sendKeyEvent(0, keysym);
      const mouse = isCoarsePointer() ? new api.Mouse.Touchscreen(displayElement) : new api.Mouse(displayElement);
      const sendMouse = (state) => client.sendMouseState(state);
      mouse.onmousedown = sendMouse;
      mouse.onmouseup = sendMouse;
      mouse.onmousemove = sendMouse;
      stage.onpointerdown = () => stage.focus();

      const resize = () => resizeDisplay(stage, display, client);
      const resizeObserver = new ResizeObserver(resize);
      resizeObserver.observe(stage);
      client.onstatechange = (state) => {
        if (state === api.Client.State.CONNECTED) {
          this.#setStatus(context.language === 'de' ? 'Verbunden' : 'Connected');
          resize();
        } else if (state === api.Client.State.DISCONNECTED) {
          this.#setStatus(context.language === 'de' ? 'Anzeige getrennt' : 'Display disconnected', 'error');
        }
      };
      client.onerror = () => this.#setStatus(
        context.language === 'de' ? 'Fehler in der Browseranzeige.' : 'Browser display error.',
        'error',
      );

      this.#client = client;
      this.#keyboard = keyboard;
      this.#mouse = mouse;
      this.#resizeObserver = resizeObserver;
      client.connect(endpoint.connectData);
      stage.focus();
    }

    #stopPolling() {
      if (this.#pollTimer !== null) {
        globalThis.clearTimeout(this.#pollTimer);
        this.#pollTimer = null;
      }
    }

    #stopClient() {
      this.#resizeObserver?.disconnect();
      this.#resizeObserver = null;
      if (this.#keyboard !== null) {
        this.#keyboard.onkeydown = null;
        this.#keyboard.onkeyup = null;
        this.#keyboard.reset?.();
      }
      this.#keyboard = null;
      if (this.#mouse !== null) {
        this.#mouse.onmousedown = null;
        this.#mouse.onmouseup = null;
        this.#mouse.onmousemove = null;
      }
      this.#mouse = null;
      try {
        this.#client?.disconnect();
      } catch {
      }
      this.#client = null;
      const stage = this.shadowRoot?.getElementById('stage');
      if (stage !== null && stage !== undefined) {
        stage.onpointerdown = null;
        stage.replaceChildren();
      }
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

    #required(id) {
      const element = this.shadowRoot?.getElementById(id);
      if (element === null || element === undefined) {
        throw new Error(`Browser frontend is missing '${id}'.`);
      }
      return element;
    }
  }

  if (!customElements.get('julos-browser-app')) {
    customElements.define('julos-browser-app', JulOsBrowserApp);
  }
}

const terminalStates = new Set(['cancelled', 'disconnected', 'expired', 'failed']);
let guacamolePromise = null;

function loadGuacamole() {
  if (globalThis.Guacamole !== undefined) {
    return Promise.resolve();
  }
  guacamolePromise ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = '/vendor/guacamole-common-js-1.6.0.js';
    script.async = true;
    script.addEventListener('load', () => resolve(), { once: true });
    script.addEventListener('error', () => reject(new Error('Guacamole client failed to load.')), { once: true });
    document.head.append(script);
  });
  return guacamolePromise;
}

export function normalizeUrl(value) {
  const url = new URL(value.trim());
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new Error('Browser URL must use HTTP or HTTPS.');
  }
  return url.href;
}

// Maps the selected profile (or null for a temporary session) onto the opaque
// Browser session request. A retained profile carries its stored mode and id;
// no selection means a one-shot temporary session with no persistent volume.
export function toSessionRequest(selection, url) {
  if (selection === null || selection === undefined) {
    return { initialUrl: url, profileMode: 'temporary', profileId: null };
  }
  if (
    typeof selection !== 'object'
    || typeof selection.profileId !== 'string'
    || (selection.mode !== 'persistent' && selection.mode !== 'application')
  ) {
    throw new Error('Browser profile selection is invalid.');
  }
  return { initialUrl: url, profileMode: selection.mode, profileId: selection.profileId };
}

// Validates the caller-safe interactive.profiles list response and returns only
// the fields the app needs, rejecting any malformed entry.
export function validateProfileList(value) {
  const profiles = value !== null && typeof value === 'object' ? value.profiles : undefined;
  if (!Array.isArray(profiles)) {
    throw new Error('Browser profile list is invalid.');
  }
  return profiles.map((profile) => {
    if (
      profile === null
      || typeof profile !== 'object'
      || typeof profile.profileId !== 'string'
      || typeof profile.displayName !== 'string'
      || (profile.mode !== 'persistent' && profile.mode !== 'application')
    ) {
      throw new Error('Browser profile entry is invalid.');
    }
    return { profileId: profile.profileId, displayName: profile.displayName, mode: profile.mode };
  });
}

// Builds the opaque create-profile request. Slice 1b only creates persistent
// profiles; application-mode creation (which also needs a fixed URL) is a later
// slice. Trims and bounds the name and requires an existing network profile.
export function toCreateProfileRequest(name, networkProfileKey) {
  const displayName = typeof name === 'string' ? name.trim() : '';
  const key = typeof networkProfileKey === 'string' ? networkProfileKey.trim() : '';
  if (displayName.length === 0 || displayName.length > 96 || key.length === 0) {
    throw new Error('Browser profile is invalid.');
  }
  return { displayName, mode: 'persistent', networkProfileKey: key };
}

// Validates the caller-safe list-networks response, returning only the fields the
// create form needs. A proxy secret value is never present in this response.
export function validateNetworkProfileList(value) {
  const networks = value !== null && typeof value === 'object' ? value.networkProfiles : undefined;
  if (!Array.isArray(networks)) {
    throw new Error('Browser network profile list is invalid.');
  }
  return networks.map((network) => {
    if (
      network === null
      || typeof network !== 'object'
      || typeof network.key !== 'string'
      || typeof network.runtimeNetwork !== 'string'
    ) {
      throw new Error('Browser network profile entry is invalid.');
    }
    return { key: network.key, runtimeNetwork: network.runtimeNetwork };
  });
}

// Validates the single created-profile response returned by interactive.profiles/create.
export function validateCreatedProfile(value) {
  if (
    value === null
    || typeof value !== 'object'
    || typeof value.profileId !== 'string'
    || typeof value.displayName !== 'string'
    || (value.mode !== 'persistent' && value.mode !== 'application')
  ) {
    throw new Error('Browser profile response is invalid.');
  }
  return { profileId: value.profileId, displayName: value.displayName, mode: value.mode };
}

// Escapes text before it is placed into option markup, so a user-chosen profile
// name can never inject markup into the toolbar.
export function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function failureDetail(value) {
  return value instanceof Error && value.message.trim().length > 0
    ? value.message.trim()
    : null;
}

export function validateSession(value) {
  if (
    value === null
    || typeof value !== 'object'
    || typeof value.sessionId !== 'string'
    || typeof value.state !== 'string'
    || !Number.isInteger(value.revision)
    || value.revision < 1
  ) {
    throw new Error('Browser session response is invalid.');
  }
  return value;
}

function validateDisplay(value) {
  if (
    value === null
    || typeof value !== 'object'
    || value.kind !== 'graphical'
    || value.contractVersion !== '1.0.0'
    || typeof value.endpoint !== 'string'
    || value.endpoint.length === 0
  ) {
    throw new Error('Browser display descriptor is invalid.');
  }
  const endpoint = new URL(value.endpoint, globalThis.location.origin);
  if (endpoint.origin !== globalThis.location.origin) {
    throw new Error('Browser display endpoint must be same-origin.');
  }
  return value;
}

function splitEndpoint(endpoint) {
  const url = new URL(endpoint, globalThis.location.origin);
  if (url.origin !== globalThis.location.origin) {
    throw new Error('Browser display endpoint must be same-origin.');
  }
  return {
    tunnelUrl: url.pathname,
    connectData: url.search.startsWith('?') ? url.search.slice(1) : url.search,
  };
}

function resizeDisplay(stage, display, client) {
  const rect = stage.getBoundingClientRect();
  if (rect.width < 1 || rect.height < 1) {
    return;
  }
  const scale = Math.min(3, Math.max(1, globalThis.devicePixelRatio || 1));
  client.sendSize(Math.max(1, Math.floor(rect.width * scale)), Math.max(1, Math.floor(rect.height * scale)));
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
