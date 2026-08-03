export async function register(context) {
  class JulOsRemoteApp extends HTMLElement {
    connectedCallback() {
      const shadow = this.attachShadow({ mode: 'open' });
      const form = document.createElement('form');
      const target = input('target', context.language === 'de' ? 'Ziel' : 'Target');
      const user = input('userName', context.language === 'de' ? 'Benutzer' : 'User');
      const secretReference = input(
        'secretReferenceId',
        context.language === 'de' ? 'Secret-Referenz' : 'Secret reference',
      );
      const protocol = document.createElement('select');
      for (const value of ['rdp', 'ssh', 'vnc']) {
        const option = document.createElement('option');
        option.value = value;
        option.textContent = value.toUpperCase();
        protocol.append(option);
      }
      const submit = document.createElement('button');
      const status = document.createElement('p');
      submit.type = 'submit';
      submit.textContent = context.language === 'de' ? 'Verbinden' : 'Connect';
      form.append(protocol, target.label, user.label, secretReference.label, submit, status);
      form.addEventListener('submit', (event) => {
        event.preventDefault();
        status.textContent = context.language === 'de' ? 'Verbindung wird erstellt' : 'Creating session';
        void context.invokeCapability('remote.session', 'create', {
          protocol: protocol.value,
          target: target.control.value,
          userName: user.control.value,
          secretReferenceId: secretReference.control.value,
        }).then((session) => {
          status.textContent = session.state ?? 'created';
        }).catch(() => {
          status.textContent = context.language === 'de'
            ? 'Verbindung fehlgeschlagen oder nicht erlaubt.'
            : 'Connection failed or is not permitted.';
        });
      });
      shadow.append(form);
    }
  }

  class JulOsRemoteWidget extends HTMLElement {
    connectedCallback() {
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

function input(name, title) {
  const label = document.createElement('label');
  const control = document.createElement('input');
  const text = document.createElement('span');
  control.name = name;
  control.required = true;
  control.autocomplete = 'off';
  text.textContent = title;
  label.append(text, control);
  return { label, control };
}
