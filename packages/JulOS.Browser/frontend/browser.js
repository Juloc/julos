export async function register(context) {
  class JulOsBrowserApp extends HTMLElement {
    connectedCallback() {
      const shadow = this.attachShadow({ mode: 'open' });
      const form = document.createElement('form');
      const address = document.createElement('input');
      const open = document.createElement('button');
      const status = document.createElement('p');
      address.type = 'url';
      address.required = true;
      address.placeholder = 'https://example.org';
      address.autocomplete = 'off';
      open.type = 'submit';
      open.textContent = context.language === 'de' ? 'Öffnen' : 'Open';
      form.append(address, open, status);
      form.addEventListener('submit', (event) => {
        event.preventDefault();
        status.textContent = context.language === 'de' ? 'Sitzung wird gestartet' : 'Starting session';
        void context.invokeCapability('interactive.session', 'create', {
          operationKey: crypto.randomUUID(),
          request: {
            initialUrl: address.value,
            profileMode: 'temporary',
            profileId: null,
          },
        }).then((session) => {
          status.textContent = session.state ?? 'created';
        }).catch(() => {
          status.textContent = context.language === 'de'
            ? 'Browsersitzung konnte nicht gestartet werden.'
            : 'Browser session could not be started.';
        });
      });
      shadow.append(form);
    }
  }

  if (!customElements.get('julos-browser-app')) {
    customElements.define('julos-browser-app', JulOsBrowserApp);
  }
}
