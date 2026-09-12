const displayKind = 'graphical';
const displayContractVersion = '1.0.0';
const terminalStates = new Set(['cancelled', 'disconnected', 'expired', 'failed']);
const defaultPorts = Object.freeze({ rdp: 3389, ssh: 22, vnc: 5900 });
const launchTargetPrefix = 'remote:v1:';
const resolutionPresets = Object.freeze({
  '1920x1080': [1920, 1080],
  '1600x900': [1600, 900],
  '1366x768': [1366, 768],
  '1280x720': [1280, 720],
});
export const defaultRemoteInteraction = Object.freeze({
  touchMode: 'direct',
  gestureRightClick: true,
  longPressMs: 500,
  scrollThreshold: 20,
  cursorVisible: true,
  resolutionMode: 'auto',
  customWidth: 1920,
  customHeight: 1080,
  scaleMode: 'fit',
  resizeMode: 'display-update',
  toolbarExpanded: false,
});

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
    #display = null;
    #keyboard = null;
    #pointer = null;
    #clipboard = null;
    #resizeObserver = null;
    #resizeScheduler = null;
    #modifierState = new Set();
    #resizeReconnectInFlight = false;

    connectedCallback() {
      if (this.#connected) return;
      this.#connected = true;
      this.#render();
      this.#bindActions();
      this.#applyInteraction(defaultRemoteInteraction);
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
          :host { display:block; min-height:28rem; color:CanvasText; font:14px/1.4 system-ui,sans-serif; }
          * { box-sizing:border-box; }
          button,input,select { font:inherit; }
          button { cursor:pointer; min-height:2.5rem; border:1px solid color-mix(in srgb,CanvasText 22%,transparent); border-radius:.5rem; }
          button:disabled { opacity:.5; cursor:default; }
          .layout { display:grid; grid-template-columns:minmax(16rem,21rem) minmax(0,1fr); gap:1rem; min-height:28rem; }
          form,.viewer { border:1px solid color-mix(in srgb,CanvasText 16%,transparent); border-radius:.75rem; background:Canvas; }
          form { display:grid; align-content:start; gap:.7rem; padding:1rem; }
          label { display:grid; gap:.3rem; font-weight:600; }
          input,select { width:100%; min-height:2.5rem; padding:.35rem .55rem; border-radius:.45rem; border:1px solid color-mix(in srgb,CanvasText 24%,transparent); background:Canvas; color:CanvasText; }
          .remember,.check { display:flex; align-items:center; gap:.5rem; font-weight:400; }
          .remember input,.check input { width:auto; min-height:auto; }
          .actions { display:flex; gap:.5rem; flex-wrap:wrap; }
          .actions button { flex:1; }
          .viewer { min-width:0; overflow:hidden; position:relative; background:#111; min-height:28rem; }
          .stage { width:100%; height:100%; min-height:28rem; overflow:hidden; position:relative; background:#111; outline:none; touch-action:none; }
          .stage[data-scale='manual'] { overflow:auto; }
          .stage>div { transform-origin:top left; }
          .handle { position:absolute; z-index:30; top:max(.25rem,env(safe-area-inset-top)); left:50%; transform:translateX(-50%); width:3.5rem; height:2rem; border:0; background:transparent; }
          .handle::before { content:''; display:block; margin:.5rem auto 0; width:2.3rem; height:.28rem; border-radius:999px; background:#ffffffd9; box-shadow:0 1px 6px #0008; }
          .toolbar { position:absolute; z-index:29; top:max(2rem,calc(env(safe-area-inset-top) + 1.8rem)); left:50%; transform:translate(-50%,-8px); display:flex; align-items:center; gap:.3rem; max-width:calc(100% - 1rem); overflow-x:auto; padding:.4rem; border:1px solid #ffffff33; border-radius:.8rem; background:#171717e8; color:#fff; backdrop-filter:blur(18px); opacity:0; pointer-events:none; transition:.12s ease; }
          .toolbar[data-open='true'] { transform:translate(-50%,0); opacity:1; pointer-events:auto; }
          .toolbar button { flex:0 0 auto; min-width:44px; min-height:44px; border:0; background:transparent; color:inherit; padding:.35rem .5rem; }
          .toolbar button[aria-pressed='true'],.toolbar button:hover { background:#ffffff20; }
          .sheet { position:absolute; z-index:31; top:max(5.1rem,calc(env(safe-area-inset-top) + 4.8rem)); left:50%; transform:translateX(-50%); width:min(31rem,calc(100% - 1rem)); max-height:calc(100% - 6rem - env(safe-area-inset-bottom)); overflow:auto; display:none; gap:.7rem; padding:.9rem; border:1px solid #ffffff33; border-radius:.8rem; background:#171717f2; color:#fff; box-shadow:0 .7rem 2rem #0009; }
          .sheet[data-open='true'] { display:grid; }
          .sheet select,.sheet input { background:#242424; color:#fff; border-color:#ffffff2b; }
          .grid2 { display:grid; grid-template-columns:1fr 1fr; gap:.6rem; }
          .hint { margin:0; font-size:.8rem; opacity:.72; }
          .status { position:absolute; z-index:10; left:.65rem; bottom:max(.65rem,env(safe-area-inset-bottom)); margin:0; padding:.42rem .65rem; border-radius:999px; background:#161616df; color:#fff; pointer-events:none; }
          .status[data-state='connected'] { opacity:0; }
          .status[data-state='error'] { background:#8f1b20ee; }
          .layout[data-active='true'] { grid-template-columns:1fr; gap:0; }
          .layout[data-active='true'] form { display:none; }
          .layout[data-config-open='true'] form { display:grid; position:absolute; z-index:35; top:.6rem; left:.6rem; width:min(22rem,calc(100% - 1.2rem)); max-height:calc(100% - 1.2rem); overflow:auto; box-shadow:0 .7rem 2rem #0008; }
          .viewer:fullscreen,.viewer:fullscreen .stage { width:100vw; height:100vh; min-height:100vh; }
          [hidden] { display:none!important; }
          @media(max-width:760px){
            :host,.layout { min-height:100%; }
            .layout { grid-template-columns:1fr; }
            form { order:2; }
            .viewer,.stage { min-height:72vh; }
            .layout[data-active='true'],.layout[data-active='true'] .viewer,.layout[data-active='true'] .stage { min-height:100%; height:100%; }
            .toolbar { left:.35rem; right:.35rem; transform:translateY(-8px); max-width:none; }
            .toolbar[data-open='true'] { transform:translateY(0); }
            .sheet { left:.5rem; right:.5rem; width:auto; transform:none; }
          }
        </style>
        <section id="layout" class="layout">
          <form id="connection-form">
            <strong>${de ? 'Remote-Verbindung' : 'Remote connection'}</strong>
            <label>${de ? 'Name' : 'Name'}<input id="connection-name" autocomplete="off" placeholder="Windows 11" /></label>
            <label>${de ? 'Protokoll' : 'Protocol'}<select id="protocol"><option value="rdp">RDP</option><option value="ssh">SSH</option><option value="vnc">VNC</option></select></label>
            <label>${de ? 'Ziel' : 'Target'}<input id="target" required autocomplete="off" placeholder="192.168.1.10:3389" /></label>
            <label>${de ? 'Benutzer' : 'User'}<input id="user-name" autocomplete="username" /></label>
            <label>${de ? 'Domäne (optional)' : 'Domain (optional)'}<input id="domain" autocomplete="organization" /></label>
            <label>${de ? 'Passwort' : 'Password'}<input id="password" type="password" autocomplete="current-password" /></label>
            <label class="remember"><input id="remember-password" type="checkbox" checked />${de ? 'Passwort verschlüsselt speichern' : 'Store password encrypted'}</label>
            <div class="actions"><button id="connect" type="submit">${de ? 'Verbinden' : 'Connect'}</button><button id="save" type="button">${de ? 'Als App speichern' : 'Save as app'}</button></div>
          </form>
          <section id="viewer" class="viewer" aria-label="${de ? 'Remote-Anzeige' : 'Remote display'}">
            <div id="stage" class="stage" tabindex="0" aria-label="${de ? 'Interaktive Remote-Anzeige' : 'Interactive remote display'}"></div>
            <button id="controls-handle" class="handle" type="button" aria-expanded="false" aria-controls="toolbar" aria-label="${de ? 'Remote-Steuerung anzeigen' : 'Show remote controls'}"></button>
            <div id="toolbar" class="toolbar" role="toolbar" data-open="false">
              <button id="keyboard-button" type="button" disabled title="${de ? 'Tastatur' : 'Keyboard'}">⌨</button>
              <button id="pointer-mode-button" type="button" disabled title="${de ? 'Mausmodus' : 'Pointer mode'}">↖</button>
              <button id="right-click" type="button" disabled title="${de ? 'Rechtsklick' : 'Right click'}">RMB</button>
              <button id="ctrl" type="button" disabled aria-pressed="false">Ctrl</button>
              <button id="alt" type="button" disabled aria-pressed="false">Alt</button>
              <button id="shift" type="button" disabled aria-pressed="false">Shift</button>
              <button id="meta" type="button" disabled>Win</button>
              <button id="tab-key" type="button" disabled>Tab</button>
              <button id="esc-key" type="button" disabled>Esc</button>
              <button id="cad" type="button" disabled>CAD</button>
              <button id="paste-local" type="button" disabled>${de ? 'Einfügen' : 'Paste'}</button>
              <button id="copy-remote" type="button" disabled>${de ? 'Remote kopieren' : 'Copy remote'}</button>
              <button id="settings-toggle" type="button" aria-expanded="false">⚙</button>
              <button id="connection-settings" type="button">⋯</button>
              <button id="fullscreen" type="button" disabled>⛶</button>
              <button id="reconnect" type="button" disabled>↻</button>
              <button id="disconnect" type="button" disabled>×</button>
            </div>
            <section id="settings" class="sheet" data-open="false">
              <strong>${de ? 'Anzeige und Eingabe' : 'Display and input'}</strong>
              <label>${de ? 'Touch-Steuerung' : 'Touch control'}<select id="touch-mode"><option value="direct">${de ? 'Direkt – Long-Press Rechtsklick' : 'Direct – long-press right click'}</option><option value="touchpad">${de ? 'Trackpad – 2-Finger-Tap Rechtsklick' : 'Trackpad – two-finger tap right click'}</option></select></label>
              <label class="check"><input id="gesture-right-click" type="checkbox" />${de ? 'Rechtsklick-Geste aktiv' : 'Right-click gesture enabled'}</label>
              <label>${de ? 'Long-Press' : 'Long press'}<select id="long-press-ms"><option value="350">350 ms</option><option value="500">500 ms</option><option value="750">750 ms</option><option value="1000">1000 ms</option></select></label>
              <label>${de ? '2-Finger-Scrollen' : 'Two-finger scrolling'}<select id="scroll-threshold"><option value="12">${de ? 'Schnell' : 'Fast'}</option><option value="20">${de ? 'Normal' : 'Normal'}</option><option value="32">${de ? 'Ruhig' : 'Slow'}</option></select></label>
              <label class="check"><input id="cursor-visible" type="checkbox" />${de ? 'Remote-Mauszeiger anzeigen' : 'Show remote cursor'}</label>
              <label>${de ? 'Auflösung' : 'Resolution'}<select id="resolution-mode"><option value="auto">${de ? 'Auto – Fenster' : 'Auto – window'}</option><option value="native">${de ? 'Native Geräteauflösung' : 'Native device resolution'}</option><option value="1920x1080">1920×1080</option><option value="1600x900">1600×900</option><option value="1366x768">1366×768</option><option value="1280x720">1280×720</option><option value="custom">${de ? 'Benutzerdefiniert' : 'Custom'}</option></select></label>
              <div id="custom-resolution" class="grid2"><label>${de ? 'Breite' : 'Width'}<input id="custom-width" type="number" min="320" max="7680" /></label><label>${de ? 'Höhe' : 'Height'}<input id="custom-height" type="number" min="240" max="4320" /></label></div>
              <label>${de ? 'Skalierung' : 'Scaling'}<select id="scale-mode"><option value="fit">Fit</option><option value="100">100%</option><option value="125">125%</option><option value="150">150%</option><option value="200">200%</option></select></label>
              <label>${de ? 'Bei Größenänderung' : 'On resize'}<select id="resize-mode"><option value="display-update">${de ? 'Dynamisch (empfohlen)' : 'Dynamic (recommended)'}</option><option value="reconnect">${de ? 'Neu verbinden' : 'Reconnect'}</option><option value="none">${de ? 'Remote-Auflösung behalten' : 'Keep remote resolution'}</option></select></label>
              <div class="actions"><button id="apply-display" type="button">${de ? 'Anwenden' : 'Apply'}</button><button id="save-live-settings" type="button">${de ? 'Speichern' : 'Save'}</button></div>
              <p class="hint">${de ? 'System-Zurück bleibt bei JulOS. Remote-Sondertasten werden über die Leiste gesendet.' : 'System Back stays with JulOS. Remote special keys are sent from the toolbar.'}</p>
            </section>
            <p id="status" class="status" role="status">${de ? 'Nicht verbunden' : 'Not connected'}</p>
          </section>
        </section>`;
    }

    #bindActions() {
      this.#required('connection-form').addEventListener('submit', (event) => { event.preventDefault(); void this.#connectFromForm(); });
      this.#required('save').addEventListener('click', () => void this.#saveConnection());
      this.#required('save-live-settings').addEventListener('click', () => void this.#saveConnection());
      this.#required('reconnect').addEventListener('click', () => void this.#resumeSession());
      this.#required('disconnect').addEventListener('click', () => void this.#disconnectSession());
      this.#required('fullscreen').addEventListener('click', () => void this.#enterFullscreen());
      this.#required('controls-handle').addEventListener('click', () => this.#toggleToolbar());
      this.#required('settings-toggle').addEventListener('click', () => this.#toggleSettings());
      this.#required('connection-settings').addEventListener('click', () => this.#toggleConnectionForm());
      this.#required('keyboard-button').addEventListener('click', () => this.#keyboard?.focusTextInput());
      this.#required('pointer-mode-button').addEventListener('click', () => this.#cycleTouchMode());
      this.#required('right-click').addEventListener('click', () => this.#pointer?.clickRight());
      this.#required('ctrl').addEventListener('click', () => this.#toggleModifier('ctrl', 0xffe3));
      this.#required('alt').addEventListener('click', () => this.#toggleModifier('alt', 0xffe9));
      this.#required('shift').addEventListener('click', () => this.#toggleModifier('shift', 0xffe1));
      this.#required('meta').addEventListener('click', () => this.#sendKeys([0xffeb]));
      this.#required('tab-key').addEventListener('click', () => this.#sendKeys([0xff09]));
      this.#required('esc-key').addEventListener('click', () => this.#sendKeys([0xff1b]));
      this.#required('cad').addEventListener('click', () => this.#sendChord([0xffe3, 0xffe9, 0xffff]));
      this.#required('paste-local').addEventListener('click', () => void this.#pasteLocal());
      this.#required('copy-remote').addEventListener('click', () => void this.#copyRemote());
      this.#required('apply-display').addEventListener('click', () => this.#applyDisplay(true));
      this.#required('resolution-mode').addEventListener('change', () => this.#syncCustomResolution());
      for (const id of ['touch-mode','gesture-right-click','long-press-ms','scroll-threshold']) this.#required(id).addEventListener('change', () => this.#recreatePointerPipeline());
      this.#required('cursor-visible').addEventListener('change', () => this.#applyCursor());
      this.#required('scale-mode').addEventListener('change', () => this.#applyDisplay(false));
      this.#required('resize-mode').addEventListener('change', () => this.#configureResizePipeline());
    }

    #toggleToolbar(force) {
      const bar = this.#required('toolbar');
      const open = force ?? bar.dataset.open !== 'true';
      bar.dataset.open = String(open);
      this.#required('controls-handle').setAttribute('aria-expanded', String(open));
      if (!open) this.#toggleSettings(false);
    }

    #toggleSettings(force) {
      const sheet = this.#required('settings');
      const open = force ?? sheet.dataset.open !== 'true';
      sheet.dataset.open = String(open);
      this.#required('settings-toggle').setAttribute('aria-expanded', String(open));
      if (open) this.#toggleToolbar(true);
    }

    #toggleConnectionForm(force) {
      const layout = this.#required('layout');
      const open = force ?? layout.dataset.configOpen !== 'true';
      layout.dataset.configOpen = String(open);
      if (open) this.#toggleToolbar(true);
    }

    #applyLaunchTarget() {
      const identity = this.launchTarget?.externalIdentity;
      if (typeof identity !== 'string' || !identity.startsWith(launchTargetPrefix)) return;
      try {
        const saved = decodeRemoteLaunchTarget(identity);
        this.#savedTarget = saved;
        this.#required('connection-name').value = this.launchTarget?.displayName ?? '';
        this.#required('protocol').value = saved.protocol;
        this.#required('target').value = formatTarget(saved.host, saved.port, saved.protocol);
        this.#required('user-name').value = saved.userName;
        this.#required('domain').value = saved.domain;
        this.#required('remember-password').checked = saved.secretReferenceId !== null;
        this.#applyInteraction(saved.interaction);
        if (saved.secretReferenceId !== null) void this.#createSession(saved.secretReferenceId);
        else this.#setStatus(context.language === 'de' ? 'Passwort eingeben und verbinden.' : 'Enter the password and connect.');
      } catch { this.#setStatus(context.language === 'de' ? 'Gespeicherte Verbindung ist ungültig.' : 'Saved connection is invalid.', 'error'); }
    }

    #applyInteraction(interaction) {
      const value = validateInteraction(interaction);
      this.#required('touch-mode').value = value.touchMode;
      this.#required('gesture-right-click').checked = value.gestureRightClick;
      this.#required('long-press-ms').value = String(value.longPressMs);
      this.#required('scroll-threshold').value = String(value.scrollThreshold);
      this.#required('cursor-visible').checked = value.cursorVisible;
      this.#required('resolution-mode').value = value.resolutionMode;
      this.#required('custom-width').value = String(value.customWidth);
      this.#required('custom-height').value = String(value.customHeight);
      this.#required('scale-mode').value = value.scaleMode;
      this.#required('resize-mode').value = value.resizeMode;
      this.#toggleToolbar(value.toolbarExpanded);
      this.#syncCustomResolution();
    }

    #readInteraction() {
      return validateInteraction({
        touchMode: this.#required('touch-mode').value,
        gestureRightClick: this.#required('gesture-right-click').checked,
        longPressMs: Number(this.#required('long-press-ms').value),
        scrollThreshold: Number(this.#required('scroll-threshold').value),
        cursorVisible: this.#required('cursor-visible').checked,
        resolutionMode: this.#required('resolution-mode').value,
        customWidth: Number(this.#required('custom-width').value),
        customHeight: Number(this.#required('custom-height').value),
        scaleMode: this.#required('scale-mode').value,
        resizeMode: this.#required('resize-mode').value,
        toolbarExpanded: this.#required('toolbar').dataset.open === 'true',
      });
    }

    #syncCustomResolution() { this.#required('custom-resolution').hidden = this.#required('resolution-mode').value !== 'custom'; }

    async #connectFromForm() {
      let credential = null;
      try {
        const savedReference = this.#savedTarget?.secretReferenceId ?? null;
        const password = this.#required('password').value;
        if (password.length > 0) { credential = await this.#createCredential(); this.#temporaryCredential = credential; }
        else if (savedReference !== null) credential = { secretReferenceId: savedReference };
        else throw new Error('credential-required');
        await this.#createSession(credential.secretReferenceId);
      } catch {
        if (credential !== null && this.#temporaryCredential === credential) await this.#deleteTemporaryCredential();
        this.#setStatus(context.language === 'de' ? 'Passwort fehlt oder Verbindung konnte nicht erstellt werden.' : 'Password is missing or the connection could not be created.', 'error');
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
          if (secretReferenceId === null) { newlyCreated = await this.#createCredential(); secretReferenceId = newlyCreated.secretReferenceId; }
          else await this.#rotateCredential(secretReferenceId);
        }
        if (remember && secretReferenceId === null) throw new Error('credential-required');
        const externalIdentity = encodeRemoteLaunchTarget({ ...settings, secretReferenceId });
        const displayName = this.#required('connection-name').value.trim() || `${settings.protocol.toUpperCase()} ${settings.host}`;
        const previous = this.launchTarget;
        const saved = await context.saveLaunchTarget('remote', externalIdentity, displayName);
        if (previous?.launchTargetId && previous.externalIdentity !== externalIdentity) await context.deleteLaunchTarget(previous.launchTargetId);
        if (!remember && this.#savedTarget?.secretReferenceId) await this.#deleteCredential(this.#savedTarget.secretReferenceId);
        this.launchTarget = saved;
        this.#savedTarget = decodeRemoteLaunchTarget(saved.externalIdentity);
        this.#required('connection-name').value = saved.displayName;
        this.#required('password').value = '';
        this.#setStatus(context.language === 'de' ? 'Verbindung gespeichert.' : 'Connection saved.');
      } catch {
        if (newlyCreated !== null) await this.#deleteCredential(newlyCreated.secretReferenceId).catch(() => {});
        this.#setStatus(context.language === 'de' ? 'Verbindung konnte nicht gespeichert werden.' : 'Connection could not be saved.', 'error');
      } finally { this.#setBusy(false); }
    }

    async #createCredential() {
      const settings = readConnectionSettings(this.shadowRoot);
      const password = this.#required('password').value;
      if (password.length === 0) throw new Error('credential-required');
      return validateCredentialResponse(await context.invokeCapability('remote.session', 'credential.create', { secretValue: JSON.stringify({ username:settings.userName||null,password,domain:settings.domain||null,privateKey:null,passphrase:null }) }));
    }

    async #rotateCredential(secretReferenceId) {
      const settings = readConnectionSettings(this.shadowRoot);
      const password = this.#required('password').value;
      if (password.length === 0) return;
      await context.invokeCapability('remote.session', 'credential.rotate', { secretReferenceId, secretValue: JSON.stringify({ username:settings.userName||null,password,domain:settings.domain||null,privateKey:null,passphrase:null }) });
    }

    async #deleteCredential(secretReferenceId) { await context.invokeCapability('remote.session', 'credential.delete', { secretReferenceId }); }
    async #deleteTemporaryCredential() { const current=this.#temporaryCredential; this.#temporaryCredential=null; if(current!==null) await this.#deleteCredential(current.secretReferenceId).catch(()=>{}); }

    async #createSession(secretReferenceId) {
      this.#clearPoll(); this.#stopClient(); this.#session=null; this.#pollAttempt=0; this.#setBusy(true);
      this.#setStatus(context.language==='de'?'Verbindung wird erstellt …':'Creating session …');
      try {
        const settings=readConnectionSettings(this.shadowRoot); const now=new Date();
        const session=await context.invokeCapability('remote.session','create',{
          operationKey:crypto.randomUUID(), protocol:settings.protocol, target:{host:settings.host,port:settings.port}, secretReferenceId,
          profileId:null, networkProfileId:null, viewport:resolveViewport(this.#required('stage'),settings.interaction), idleTimeoutSeconds:1800,
          maximumSessionSeconds:28800, requestedAtUtc:now.toISOString(), deadlineUtc:new Date(now.getTime()+30000).toISOString(),
        });
        await this.#consumeSession(session);
      } finally { this.#setBusy(false); }
    }

    async #consumeSession(value) {
      const session=validateSessionResponse(value); this.#session=session; this.#updateButtons();
      if(terminalStates.has(session.state)){ this.#clearPoll(); this.#stopClient(); await this.#deleteTemporaryCredential(); this.#required('layout').dataset.active='false'; this.#setStatus(session.failure?.detail??session.state,'error'); return; }
      if(session.state==='connected'){ if(session.display==null){ await this.#resumeSession(); return; } this.#attachDisplay(validateDisplayDescriptor(session.display)); return; }
      this.#setStatus(`${session.state} …`); this.#scheduleRead();
    }

    #scheduleRead(){ this.#clearPoll(); if(!this.#connected||this.#session===null||this.#pollAttempt>=30){ if(this.#pollAttempt>=30)this.#setStatus(context.language==='de'?'Die Sitzung ist noch nicht bereit. Erneut verbinden.':'The session is not ready yet. Reconnect to continue.','error'); return;} this.#pollAttempt+=1; this.#pollTimer=globalThis.setTimeout(()=>void this.#readSession(),1000); }
    async #readSession(){ if(this.#session===null)return; try{ await this.#consumeSession(await context.invokeCapability('remote.session','read',{sessionId:this.#session.sessionId})); }catch{ this.#setStatus(context.language==='de'?'Sitzungsstatus nicht verfügbar.':'Session status is unavailable.','error'); } }
    async #resumeSession(){ if(this.#session===null)return; this.#clearPoll(); this.#setStatus(context.language==='de'?'Anzeige wird verbunden …':'Connecting display …'); try{ await this.#consumeSession(await context.invokeCapability('remote.session','resume',{sessionId:this.#session.sessionId,expectedRevision:this.#session.revision})); }catch{ this.#setStatus(context.language==='de'?'Die Anzeige konnte nicht erneut verbunden werden.':'The display could not be reconnected.','error'); } }
    async #cancelAndDispose(){ const session=this.#session; if(session!==null&&!terminalStates.has(session.state)){ try{ await context.invokeCapability('remote.session','cancel',{sessionId:session.sessionId,operationKey:crypto.randomUUID(),expectedRevision:session.revision,reason:'window_closed'}); }catch{} } await this.#deleteTemporaryCredential(); }
    async #disconnectSession(){ if(this.#session===null)return; this.#clearPoll(); try{ this.#session=validateSessionResponse(await context.invokeCapability('remote.session','disconnect',{sessionId:this.#session.sessionId,expectedRevision:this.#session.revision})); this.#stopClient(); await this.#deleteTemporaryCredential(); this.#required('layout').dataset.active='false'; this.#setStatus(context.language==='de'?'Verbindung getrennt':'Disconnected'); }catch{ this.#setStatus(context.language==='de'?'Trennen fehlgeschlagen.':'Disconnect failed.','error'); }finally{ this.#updateButtons(); } }

    #attachDisplay(descriptor) {
      this.#stopClient();
      const stage=this.#required('stage'); const endpoint=splitDisplayEndpoint(descriptor.endpoint); const tunnel=new Guacamole.WebSocketTunnel(endpoint.tunnelUrl); const client=new Guacamole.Client(tunnel); const display=client.getDisplay(); const displayElement=display.getElement();
      stage.replaceChildren(displayElement); this.#tunnel=tunnel; this.#client=client; this.#display=display;
      this.#keyboard=createKeyboardPipeline(Guacamole,stage,client,isCoarsePointer(),()=>this.#setStatus(context.language==='de'?'Tastatur freigegeben. Anzeige antippen, um sie wieder zu erfassen.':'Keyboard released. Tap the display to capture it again.'));
      this.#clipboard=createClipboardPipeline(Guacamole,client,()=>{ this.#required('copy-remote').disabled=false; });
      this.#recreatePointerPipeline(); this.#applyCursor(); stage.onpointerdown=()=>stage.focus();
      client.onstatechange=(state)=>{
        if(state===Guacamole.Client.State.CONNECTED){ this.#required('layout').dataset.active='true'; this.#required('layout').dataset.configOpen='false'; this.#setStatus(context.language==='de'?'Verbunden':'Connected','connected'); this.#enableControls(true); this.#applyDisplay(true,true); }
        else if(state===Guacamole.Client.State.DISCONNECTED){ this.#setStatus(context.language==='de'?'Anzeige getrennt':'Display disconnected','error'); this.#enableControls(false); this.#required('reconnect').disabled=this.#session===null; }
      };
      client.onerror=()=>{ this.#setStatus(context.language==='de'?'Fehler in der Remote-Anzeige.':'Remote display error.','error'); this.#required('reconnect').disabled=this.#session===null; };
      this.#configureResizePipeline(); client.connect(endpoint.connectData); stage.focus(); this.#updateButtons();
    }

    #configureResizePipeline(){ this.#resizeObserver?.disconnect(); this.#resizeObserver=null; this.#resizeScheduler?.dispose(); this.#resizeScheduler=null; if(this.#client===null||this.#display===null)return; this.#resizeScheduler=createResizeScheduler(()=>{ const settings=this.#readInteraction(); if(settings.resizeMode==='reconnect'&&settings.resolutionMode==='auto'){ void this.#reconnectForResize(); return; } resizeDisplay(this.#required('stage'),this.#display,this.#client,settings,settings.resizeMode==='display-update'&&settings.resolutionMode==='auto'); },150); this.#resizeObserver=new ResizeObserver(()=>this.#resizeScheduler?.schedule()); this.#resizeObserver.observe(this.#required('stage')); }
    #applyDisplay(sendRemoteSize,initial=false){ if(this.#client===null||this.#display===null)return; const settings=this.#readInteraction(); if(sendRemoteSize&&!initial&&settings.resizeMode==='reconnect'){ void this.#reconnectForResize(); return; } resizeDisplay(this.#required('stage'),this.#display,this.#client,settings,initial||(sendRemoteSize&&settings.resizeMode==='display-update')); this.#configureResizePipeline(); }
    async #reconnectForResize(){ if(this.#resizeReconnectInFlight||this.#session===null||this.#client===null)return; this.#resizeReconnectInFlight=true; try{ this.#stopClient(); await this.#resumeSession(); } finally { this.#resizeReconnectInFlight=false; } }
    #applyCursor(){ if(this.#display!==null&&typeof this.#display.showCursor==='function') this.#display.showCursor(this.#readInteraction().cursorVisible); }
    #recreatePointerPipeline(){ if(this.#client===null||this.#display===null)return; this.#pointer?.dispose(); const settings=this.#readInteraction(); this.#pointer=createPointerPipeline(Guacamole,this.#display.getElement(),this.#client,isCoarsePointer(),settings); this.#updatePointerButton(); }
    #cycleTouchMode(){ const select=this.#required('touch-mode'); select.value=select.value==='direct'?'touchpad':'direct'; this.#recreatePointerPipeline(); }
    #updatePointerButton(){ const b=this.#required('pointer-mode-button'); b.textContent=this.#required('touch-mode').value==='touchpad'?'TP':'↖'; }

    #toggleModifier(name,keysym){ if(this.#client===null)return; const b=this.#required(name); if(this.#modifierState.has(name)){this.#client.sendKeyEvent(0,keysym);this.#modifierState.delete(name);b.setAttribute('aria-pressed','false');}else{this.#client.sendKeyEvent(1,keysym);this.#modifierState.add(name);b.setAttribute('aria-pressed','true');} }
    #releaseModifiers(){ if(this.#client!==null){ const map={ctrl:0xffe3,alt:0xffe9,shift:0xffe1}; for(const name of this.#modifierState)this.#client.sendKeyEvent(0,map[name]); } for(const name of this.#modifierState)this.shadowRoot?.getElementById(name)?.setAttribute('aria-pressed','false'); this.#modifierState.clear(); }
    #sendKeys(keys){ if(this.#client===null)return; for(const k of keys){this.#client.sendKeyEvent(1,k);this.#client.sendKeyEvent(0,k);} }
    #sendChord(keys){ if(this.#client===null)return; for(const k of keys)this.#client.sendKeyEvent(1,k); for(const k of [...keys].reverse())this.#client.sendKeyEvent(0,k); }
    async #pasteLocal(){ if(this.#client===null)return; try{ const text=await navigator.clipboard.readText(); if(text.length>0)sendTextAsKeysyms(this.#client,text); }catch{ this.#setStatus(context.language==='de'?'Zwischenablage konnte nicht gelesen werden.':'Clipboard could not be read.','error'); } }
    async #copyRemote(){ const text=this.#clipboard?.readLatest()??''; if(text.length===0)return; try{ await navigator.clipboard.writeText(text); }catch{ this.#setStatus(context.language==='de'?'Zwischenablage konnte nicht geschrieben werden.':'Clipboard could not be written.','error'); } }
    #enableControls(enabled){ for(const id of ['keyboard-button','pointer-mode-button','right-click','ctrl','alt','shift','meta','tab-key','esc-key','cad','paste-local','fullscreen'])this.#required(id).disabled=!enabled; if(enabled)this.#required('reconnect').disabled=true; }

    async #enterFullscreen(){ const viewer=this.#required('viewer'); if(typeof viewer.requestFullscreen!=='function'){this.#setStatus(context.language==='de'?'Vollbild wird nicht unterstützt.':'Full screen is not supported.','error');return;} try{await viewer.requestFullscreen();}catch{this.#setStatus(context.language==='de'?'Vollbild konnte nicht geöffnet werden.':'Full screen could not be opened.','error');} }
    #stopClient(){ this.#resizeObserver?.disconnect();this.#resizeObserver=null;this.#resizeScheduler?.dispose();this.#resizeScheduler=null;this.#releaseModifiers();this.#keyboard?.dispose();this.#keyboard=null;this.#pointer?.dispose();this.#pointer=null;this.#clipboard?.dispose();this.#clipboard=null;this.#display=null;if(this.#client!==null){try{this.#client.disconnect();}catch{}}this.#client=null;this.#tunnel=null;const stage=this.shadowRoot?.getElementById('stage');if(stage){stage.onpointerdown=null;stage.replaceChildren();}this.#enableControls(false); }
    #clearPoll(){ if(this.#pollTimer!==null){globalThis.clearTimeout(this.#pollTimer);this.#pollTimer=null;} }
    #setBusy(value){this.#required('connect').disabled=value;this.#required('save').disabled=value;this.#required('save-live-settings').disabled=value;}
    #setStatus(message,state=''){const s=this.#required('status');s.textContent=message;if(state.length===0)delete s.dataset.state;else s.dataset.state=state;}
    #updateButtons(){const has=this.#session!==null;this.#required('disconnect').disabled=!has||terminalStates.has(this.#session.state);this.#required('reconnect').disabled=!has||this.#client!==null||terminalStates.has(this.#session.state);}
    #required(id){const element=this.shadowRoot?.getElementById(id);if(!element)throw new Error(`Remote frontend is missing '${id}'.`);return element;}
  }

  class JulOsRemoteWidget extends HTMLElement { connectedCallback(){ if(this.shadowRoot!==null)return;const shadow=this.attachShadow({mode:'open'});const button=document.createElement('button');button.type='button';button.textContent=context.language==='de'?'Remote öffnen':'Open Remote';button.addEventListener('click',()=>context.openApplication('remote'));shadow.append(button);} }
  if(!customElements.get('julos-remote-app'))customElements.define('julos-remote-app',JulOsRemoteApp);
  if(!customElements.get('julos-remote-widget'))customElements.define('julos-remote-widget',JulOsRemoteWidget);
}

export function encodeRemoteLaunchTarget(value){const settings=validateRemoteLaunchTarget(value);return `${launchTargetPrefix}${base64UrlEncode(new TextEncoder().encode(JSON.stringify(settings)))}`;}
export function decodeRemoteLaunchTarget(value){if(typeof value!=='string'||!value.startsWith(launchTargetPrefix))throw new Error('Remote launch target is invalid.');return validateRemoteLaunchTarget(JSON.parse(new TextDecoder().decode(base64UrlDecode(value.slice(launchTargetPrefix.length)))));}
function validateRemoteLaunchTarget(value){if(value===null||typeof value!=='object')throw new Error('Remote launch target is invalid.');const protocol=normalizeProtocol(value.protocol);const host=normalizeHost(value.host);const port=normalizePort(value.port,protocol);const userName=typeof value.userName==='string'?value.userName.trim():'';const domain=typeof value.domain==='string'?value.domain.trim():'';const secretReferenceId=value.secretReferenceId==null?null:validateGuid(value.secretReferenceId);return{protocol,host,port,userName,domain,secretReferenceId,interaction:validateInteraction(value.interaction)};}
export function validateInteraction(value={}){const v=value&&typeof value==='object'?value:{};return{touchMode:v.touchMode==='touchpad'?'touchpad':'direct',gestureRightClick:typeof v.gestureRightClick==='boolean'?v.gestureRightClick:true,longPressMs:oneOfInt(v.longPressMs,[350,500,750,1000],500),scrollThreshold:oneOfInt(v.scrollThreshold,[12,20,32],20),cursorVisible:typeof v.cursorVisible==='boolean'?v.cursorVisible:true,resolutionMode:['auto','native','1920x1080','1600x900','1366x768','1280x720','custom'].includes(v.resolutionMode)?v.resolutionMode:'auto',customWidth:boundedInt(v.customWidth,320,7680,1920),customHeight:boundedInt(v.customHeight,240,4320,1080),scaleMode:['fit','100','125','150','200'].includes(String(v.scaleMode))?String(v.scaleMode):'fit',resizeMode:['display-update','reconnect','none'].includes(v.resizeMode)?v.resizeMode:(v.resizeRemote===false?'none':'display-update'),toolbarExpanded:typeof v.toolbarExpanded==='boolean'?v.toolbarExpanded:false};}
function readConnectionSettings(root){const protocol=normalizeProtocol(root?.getElementById('protocol')?.value);const target=parseTarget(root?.getElementById('target')?.value??'',protocol);const userName=(root?.getElementById('user-name')?.value??'').trim();const domain=(root?.getElementById('domain')?.value??'').trim();if((protocol==='rdp'||protocol==='ssh')&&userName.length===0)throw new Error('User name is required.');const interaction=validateInteraction({touchMode:root?.getElementById('touch-mode')?.value,gestureRightClick:root?.getElementById('gesture-right-click')?.checked,longPressMs:Number(root?.getElementById('long-press-ms')?.value),scrollThreshold:Number(root?.getElementById('scroll-threshold')?.value),cursorVisible:root?.getElementById('cursor-visible')?.checked,resolutionMode:root?.getElementById('resolution-mode')?.value,customWidth:Number(root?.getElementById('custom-width')?.value),customHeight:Number(root?.getElementById('custom-height')?.value),scaleMode:root?.getElementById('scale-mode')?.value,resizeMode:root?.getElementById('resize-mode')?.value,toolbarExpanded:root?.getElementById('toolbar')?.dataset.open==='true'});return{protocol,host:target.host,port:target.port,userName,domain,interaction};}
export function parseTarget(value,protocol){const text=String(value??'').trim();if(text.length===0||/[\s/@?#]/u.test(text))throw new Error('Remote target is invalid.');if(text.startsWith('[')){const closing=text.indexOf(']');if(closing<2)throw new Error('Remote target is invalid.');const host=normalizeHost(text.slice(1,closing));const suffix=text.slice(closing+1);return{host,port:suffix.length===0?defaultPorts[normalizeProtocol(protocol)]:parseExplicitPort(suffix)};}const colon=text.lastIndexOf(':');if(colon>0&&text.indexOf(':')===colon)return{host:normalizeHost(text.slice(0,colon)),port:parseExplicitPort(text.slice(colon))};return{host:normalizeHost(text),port:defaultPorts[normalizeProtocol(protocol)]};}
function parseExplicitPort(suffix){if(!/^:\d{1,5}$/u.test(suffix))throw new Error('Remote port is invalid.');const port=Number(suffix.slice(1));if(!Number.isInteger(port)||port<1||port>65535)throw new Error('Remote port is invalid.');return port;}
function normalizeProtocol(value){const protocol=String(value??'').trim().toLowerCase();if(!(protocol in defaultPorts))throw new Error('Remote protocol is invalid.');return protocol;}
function normalizeHost(value){const host=String(value??'').trim();if(host.length===0||host.length>253||/[\s/@?#]/u.test(host))throw new Error('Remote host is invalid.');return host;}
function normalizePort(value,protocol){if(value==null||value==='')return defaultPorts[protocol];const port=Number(value);if(!Number.isInteger(port)||port<1||port>65535)throw new Error('Remote port is invalid.');return port;}
function formatTarget(host,port,protocol){const wrapped=host.includes(':')?`[${host}]`:host;return port===defaultPorts[protocol]?wrapped:`${wrapped}:${port}`;}
export function resolveViewport(stage,interaction=defaultRemoteInteraction,deviceScale=globalThis.devicePixelRatio||1,screenValue=globalThis.screen){const settings=validateInteraction(interaction);if(resolutionPresets[settings.resolutionMode]){const [width,height]=resolutionPresets[settings.resolutionMode];return{width,height,deviceScaleFactor:1};}if(settings.resolutionMode==='custom')return{width:settings.customWidth,height:settings.customHeight,deviceScaleFactor:1};if(settings.resolutionMode==='native')return{width:boundedInt(Math.floor((screenValue?.width||1920)*deviceScale),320,7680,1920),height:boundedInt(Math.floor((screenValue?.height||1080)*deviceScale),240,4320,1080),deviceScaleFactor:1};const rect=stage.getBoundingClientRect();return{width:boundedInt(Math.floor(rect.width||1024),320,7680,1024),height:boundedInt(Math.floor(rect.height||720),240,4320,720),deviceScaleFactor:1};}
function validateCredentialResponse(value){if(value===null||typeof value!=='object'||typeof value.secretReferenceId!=='string')throw new Error('Remote credential response is invalid.');validateGuid(value.secretReferenceId);return value;}
function validateGuid(value){const text=String(value);if(!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(text))throw new Error('Identifier is invalid.');return text;}
function base64UrlEncode(bytes){let binary='';for(const value of bytes)binary+=String.fromCharCode(value);return btoa(binary).replace(/\+/gu,'-').replace(/\//gu,'_').replace(/=+$/gu,'');}
function base64UrlDecode(value){if(!/^[A-Za-z0-9_-]+$/u.test(value))throw new Error('Remote launch target is invalid.');const padded=value.replace(/-/gu,'+').replace(/_/gu,'/')+'='.repeat((4-value.length%4)%4);return Uint8Array.from(atob(padded),(character)=>character.charCodeAt(0));}
export function validateDisplayDescriptor(value){if(value===null||typeof value!=='object'||value.kind!==displayKind||value.contractVersion!==displayContractVersion||typeof value.endpoint!=='string'||value.endpoint.length===0||typeof value.expiresAtUtc!=='string')throw new Error('Remote display descriptor is invalid.');const endpoint=new URL(value.endpoint,currentOrigin());if(endpoint.origin!==currentOrigin())throw new Error('Remote display endpoint must be same-origin.');for(const name of endpoint.searchParams.keys())if(/token|secret|password|credential/iu.test(name))throw new Error('Remote display endpoint contains a forbidden credential selector.');return value;}
export function splitDisplayEndpoint(endpoint,origin=currentOrigin()){const url=new URL(endpoint,origin);if(url.origin!==origin)throw new Error('Remote display endpoint must be same-origin.');return{tunnelUrl:url.pathname,connectData:url.search.startsWith('?')?url.search.slice(1):url.search};}
export function isKeyboardReleaseShortcut(event){return event.key==='Escape'&&event.ctrlKey===true&&event.altKey===true&&event.shiftKey===true;}
export function createKeyboardPipeline(api,target,client,mobile,onRelease=()=>{}){const inputSink=mobile?new api.InputSink():null;const sinkElement=inputSink?.getElement()??null;if(sinkElement!==null)target.append(sinkElement);let composing=false;let suppressText='';const keyboard=new api.Keyboard(target);keyboard.onkeydown=(keysym)=>{if(!composing)client.sendKeyEvent(1,keysym);};keyboard.onkeyup=(keysym)=>{if(!composing)client.sendKeyEvent(0,keysym);};const handlers=[];const listen=(element,name,handler,options)=>{element?.addEventListener?.(name,handler,options);handlers.push(()=>element?.removeEventListener?.(name,handler,options));};if(sinkElement){listen(sinkElement,'compositionstart',()=>{composing=true;});listen(sinkElement,'compositionend',(event)=>{composing=false;const text=String(event.data??'');if(text){sendTextAsKeysyms(client,text);suppressText=text;}clearSink(sinkElement);});listen(sinkElement,'paste',(event)=>{const text=event.clipboardData?.getData?.('text/plain')??'';if(text){event.preventDefault?.();sendTextAsKeysyms(client,text);suppressText=text;}clearSink(sinkElement);});listen(sinkElement,'input',(event)=>{if(composing||event.isComposing)return;const type=String(event.inputType??'');if(type==='deleteContentBackward'){sendKeysym(client,0xff08);clearSink(sinkElement);return;}if(type==='deleteContentForward'){sendKeysym(client,0xffff);clearSink(sinkElement);return;}if(type==='insertLineBreak'||type==='insertParagraph'){sendKeysym(client,0xff0d);clearSink(sinkElement);return;}const text=String(event.data??sinkElement.value??'');if(text){if(text===suppressText)suppressText='';else sendTextAsKeysyms(client,text);}clearSink(sinkElement);});}
const release=(event)=>{if(!isKeyboardReleaseShortcut(event))return;event.preventDefault();event.stopImmediatePropagation();keyboard.reset?.();sinkElement?.blur?.();target.blur?.();onRelease();};listen(target,'keydown',release,true);return{keyboard,inputSink,focusTextInput(){inputSink?.focus?.();sinkElement?.focus?.();target.focus?.();},dispose(){for(const remove of handlers)remove();keyboard.onkeydown=null;keyboard.onkeyup=null;keyboard.reset?.();sinkElement?.remove?.();}};}
function clearSink(element){if('value'in element)element.value='';}
export function sendTextAsKeysyms(client,text){for(const char of String(text)){const cp=char.codePointAt(0);let keysym;if(char==='\n'||char==='\r')keysym=0xff0d;else if(char==='\t')keysym=0xff09;else if(cp>=0x20&&cp<=0xff)keysym=cp;else if(cp>=0x100&&cp<=0x10ffff)keysym=0x01000000|cp;else continue;sendKeysym(client,keysym);}}
function sendKeysym(client,keysym){client.sendKeyEvent(1,keysym);client.sendKeyEvent(0,keysym);}
export function createResizeScheduler(callback,delay=150,timers=globalThis){let timeout=null;let disposed=false;const run=()=>{timeout=null;if(!disposed)callback();};return{schedule(){if(disposed)return;if(timeout!==null)timers.clearTimeout(timeout);timeout=timers.setTimeout(run,delay);},flush(){if(disposed)return;if(timeout!==null){timers.clearTimeout(timeout);timeout=null;}callback();},dispose(){disposed=true;if(timeout!==null){timers.clearTimeout(timeout);timeout=null;}}};}
export function createPointerPipeline(api,element,client,coarsePointer,interaction=defaultRemoteInteraction){const settings=validateInteraction(interaction);let pointer;if(!coarsePointer)pointer=new api.Mouse(element);else if(settings.touchMode==='touchpad'&&api.Mouse.Touchpad)pointer=new api.Mouse.Touchpad(element);else pointer=new api.Mouse.Touchscreen(element);if('longPressThreshold'in pointer)pointer.longPressThreshold=settings.longPressMs;if('scrollThreshold'in pointer)pointer.scrollThreshold=settings.scrollThreshold;let lastState=normalizeMouseState(pointer.currentState??{x:0,y:0});const send=(state)=>{const next=normalizeMouseState(state);lastState=next;if(!settings.gestureRightClick&&next.right)next.right=false;client.sendMouseState(next,true);};pointer.onmousedown=send;pointer.onmouseup=send;pointer.onmousemove=send;return{pointer,clickRight(){const pressed={...lastState,left:false,middle:false,right:true,up:false,down:false};client.sendMouseState(pressed,true);client.sendMouseState({...pressed,right:false},true);},dispose(){pointer.onmousedown=null;pointer.onmouseup=null;pointer.onmousemove=null;}};}
function normalizeMouseState(state){return{x:Number.isFinite(state?.x)?state.x:0,y:Number.isFinite(state?.y)?state.y:0,left:state?.left===true,middle:state?.middle===true,right:state?.right===true,up:state?.up===true,down:state?.down===true};}
export function createClipboardPipeline(api,client,onRemoteText=()=>{}){let latest='';client.onclipboard=(stream,mimetype)=>{if(typeof mimetype==='string'&&!mimetype.toLowerCase().startsWith('text/plain'))return;const reader=new api.StringReader(stream);let text='';reader.ontext=(chunk)=>{text+=chunk;};reader.onend=()=>{latest=text;onRemoteText(text);};};return{send(text){const writer=new api.StringWriter(client.createClipboardStream('text/plain'));writer.sendText(String(text));writer.sendEnd();},readLatest(){return latest;},dispose(){client.onclipboard=null;latest='';}};}
function validateSessionResponse(value){if(value===null||typeof value!=='object'||typeof value.sessionId!=='string'||typeof value.state!=='string'||!Number.isInteger(value.revision)||value.revision<1)throw new Error('Remote session response is invalid.');return value;}
export function resizeDisplay(stage,display,client,interaction=defaultRemoteInteraction,sendRemoteSize=true){const settings=validateInteraction(interaction);const rect=stage.getBoundingClientRect();if(rect.width<1||rect.height<1)return;const target=resolveViewport(stage,settings);if(sendRemoteSize)client.sendSize(target.width,target.height);const rw=display.getWidth()||target.width;const rh=display.getHeight()||target.height;let scale=Math.min(rect.width/rw,rect.height/rh);if(settings.scaleMode!=='fit')scale=Number(settings.scaleMode)/100;if(Number.isFinite(scale)&&scale>0)display.scale(scale);stage.dataset.scale=settings.scaleMode==='fit'?'fit':'manual';}
function oneOfInt(value,allowed,fallback){const n=Number(value);return allowed.includes(n)?n:fallback;}
function boundedInt(value,min,max,fallback){const n=Number(value);return Number.isInteger(n)&&n>=min&&n<=max?n:fallback;}
function isCoarsePointer(){return(globalThis.navigator?.maxTouchPoints??0)>0||globalThis.matchMedia?.('(pointer: coarse)').matches===true;}
function currentOrigin(){return globalThis.location?.origin??'https://julos.invalid';}
