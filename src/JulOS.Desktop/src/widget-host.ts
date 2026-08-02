export type WidgetSize = 'small' | 'medium' | 'wide' | 'large';
export type WidgetStatus = 'loading' | 'live' | 'stale' | 'offline' | 'unauthorized' | 'error';

export interface WidgetRegistration {
  readonly widgetId: string;
  readonly packageId: string;
  readonly size: WidgetSize;
}

export interface WidgetSnapshot extends WidgetRegistration {
  readonly status: WidgetStatus;
  readonly observedAtUtc: string | null;
  readonly value: unknown;
}

export class WidgetOwnershipError extends Error {
  public constructor(widgetId: string, packageId: string) {
    super(`Package '${packageId}' does not own widget '${widgetId}'.`);
    this.name = 'WidgetOwnershipError';
  }
}

export class WidgetHostStore {
  readonly #widgets = new Map<string, WidgetSnapshot>();

  public get widgets(): readonly WidgetSnapshot[] {
    return [...this.#widgets.values()].map(cloneWidget);
  }

  public register(registration: WidgetRegistration): WidgetSnapshot {
    validateIdentifier(registration.widgetId, 'widgetId');
    validateIdentifier(registration.packageId, 'packageId');
    if (this.#widgets.has(registration.widgetId)) {
      throw new Error(`Widget '${registration.widgetId}' is already registered.`);
    }

    const widget: WidgetSnapshot = {
      ...registration,
      status: 'loading',
      observedAtUtc: null,
      value: null,
    };
    this.#widgets.set(registration.widgetId, widget);
    return cloneWidget(widget);
  }

  public update(
    packageId: string,
    widgetId: string,
    update: Pick<WidgetSnapshot, 'status' | 'observedAtUtc' | 'value'>,
  ): WidgetSnapshot {
    const current = this.#requireOwned(packageId, widgetId);
    validateObservation(update.status, update.observedAtUtc);
    const updated: WidgetSnapshot = { ...current, ...update };
    this.#widgets.set(widgetId, updated);
    return cloneWidget(updated);
  }

  public remove(packageId: string, widgetId: string): void {
    this.#requireOwned(packageId, widgetId);
    this.#widgets.delete(widgetId);
  }

  #requireOwned(packageId: string, widgetId: string): WidgetSnapshot {
    const current = this.#widgets.get(widgetId);
    if (current === undefined) {
      throw new Error(`Widget '${widgetId}' is not registered.`);
    }
    if (current.packageId !== packageId) {
      throw new WidgetOwnershipError(widgetId, packageId);
    }
    return current;
  }
}

export function widgetObservationLabel(widget: WidgetSnapshot, nowUtc: string): string {
  if (widget.observedAtUtc === null) {
    return widget.status;
  }

  const observed = Date.parse(widget.observedAtUtc);
  const now = Date.parse(nowUtc);
  if (!Number.isFinite(observed) || !Number.isFinite(now)) {
    throw new TypeError('Widget observation timestamps must be valid ISO timestamps.');
  }

  const seconds = Math.max(0, Math.floor((now - observed) / 1000));
  return `${widget.status}; observed ${seconds} seconds ago`;
}

function validateObservation(status: WidgetStatus, observedAtUtc: string | null): void {
  if ((status === 'live' || status === 'stale') && observedAtUtc === null) {
    throw new TypeError(`Widget status '${status}' requires an observation time.`);
  }
  if (observedAtUtc !== null && !Number.isFinite(Date.parse(observedAtUtc))) {
    throw new TypeError('Widget observation time is invalid.');
  }
}

function validateIdentifier(value: string, field: string): void {
  if (value.trim().length === 0 || value !== value.trim()) {
    throw new TypeError(`Widget field '${field}' is invalid.`);
  }
}

function cloneWidget(widget: WidgetSnapshot): WidgetSnapshot {
  return { ...widget };
}
