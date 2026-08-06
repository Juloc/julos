export async function register(context) {
  const read = async () => context.invokeCapability('host.metrics.read', 'latest', {});

  class JulOsHostMetricsApp extends HTMLElement {
    connectedCallback() {
      const shadow = this.attachShadow({ mode: 'open' });
      const surface = document.createElement('main');
      const heading = document.createElement('h1');
      const status = document.createElement('p');
      const refresh = document.createElement('button');
      const list = document.createElement('dl');
      heading.textContent = context.language === 'de' ? 'Host-Metriken' : 'Host metrics';
      refresh.type = 'button';
      refresh.textContent = context.language === 'de' ? 'Aktualisieren' : 'Refresh';
      surface.append(heading, status, refresh, list);
      shadow.append(surface);

      const load = async () => {
        status.textContent = context.language === 'de' ? 'Wird geladen' : 'Loading';
        refresh.disabled = true;
        try {
          const snapshot = await read();
          renderMetrics(list, snapshot);
          status.textContent = snapshotStatusText(snapshot, context.language);
        } catch {
          list.replaceChildren();
          status.textContent = snapshotErrorText(context.language);
        } finally {
          refresh.disabled = false;
        }
      };

      refresh.addEventListener('click', () => void load());
      void load();
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
        const summary = cpuWidgetSummary(snapshot, context.language);
        value.textContent = summary.value;
        label.textContent = summary.label;
      }).catch(() => {
        value.textContent = '—';
        label.textContent = snapshotErrorText(context.language);
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

export function snapshotStatusText(snapshot, language) {
  const german = language === 'de';
  switch (snapshot?.state) {
    case 'live':
      return german ? 'Aktuell' : 'Current';
    case 'stale':
      return german ? 'Veraltete Messung' : 'Stale observation';
    case 'offline':
      return german ? 'Agent offline' : 'Agent offline';
    case 'unavailable':
      return german ? 'Noch keine Messwerte' : 'No observations yet';
    default:
      return snapshotErrorText(language);
  }
}

export function snapshotErrorText(language) {
  return language === 'de'
    ? 'Metriken sind nicht verfügbar oder nicht erlaubt.'
    : 'Metrics are unavailable or unauthorized.';
}

export function cpuWidgetSummary(snapshot, language) {
  const cpu = snapshot?.metrics?.find((metric) => metric.name === 'host.cpu.utilization');
  const status = snapshotStatusText(snapshot, language);
  if (typeof cpu?.value !== 'number') {
    return {
      value: '—',
      label: status,
    };
  }

  return {
    value: `${Math.round(cpu.value * 100)}%`,
    label: snapshot?.state === 'live'
      ? 'CPU'
      : `CPU · ${status}`,
  };
}

function renderMetrics(list, snapshot) {
  list.replaceChildren();
  for (const metric of snapshot?.metrics ?? []) {
    const term = document.createElement('dt');
    const value = document.createElement('dd');
    term.textContent = metric.name;
    value.textContent = metric.value === null ? 'Unknown' : `${metric.value} ${metric.unit}`;
    list.append(term, value);
  }
}
