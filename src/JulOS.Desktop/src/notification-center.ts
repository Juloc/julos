import type { RealtimeEventEnvelope } from './realtime-events.js';

export type NotificationSeverity = 'information' | 'success' | 'warning' | 'error';
export type ProblemState = 'active' | 'resolved';

export interface ShellNotification {
  readonly notificationId: string;
  readonly deduplicationKey: string;
  readonly severity: NotificationSeverity;
  readonly title: string;
  readonly message: string;
  readonly occurredAtUtc: string;
  readonly sourcePackageId: string | null;
  readonly deepLink: string | null;
  readonly count: number;
  readonly acknowledged: boolean;
}

export interface ShellProblem {
  readonly problemId: string;
  readonly conditionKey: string;
  readonly severity: NotificationSeverity;
  readonly title: string;
  readonly detail: string;
  readonly state: ProblemState;
  readonly firstObservedAtUtc: string;
  readonly lastObservedAtUtc: string;
  readonly sourcePackageId: string | null;
  readonly resourceId: string | null;
  readonly deepLink: string | null;
}

export interface NotificationCenterSnapshot {
  readonly notifications: readonly ShellNotification[];
  readonly activeProblems: readonly ShellProblem[];
  readonly resolvedProblems: readonly ShellProblem[];
  readonly unreadCount: number;
}

export type NotificationCenterListener = (snapshot: NotificationCenterSnapshot) => void;

/** Deduplicates transient notifications and keeps one problem per stable condition. */
export class NotificationCenterStore {
  readonly #notifications = new Map<string, ShellNotification>();
  readonly #problems = new Map<string, ShellProblem>();
  readonly #listeners = new Set<NotificationCenterListener>();
  readonly #maximumNotifications: number;

  public constructor(maximumNotifications = 500) {
    if (!Number.isInteger(maximumNotifications) || maximumNotifications < 1) {
      throw new RangeError('Notification retention must be positive.');
    }
    this.#maximumNotifications = maximumNotifications;
  }

  public subscribe(listener: NotificationCenterListener): () => void {
    this.#listeners.add(listener);
    listener(this.snapshot());
    return () => this.#listeners.delete(listener);
  }

