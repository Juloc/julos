export async function register(context) {
  class JulOsReferenceApp extends HTMLElement {
    connectedCallback() {
      const shadow = this.attachShadow({ mode: 'open' });
      const root = document.createElement('main');
      root.setAttribute('part', 'surface');
      const heading = document.createElement('h1');
      heading.textContent = context.language === 'de' ? 'JulOS Referenzpaket' : 'JulOS Reference Package';
      const status = document.createElement('p');
      status.textContent = context.theme === 'dark' ? 'Dark theme' : 'Light theme';
      root.append(heading, status);
      shadow.append(root);
    }
  }

  class JulOsReferenceWidget extends HTMLElement {
    connectedCallback() {
      const shadow = this.attachShadow({ mode: 'open' });
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = context.language === 'de' ? 'Referenz-App öffnen' : 'Open reference app';
      button.addEventListener('click', () => context.openApplication('reference'));
      shadow.append(button);
    }
  }

  if (!customElements.get('julos-reference-app')) {
    customElements.define('julos-reference-app', JulOsReferenceApp);
  }
  if (!customElements.get('julos-reference-widget')) {
    customElements.define('julos-reference-widget', JulOsReferenceWidget);
  }
}
