export async function register(context) {
  const read = async () => context.invokeCapability('host.metrics.read', 'latest', {});

  class JulOsHostMetricsApp extends HTMLElement {
    connectedCallback() {
      const shadow = this.attachShadow({ mode: 'open' });
      const surface = document.createElement('main');
      const heading = document.createElement('h1');
      const status = document.createElement('p');
      const list = document.createElement('dl');
      heading.textContent = context.language === 'de' ? 'Host-Metriken' : 'Host metrics';
      status.textContent = context.language === 'de' ? 'Wird geladen' : 'Loading';
      surface.append(heading, status, list);
      shadow.append(surface);
      void read().then((snapshot) => {
        renderMetrics(list, snapshot);
        status.textContent = snapshot.stale
          ? (context.language === 'de' ? 'Veraltete Messung' : 'Stale observation')
          : (context.language === 'de' ? 'Aktuell' : 'Current');
      }).catch(() => {
        status.textContent = context.language === 'de'
          ? 'Metriken sind offline oder nicht erlaubt.'
          : 'Metrics are offline or unauthorized.';
      });
    }
  }

  class JulOsHostMetricsWidget extends HTMLElement {
    connectedCallback() {
      const shadow = this.attachShadow({ mode: 'open' });
      const button = document.createElement('button');
      const value = document.createElement('strong');
      const label = document.createElement('span');
      button.type = 'button';
      value.textContent = '—';
      label.textContent = context.language === 'de' ? 'CPU unbekannt' : 'CPU unknown';
      button.append(value, label);
      button.addEventListener('click', () => context.openApplication('host-metrics'));
      shadow.append(button);
      void read().then((snapshot) => {
        const cpu = snapshot.metrics?.find((metric) => metric.name === 'host.cpu.utilization');
        if (typeof cpu?.value === 'number') {
          value.textContent = `${Math.round(cpu.value * 100)}%`;
          label.textContent = snapshot.stale
            ? (context.language === 'de' ? 'CPU · veraltet' : 'CPU · stale')
            : 'CPU';
        }
      }).catch(() => {
        label.textContent = context.language === 'de' ? 'Agent offline' : 'Agent offline';
      });
    }
  }

  if (!customElements.get('julos-host-metrics-app')) {
    customElements.define('julos-host-metrics-app', JulOsHostMetricsApp);
  }
  if (!customElements.get('julos-host-metrics-widget')) {
    customElements.define('julos-host-metrics-widget', JulOsHostMetricsWidget);
  }
}

function renderMetrics(list, snapshot) {
  list.replaceChildren();
  for (const metric of snapshot.metrics ?? []) {
    const term = document.createElement('dt');
    const value = document.createElement('dd');
    term.textContent = metric.name;
    value.textContent = metric.value === null ? 'Unknown' : `${metric.value} ${metric.unit}`;
    list.append(term, value);
  }
}
