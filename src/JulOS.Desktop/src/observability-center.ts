export type ProblemSeverity = 'information' | 'warning' | 'error' | 'critical';
export type ProblemState = 'active' | 'acknowledged' | 'resolved';

export interface ProblemObservation {
  readonly identity: string;
  readonly severity: ProblemSeverity;
  readonly title: string;
  readonly sourcePackage: string;
  readonly resourceId: string | null;
  readonly observedAtUtc: string;
  readonly deepLink: string | null;
}

export interface ProblemCenterItem extends ProblemObservation {
  readonly state: ProblemState;
  readonly observationCount: number;
  readonly firstObservedAtUtc: string;
  readonly lastObservedAtUtc: string;
}

export interface NotificationObservation {
  readonly id: string;
  readonly deduplicationKey: string;
  readonly title: string;
  readonly body: string;
  readonly observedAtUtc: string;
  readonly deepLink: string | null;
}

export interface NotificationItem extends NotificationObservation {
  readonly repeatCount: number;
}

export class ObservabilityCenter {
  readonly #problems = new Map<string, ProblemCenterItem>();
  readonly #notifications = new Map<string, NotificationItem>();

  public get problems(): readonly ProblemCenterItem[] {
    return [...this.#problems.values()].sort((left, right) =>
      right.lastObservedAtUtc.localeCompare(left.lastObservedAtUtc));
  }

  public get notifications(): readonly NotificationItem[] {
    return [...this.#notifications.values()].sort((left, right) =>
      right.observedAtUtc.localeCompare(left.observedAtUtc));
  }

  public observeProblem(observation: ProblemObservation): ProblemCenterItem {
    validateIdentity(observation.identity, 'problem identity');
    validateIsoTimestamp(observation.observedAtUtc);
    const current = this.#problems.get(observation.identity);
    const updated: ProblemCenterItem = current === undefined
      ? {
          ...observation,
          state: 'active',
          observationCount: 1,
          firstObservedAtUtc: observation.observedAtUtc,
          lastObservedAtUtc: observation.observedAtUtc,
        }
      : {
          ...current,
          ...observation,
          state: current.state === 'resolved' ? 'active' : current.state,
          observationCount: current.observationCount + 1,
          firstObservedAtUtc: current.firstObservedAtUtc,
          lastObservedAtUtc: observation.observedAtUtc,
        };
    this.#problems.set(observation.identity, updated);
    return updated;
  }

  public setProblemState(identity: string, state: ProblemState): ProblemCenterItem {
    const current = this.#problems.get(identity);
    if (current === undefined) {
      throw new Error(`Problem '${identity}' does not exist.`);
    }
    const updated = { ...current, state };
    this.#problems.set(identity, updated);
    return updated;
  }

  public observeNotification(observation: NotificationObservation): NotificationItem {
    validateIdentity(observation.deduplicationKey, 'notification deduplication key');
    validateIsoTimestamp(observation.observedAtUtc);
    const current = this.#notifications.get(observation.deduplicationKey);
    const updated: NotificationItem = current === undefined
      ? { ...observation, repeatCount: 1 }
      : {
          ...observation,
          id: current.id,
          repeatCount: current.repeatCount + 1,
        };
    this.#notifications.set(observation.deduplicationKey, updated);
    return updated;
  }

  public dismissNotification(deduplicationKey: string): void {
    this.#notifications.delete(deduplicationKey);
  }
}

export function severityLabel(severity: ProblemSeverity): string {
  switch (severity) {
    case 'information':
      return 'Information';
    case 'warning':
      return 'Warning';
    case 'error':
      return 'Error';
    case 'critical':
      return 'Critical';
  }
}

function validateIdentity(value: string, field: string): void {
  if (value.trim().length === 0 || value !== value.trim()) {
    throw new TypeError(`The ${field} is invalid.`);
  }
}

function validateIsoTimestamp(value: string): void {
  if (!Number.isFinite(Date.parse(value))) {
    throw new TypeError(`Timestamp '${value}' is invalid.`);
  }
}
