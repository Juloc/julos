export async function register(context) {
  /**
   * The reference application is also the Surface conformance app.
   *
   * It deliberately owns work that a suspended Surface must stop: an interval that ticks a
   * counter into the DOM. That makes "a suspended Surface performs no rendering activity"
   * something the Shell can measure rather than assume, and it gives the platform a way to
   * detect a package that ignores `suspend`.
   */
  class JulOsReferenceApp extends HTMLElement {
    #root = null;
    #tick = null;
    #ticker = null;
    #ticks = 0;
    #depth = 0;
    #disposed = false;

    connectedCallback() {
      if (this.shadowRoot !== null) {
        return;
      }

      const shadow = this.attachShadow({ mode: 'open' });
      const root = document.createElement('main');
      root.setAttribute('part', 'surface');

      const heading = document.createElement('h1');
      heading.textContent = context.language === 'de' ? 'JulOS Referenzpaket' : 'JulOS Reference Package';

      const status = document.createElement('p');
      status.textContent = context.theme === 'dark' ? 'Dark theme' : 'Light theme';

      this.#tick = document.createElement('p');
      this.#tick.dataset.role = 'tick';
      this.#tick.textContent = '0';

      const deeper = document.createElement('button');
      deeper.type = 'button';
      deeper.textContent = context.language === 'de' ? 'Ebene öffnen' : 'Open a level';
      deeper.addEventListener('click', () => { this.#depth += 1; });

      root.append(heading, status, this.#tick, deeper);
      shadow.append(root);
      this.#root = root;
    }

    // --- Surface contract, docs/MOBILE_PWA.md section 10 ---

    async activate(surfaceContext) {
      this.#requireLive();
      // Only a focused Surface animates. A visible but unfocused pane stays rendered and
      // still, which is what keeps a phone split from running two tickers.
      if (surfaceContext.presentation === 'focused') {
        this.#startTicking();
      } else {
        this.#stopTicking();
      }
    }

    async deactivate() {
      this.#requireLive();
      // Losing visible presentation is not disposal: the DOM stays, the work stops.
      this.#stopTicking();
    }

    async suspend() {
      this.#requireLive();
      this.#stopTicking();
    }

    async resume() {
      this.#requireLive();
      // A resumed Surface re-reads authoritative state instead of trusting what it had.
      this.#ticks = 0;
      if (this.#tick !== null) {
        this.#tick.textContent = '0';
      }
    }

    async handleBack() {
      this.#requireLive();
      if (this.#depth === 0) {
        return 'not-handled';
      }
      this.#depth -= 1;
      return 'handled';
    }

    async dispose() {
      this.#stopTicking();
      this.#root?.remove();
      this.#root = null;
      this.#tick = null;
      this.#disposed = true;
    }

    #startTicking() {
      if (this.#ticker !== null) {
        return;
      }
      this.#ticker = globalThis.setInterval(() => {
        this.#ticks += 1;
        if (this.#tick !== null) {
          this.#tick.textContent = String(this.#ticks);
        }
      }, 250);
    }

    #stopTicking() {
      if (this.#ticker === null) {
        return;
      }
      globalThis.clearInterval(this.#ticker);
      this.#ticker = null;
    }

    #requireLive() {
      if (this.#disposed) {
        throw new Error('package.surface_terminated');
      }
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