  public addNotification(notification: Omit<ShellNotification, 'count' | 'acknowledged'>): void {
    validateNotification(notification);
    const existing = this.#notifications.get(notification.deduplicationKey);
    this.#notifications.set(notification.deduplicationKey, existing === undefined
      ? { ...notification, count: 1, acknowledged: false }
      : {
          ...notification,
          notificationId: existing.notificationId,
          count: existing.count + 1,
          acknowledged: false,
        });
    this.#trimNotifications();
    this.#publish();
  }

  public upsertProblem(problem: ShellProblem): void {
    validateProblem(problem);
    const existing = this.#problems.get(problem.conditionKey);
    this.#problems.set(problem.conditionKey, existing === undefined
      ? problem
      : {
          ...problem,
          problemId: existing.problemId,
          firstObservedAtUtc: existing.firstObservedAtUtc,
        });
    this.#publish();
  }

  public resolveProblem(conditionKey: string, observedAtUtc: string): void {
    const problem = this.#problems.get(conditionKey);
    if (problem === undefined) {
      return;
    }
    this.#problems.set(conditionKey, {
      ...problem,
      state: 'resolved',
      lastObservedAtUtc: observedAtUtc,
    });
    this.#publish();
  }

  public acknowledge(notificationId: string): void {
    for (const [key, notification] of this.#notifications) {
      if (notification.notificationId === notificationId) {
        this.#notifications.set(key, { ...notification, acknowledged: true });
        this.#publish();
        return;
      }
    }
  }

  public acknowledgeAll(): void {
    for (const [key, notification] of this.#notifications) {
      this.#notifications.set(key, { ...notification, acknowledged: true });
    }
    this.#publish();
  }

  public ingestEvent(event: RealtimeEventEnvelope): void {
    if (event.eventType === 'problem.changed' && isProblemPayload(event.payload)) {
      this.upsertProblem(event.payload);
    } else if (event.eventType === 'problem.resolved' && isResolvedPayload(event.payload)) {
      this.resolveProblem(event.payload.conditionKey, event.occurredAtUtc);
    } else if (event.eventType === 'notification.created' && isNotificationPayload(event.payload)) {
      this.addNotification(event.payload);
    }
  }

  public snapshot(): NotificationCenterSnapshot {
    const notifications = [...this.#notifications.values()]
      .sort((left, right) => right.occurredAtUtc.localeCompare(left.occurredAtUtc));
    const problems = [...this.#problems.values()]
      .sort((left, right) => right.lastObservedAtUtc.localeCompare(left.lastObservedAtUtc));
    return {
      notifications,
      activeProblems: problems.filter((problem) => problem.state === 'active'),
      resolvedProblems: problems.filter((problem) => problem.state === 'resolved'),
      unreadCount: notifications.filter((notification) => !notification.acknowledged).length,
    };
  }

  #trimNotifications(): void {
    const ordered = [...this.#notifications.entries()]
      .sort((left, right) => right[1].occurredAtUtc.localeCompare(left[1].occurredAtUtc));
    for (const [key] of ordered.slice(this.#maximumNotifications)) {
      this.#notifications.delete(key);
    }
  }

  #publish(): void {
    const snapshot = this.snapshot();
    for (const listener of this.#listeners) {
      listener(snapshot);
    }
  }
}

function validateNotification(notification: Omit<ShellNotification, 'count' | 'acknowledged'>): void {
  validateText(notification.notificationId);
  validateText(notification.deduplicationKey);
  validateText(notification.title);
  validateText(notification.message);
  validateTimestamp(notification.occurredAtUtc);
  validateDeepLink(notification.deepLink);
}

function validateProblem(problem: ShellProblem): void {
  validateText(problem.problemId);
  validateText(problem.conditionKey);
  validateText(problem.title);
  validateText(problem.detail);
  validateTimestamp(problem.firstObservedAtUtc);
  validateTimestamp(problem.lastObservedAtUtc);
  validateDeepLink(problem.deepLink);
}

function validateText(value: string): void {
  if (value.trim().length === 0 || value !== value.trim() || value.length > 4096) {
    throw new TypeError('Notification center text is invalid.');
  }
}

function validateTimestamp(value: string): void {
  if (!Number.isFinite(Date.parse(value))) {
    throw new TypeError('Notification center timestamp is invalid.');
  }
}

function validateDeepLink(value: string | null): void {
  if (value !== null && (!value.startsWith('/') || value.startsWith('//'))) {
    throw new TypeError('Notification deep links must remain same-origin.');
  }
}

function isProblemPayload(value: unknown): value is ShellProblem {
  return isRecord(value)
    && typeof value['problemId'] === 'string'
    && typeof value['conditionKey'] === 'string'
    && typeof value['severity'] === 'string'
    && typeof value['title'] === 'string'
    && typeof value['detail'] === 'string'
    && value['state'] === 'active'
    && typeof value['firstObservedAtUtc'] === 'string'
    && typeof value['lastObservedAtUtc'] === 'string';
}

function isResolvedPayload(value: unknown): value is { readonly conditionKey: string } {
  return isRecord(value) && typeof value['conditionKey'] === 'string';
}

function isNotificationPayload(
  value: unknown,
): value is Omit<ShellNotification, 'count' | 'acknowledged'> {
  return isRecord(value)
    && typeof value['notificationId'] === 'string'
    && typeof value['deduplicationKey'] === 'string'
    && typeof value['severity'] === 'string'
    && typeof value['title'] === 'string'
    && typeof value['message'] === 'string'
    && typeof value['occurredAtUtc'] === 'string';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
