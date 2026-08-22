import type { DesktopWindowSnapshot, UsableArea, WindowBounds, WindowStore } from './window-store.js';
import type { DesktopApplication } from './shell-api.js';
import { ShellApiClient } from './shell-api.js';

export type DisplayEdge = 'left' | 'right';

export interface WorkspaceDisplay {
  readonly displayId: string;
  readonly startedAt: number;
}

interface WorkspacePeer extends WorkspaceDisplay {
  lastSeenAt: number;
}

interface PresenceMessage {
  readonly type: 'presence';
  readonly senderId: string;
  readonly startedAt: number;
}

interface StateMessage {
  readonly type: 'state';
  readonly senderId: string;
  readonly startedAt: number;
  readonly windows: readonly DesktopWindowSnapshot[];
}

interface ClosedMessage {
  readonly type: 'closed';
  readonly senderId: string;
  readonly startedAt: number;
  readonly windowId: string;
}

interface TransferRequestMessage {
  readonly type: 'transfer-request';
  readonly senderId: string;
  readonly startedAt: number;
  readonly requestId: string;
  readonly targetId: string;
  readonly direction: DisplayEdge;
  readonly normalizedY: number;
  readonly window: DesktopWindowSnapshot;
}

interface TransferReadyMessage {
  readonly type: 'transfer-ready';
  readonly senderId: string;
  readonly startedAt: number;
  readonly requestId: string;
  readonly targetId: string;
  readonly window: DesktopWindowSnapshot;
}

interface TransferRejectedMessage {
  readonly type: 'transfer-rejected';
  readonly senderId: string;
  readonly startedAt: number;
  readonly requestId: string;
  readonly targetId: string;
}

interface TransferCommitMessage {
  readonly type: 'transfer-commit';
  readonly senderId: string;
  readonly startedAt: number;
  readonly requestId: string;
  readonly targetId: string;
  readonly window: DesktopWindowSnapshot;
}

interface LeaveMessage {
  readonly type: 'leave';
  readonly senderId: string;
  readonly startedAt: number;
}

type WorkspaceMessage =
  | PresenceMessage
  | StateMessage
  | ClosedMessage
  | TransferRequestMessage
  | TransferReadyMessage
  | TransferRejectedMessage
  | TransferCommitMessage
  | LeaveMessage;

interface PendingOutboundTransfer {
  readonly targetId: string;
  readonly resolve: (window: DesktopWindowSnapshot | null) => void;
  readonly timer: ReturnType<typeof globalThis.setTimeout>;
}

interface PendingInboundTransfer {
  readonly sourceId: string;
}

interface BroadcastChannelLike {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void;
  close(): void;
}

const channelName = 'julos.desktop.workspace.v1';
const heartbeatMilliseconds = 2_000;
const peerTimeoutMilliseconds = 6_500;
const transferReadyTimeoutMilliseconds = 2_500;
const edgeThresholdPixels = 12;

/**
 * Coordinates browser windows that show the same JulOS Desktop.
 * Durable layout still belongs to the server. This service only coordinates active
 * browser displays, window ownership and handoff between those displays.
 */
export class MultiDisplayWorkspace {
  readonly #enabled: boolean;
  readonly #channel: BroadcastChannelLike | null;
  readonly #api: ShellApiClient;
  readonly #now: () => number;
  readonly #displayId: string;
  readonly #startedAt: number;
  readonly #peers = new Map<string, WorkspacePeer>();
  readonly #owners = new Map<string, string>();
  readonly #snapshots = new Map<string, DesktopWindowSnapshot>();
  readonly #pendingOutbound = new Map<string, PendingOutboundTransfer>();
  readonly #pendingInbound = new Map<string, PendingInboundTransfer>();
  readonly #messageHandler = (event: MessageEvent<unknown>): void => this.#receive(event.data);
  readonly #pageHideHandler = (): void => this.#leave();
  readonly #heartbeatTimer: ReturnType<typeof globalThis.setInterval> | null;
  #store: WindowStore | null = null;
  #unsubscribeStore: (() => void) | null = null;
  #localWindowIds = new Set<string>();
  #applicationCache: readonly DesktopApplication[] | null = null;

  public constructor(
    channelFactory: (() => BroadcastChannelLike) | null = defaultChannelFactory(),
    api = new ShellApiClient(),
    now: () => number = () => Date.now(),
  ) {
    this.#enabled = channelFactory !== null && typeof document !== 'undefined';
    this.#channel = this.#enabled && channelFactory !== null ? channelFactory() : null;
    this.#api = api;
    this.#now = now;
    this.#displayId = createIdentifier();
    this.#startedAt = now();

    if (this.#channel === null) {
      this.#heartbeatTimer = null;
      return;
    }

    this.#channel.addEventListener('message', this.#messageHandler);
    // Neither the channel nor the heartbeat may be the only handle keeping a
    // host process alive (e.g. a Node test runner); unref both where the
    // runtime supports it. In the browser these are no-ops (the channel has no
    // unref and the timer id is a number) so live behavior is unchanged.
    (this.#channel as unknown as { unref?: () => void }).unref?.();
    window.addEventListener('pagehide', this.#pageHideHandler);
    this.#heartbeatTimer = globalThis.setInterval(() => {
      this.#prunePeers();
      this.#announcePresence();
    }, heartbeatMilliseconds);
    (this.#heartbeatTimer as unknown as { unref?: () => void }).unref?.();
    this.#announcePresence();
  }

  public get displayId(): string {
    return this.#displayId;
  }

  public get displayCount(): number {
    this.#prunePeers();
    return 1 + this.#peers.size;
  }

  public attachStore(store: WindowStore): void {
    if (!this.#enabled || this.#store === store) {
      return;
    }

    this.#unsubscribeStore?.();
    this.#store = store;
    this.#localWindowIds.clear();
    this.#unsubscribeStore = store.subscribe((windows) => this.#observeLocalWindows(windows));
    this.#announcePresence();
    this.#broadcastState();
  }

  /** Returns one combined window list for durable layout persistence. */
  public windows(store: WindowStore): readonly DesktopWindowSnapshot[] {
    if (!this.#enabled) {
      return store.windows;
    }

    const localById = new Map(store.windows.map((window) => [window.id, window]));
    const result: Array<{ owner: string; window: DesktopWindowSnapshot }> = [];
    for (const [windowId, owner] of this.#owners) {
      const snapshot = owner === this.#displayId
        ? localById.get(windowId) ?? this.#snapshots.get(windowId)
        : this.#snapshots.get(windowId);
      if (snapshot !== undefined) {
        result.push({ owner, window: cloneWindow(snapshot) });
      }
    }

    for (const window of store.windows) {
      if (!this.#owners.has(window.id)) {
        result.push({ owner: this.#displayId, window: cloneWindow(window) });
      }
    }

    result.sort((left, right) => {
      const displayOrder = compareDisplays(this.#display(left.owner), this.#display(right.owner));
      return displayOrder !== 0 ? displayOrder : left.window.zIndex - right.window.zIndex;
    });
    return result.map(({ window }, index) => ({ ...window, zIndex: index }));
  }

  /** Transfers a normal window when the pointer is released at a connected display edge. */
  public async transferAtEdge(
    windowId: string,
    pointerX: number,
    usableArea: UsableArea,
  ): Promise<boolean> {
    if (!this.#enabled || this.#channel === null || this.#store === null) {
      return false;
    }

    this.#prunePeers();
    const direction = edgeForPointer(pointerX, usableArea, edgeThresholdPixels);
    if (direction === null) {
      return false;
    }

    const current = { displayId: this.#displayId, startedAt: this.#startedAt };
    const target = resolveDisplayTarget(this.#displayId, [...this.#peers.values()], direction, current);
    if (target === null) {
      return false;
    }

    const window = this.#store.windows.find((candidate) => candidate.id === windowId);
    if (window === undefined || window.state !== 'normal') {
      return false;
    }

    const requestId = createIdentifier();
    const prepared = new Promise<DesktopWindowSnapshot | null>((resolve) => {
      const timer = globalThis.setTimeout(() => {
        this.#pendingOutbound.delete(requestId);
        resolve(null);
      }, transferReadyTimeoutMilliseconds);
      this.#pendingOutbound.set(requestId, { targetId: target.displayId, resolve, timer });
    });

    this.#post({
      type: 'transfer-request',
      senderId: this.#displayId,
      startedAt: this.#startedAt,
      requestId,
      targetId: target.displayId,
      direction,
      normalizedY: normalizeWindowY(window.bounds, usableArea),
      window: cloneWindow(window),
    });

    const targetWindow = await prepared;
    if (targetWindow === null) {
      return false;
    }

    this.#owners.set(windowId, target.displayId);
    this.#snapshots.set(windowId, cloneWindow(targetWindow));
    this.#post({
      type: 'transfer-commit',
      senderId: this.#displayId,
      startedAt: this.#startedAt,
      requestId,
      targetId: target.displayId,
      window: cloneWindow(targetWindow),
    });

    // DesktopRuntime still needs the source window for snap/persistence work in this pointerup.
    globalThis.setTimeout(() => this.#closeWindowThroughUi(windowId), 0);
    return true;
  }

  public dispose(): void {
    this.#leave();
    this.#unsubscribeStore?.();
    this.#unsubscribeStore = null;
    this.#store = null;
    if (this.#heartbeatTimer !== null) {
      globalThis.clearInterval(this.#heartbeatTimer);
    }
    for (const pending of this.#pendingOutbound.values()) {
      globalThis.clearTimeout(pending.timer);
      pending.resolve(null);
    }
    this.#pendingOutbound.clear();
    this.#pendingInbound.clear();
    this.#channel?.close();
    if (typeof window !== 'undefined') {
      window.removeEventListener('pagehide', this.#pageHideHandler);
    }
  }

  #observeLocalWindows(windows: readonly DesktopWindowSnapshot[]): void {
    const currentIds = new Set(windows.map((window) => window.id));
    for (const previousId of this.#localWindowIds) {
      if (!currentIds.has(previousId) && this.#owners.get(previousId) === this.#displayId) {
        this.#owners.delete(previousId);
        this.#snapshots.delete(previousId);
        this.#post({
          type: 'closed',
          senderId: this.#displayId,
          startedAt: this.#startedAt,
          windowId: previousId,
        });
      }
    }

    for (const window of windows) {
      const owner = this.#owners.get(window.id);
      if (owner === undefined) {
        this.#owners.set(window.id, this.#displayId);
        this.#snapshots.set(window.id, cloneWindow(window));
      } else if (owner === this.#displayId) {
        this.#snapshots.set(window.id, cloneWindow(window));
      } else {
        globalThis.setTimeout(() => this.#closeWindowThroughUi(window.id), 0);
      }
    }

    this.#localWindowIds = currentIds;
    this.#broadcastState();
  }

  #receive(value: unknown): void {
    const message = parseMessage(value);
    if (message === null || message.senderId === this.#displayId) {
      return;
    }

    this.#touchPeer(message.senderId, message.startedAt);
    switch (message.type) {
      case 'presence':
        this.#broadcastState();
        break;
      case 'state':
        this.#applyPeerState(message);
        break;
      case 'closed':
        if (this.#owners.get(message.windowId) === message.senderId) {
          this.#owners.delete(message.windowId);
          this.#snapshots.delete(message.windowId);
        }
        break;
      case 'transfer-request':
        if (message.targetId === this.#displayId) {
          void this.#prepareInboundTransfer(message);
        }
        break;
      case 'transfer-ready':
        if (message.targetId === this.#displayId) {
          this.#resolveOutbound(message.requestId, message.senderId, message.window);
        }
        break;
      case 'transfer-rejected':
        if (message.targetId === this.#displayId) {
          this.#resolveOutbound(message.requestId, message.senderId, null);
        }
        break;
      case 'transfer-commit':
        if (message.targetId === this.#displayId) {
          this.#commitInboundTransfer(message);
        }
        break;
      case 'leave':
        this.#removePeer(message.senderId);
        break;
    }
  }

  #applyPeerState(message: StateMessage): void {
    const receivedIds = new Set(message.windows.map((window) => window.id));
    for (const window of message.windows) {
      const owner = this.#owners.get(window.id);
      if (owner === undefined || owner === message.senderId) {
        this.#owners.set(window.id, message.senderId);
        this.#snapshots.set(window.id, cloneWindow(window));
        if (this.#localWindowIds.has(window.id)) {
          globalThis.setTimeout(() => this.#closeWindowThroughUi(window.id), 0);
        }
        continue;
      }

      if (owner === this.#displayId && this.#localWindowIds.has(window.id)) {
        const remoteWins = compareDisplays(
          this.#display(message.senderId),
          { displayId: this.#displayId, startedAt: this.#startedAt },
        ) < 0;
        if (remoteWins) {
          this.#owners.set(window.id, message.senderId);
          this.#snapshots.set(window.id, cloneWindow(window));
          globalThis.setTimeout(() => this.#closeWindowThroughUi(window.id), 0);
        } else {
          this.#broadcastState();
        }
        continue;
      }

      if (compareDisplays(this.#display(message.senderId), this.#display(owner)) < 0) {
        this.#owners.set(window.id, message.senderId);
        this.#snapshots.set(window.id, cloneWindow(window));
      }
    }

    for (const [windowId, owner] of [...this.#owners]) {
      if (owner === message.senderId && !receivedIds.has(windowId)) {
        this.#owners.delete(windowId);
        this.#snapshots.delete(windowId);
      }
    }
  }

  async #prepareInboundTransfer(message: TransferRequestMessage): Promise<void> {
    try {
      await this.#ensureApplicationReady(message.window.applicationId);
      const area = currentUsableArea();
      const bounds = placeTransferredWindow(
        message.window.bounds,
        area,
        message.direction,
        message.normalizedY,
      );
      const targetWindow: DesktopWindowSnapshot = {
        ...cloneWindow(message.window),
        state: 'normal',
        bounds,
        restoreBounds: bounds,
      };
      this.#pendingInbound.set(message.requestId, { sourceId: message.senderId });
      this.#post({
        type: 'transfer-ready',
        senderId: this.#displayId,
        startedAt: this.#startedAt,
        requestId: message.requestId,
        targetId: message.senderId,
        window: targetWindow,
      });
    } catch {
      this.#pendingInbound.delete(message.requestId);
      this.#post({
        type: 'transfer-rejected',
        senderId: this.#displayId,
        startedAt: this.#startedAt,
        requestId: message.requestId,
        targetId: message.senderId,
      });
    }
  }

  #commitInboundTransfer(message: TransferCommitMessage): void {
    const pending = this.#pendingInbound.get(message.requestId);
    if (pending === undefined || pending.sourceId !== message.senderId || this.#store === null) {
      return;
    }
    this.#pendingInbound.delete(message.requestId);
    const window = cloneWindow(message.window);
    this.#owners.set(window.id, this.#displayId);
    this.#snapshots.set(window.id, window);

    const existing = this.#store.windows.find((candidate) => candidate.id === window.id);
    if (existing !== undefined) {
      if (existing.state === 'normal') {
        this.#store.setBounds(existing.id, window.bounds);
      }
      this.#store.focus(existing.id);
    } else {
      this.#store.open({
        id: window.id,
        applicationId: window.applicationId,
        launchTargetId: window.launchTargetId,
        title: window.title,
        bounds: window.bounds,
      });
    }
    this.#broadcastState();
  }

  #resolveOutbound(requestId: string, senderId: string, window: DesktopWindowSnapshot | null): void {
    const pending = this.#pendingOutbound.get(requestId);
    if (pending === undefined || pending.targetId !== senderId) {
      return;
    }
    globalThis.clearTimeout(pending.timer);
    this.#pendingOutbound.delete(requestId);
    pending.resolve(window === null ? null : cloneWindow(window));
  }

  async #ensureApplicationReady(applicationId: string): Promise<void> {
    const applications = await this.#applications();
    const application = applications.find((candidate) => candidate.applicationDefinitionId === applicationId);
    if (application === undefined || application.packageId === 'julos.core') {
      return;
    }
    if (customElements.get(application.elementName) !== undefined) {
      return;
    }

    const store = this.#store;
    if (store === null) {
      throw new Error('The target display is not ready.');
    }
    const beforeIds = new Set(store.windows.map((window) => window.id));
    const launcher = findLauncherButton(applicationId);
    if (launcher === null) {
      throw new Error('The target display cannot prepare the application.');
    }
    launcher.click();

    const deadline = this.#now() + transferReadyTimeoutMilliseconds - 250;
    while (customElements.get(application.elementName) === undefined) {
      if (this.#now() >= deadline) {
        throw new Error('The target application did not become ready.');
      }
      await delay(20);
    }

    // Let DesktopRuntime complete its launch continuation before removing the preload window.
    await delay(0);
    for (const window of store.windows) {
      if (!beforeIds.has(window.id) && window.applicationId === applicationId) {
        this.#closeWindowThroughUi(window.id);
      }
    }
    await delay(0);
  }

  async #applications(): Promise<readonly DesktopApplication[]> {
    this.#applicationCache ??= await this.#api.readApplications('desktop');
    return this.#applicationCache;
  }

  #broadcastState(): void {
    if (this.#channel === null || this.#store === null) {
      return;
    }
    this.#post({
      type: 'state',
      senderId: this.#displayId,
      startedAt: this.#startedAt,
      windows: this.#store.windows
        .filter((window) => this.#owners.get(window.id) === this.#displayId)
        .map(cloneWindow),
    });
  }

  #announcePresence(): void {
    this.#post({ type: 'presence', senderId: this.#displayId, startedAt: this.#startedAt });
  }

  #post(message: WorkspaceMessage): void {
    this.#channel?.postMessage(message);
  }

  #touchPeer(displayId: string, startedAt: number): void {
    this.#peers.set(displayId, { displayId, startedAt, lastSeenAt: this.#now() });
  }

  #prunePeers(): void {
    const threshold = this.#now() - peerTimeoutMilliseconds;
    for (const peer of [...this.#peers.values()]) {
      if (peer.lastSeenAt < threshold) {
        this.#removePeer(peer.displayId);
      }
    }
  }

  #removePeer(displayId: string): void {
    if (this.#peers.delete(displayId)) {
      this.#recoverWindows(displayId);
    }
  }

  #recoverWindows(displayId: string): void {
    if (this.#store === null) {
      return;
    }
    const active = [
      { displayId: this.#displayId, startedAt: this.#startedAt },
      ...this.#peers.values(),
    ].sort(compareDisplays);
    if (active[0]?.displayId !== this.#displayId) {
      return;
    }

    for (const [windowId, owner] of [...this.#owners]) {
      if (owner !== displayId) {
        continue;
      }
      const snapshot = this.#snapshots.get(windowId);
      if (snapshot === undefined) {
        this.#owners.delete(windowId);
        continue;
      }
      this.#owners.set(windowId, this.#displayId);
      const bounds = placeTransferredWindow(snapshot.bounds, currentUsableArea(), 'right', 0.15);
      const recovered = { ...snapshot, state: 'normal' as const, bounds, restoreBounds: bounds };
      this.#snapshots.set(windowId, recovered);
      if (!this.#store.windows.some((window) => window.id === windowId)) {
        void this.#ensureApplicationReady(recovered.applicationId)
          .then(() => {
            if (this.#store === null || this.#store.windows.some((window) => window.id === windowId)) {
              return;
            }
            this.#store.open({
              id: recovered.id,
              applicationId: recovered.applicationId,
              launchTargetId: recovered.launchTargetId,
              title: recovered.title,
              bounds: recovered.bounds,
            });
          })
          .catch(() => undefined);
      }
    }
    this.#broadcastState();
  }

  #closeWindowThroughUi(windowId: string): void {
    const root = shellShadowRoot();
    const windowElement = [...(root?.querySelectorAll<HTMLElement>('.desktop-window') ?? [])]
      .find((candidate) => candidate.dataset['windowId'] === windowId);
    const close = windowElement?.querySelector<HTMLButtonElement>('[data-action="close"]');
    if (close !== null && close !== undefined) {
      close.click();
      return;
    }
    const store = this.#store;
    if (store?.windows.some((window) => window.id === windowId) === true) {
      store.close(windowId);
    }
  }

  #display(displayId: string): WorkspaceDisplay {
    if (displayId === this.#displayId) {
      return { displayId, startedAt: this.#startedAt };
    }
    return this.#peers.get(displayId) ?? { displayId, startedAt: Number.MAX_SAFE_INTEGER };
  }

  #leave(): void {
    if (this.#channel !== null) {
      this.#post({ type: 'leave', senderId: this.#displayId, startedAt: this.#startedAt });
    }
  }
}

export function edgeForPointer(
  pointerX: number,
  area: UsableArea,
  threshold = edgeThresholdPixels,
): DisplayEdge | null {
  if (!Number.isFinite(pointerX) || !Number.isFinite(threshold) || threshold < 0) {
    return null;
  }
  if (pointerX <= area.x + threshold) {
    return 'left';
  }
  if (pointerX >= area.x + area.width - threshold) {
    return 'right';
  }
  return null;
}

export function resolveDisplayTarget(
  currentDisplayId: string,
  peers: readonly WorkspaceDisplay[],
  direction: DisplayEdge,
  current: WorkspaceDisplay,
): WorkspaceDisplay | null {
  const displays = [current, ...peers]
    .filter((display, index, values) =>
      values.findIndex((candidate) => candidate.displayId === display.displayId) === index)
    .sort(compareDisplays);
  const index = displays.findIndex((display) => display.displayId === currentDisplayId);
  if (index < 0) {
    return null;
  }
  return direction === 'left' ? displays[index - 1] ?? null : displays[index + 1] ?? null;
}

export function placeTransferredWindow(
  bounds: WindowBounds,
  area: UsableArea,
  direction: DisplayEdge,
  normalizedY: number,
): WindowBounds {
  const width = Math.min(bounds.width, area.width);
  const height = Math.min(bounds.height, area.height);
  const inset = Math.min(24, Math.max(0, area.width - width));
  const x = direction === 'right'
    ? area.x + inset
    : area.x + area.width - width - inset;
  const availableY = Math.max(0, area.height - height);
  return {
    x,
    y: area.y + availableY * clamp(normalizedY, 0, 1),
    width,
    height,
  };
}

function normalizeWindowY(bounds: WindowBounds, area: UsableArea): number {
  const availableY = Math.max(1, area.height - Math.min(bounds.height, area.height));
  return clamp((bounds.y - area.y) / availableY, 0, 1);
}

function compareDisplays(left: WorkspaceDisplay, right: WorkspaceDisplay): number {
  return left.startedAt - right.startedAt || left.displayId.localeCompare(right.displayId);
}

function cloneWindow(window: DesktopWindowSnapshot): DesktopWindowSnapshot {
  return { ...window, bounds: { ...window.bounds }, restoreBounds: { ...window.restoreBounds } };
}

function defaultChannelFactory(): (() => BroadcastChannelLike) | null {
  if (typeof BroadcastChannel === 'undefined' || typeof document === 'undefined') {
    return null;
  }
  return () => new BroadcastChannel(channelName);
}

function currentUsableArea(): UsableArea {
  const layer = shellShadowRoot()?.getElementById('window-layer');
  return {
    x: 0,
    y: 0,
    width: Math.max(layer?.clientWidth ?? (typeof window !== 'undefined' ? window.innerWidth : 320), 320),
    height: Math.max(layer?.clientHeight ?? (typeof window !== 'undefined' ? window.innerHeight : 240), 240),
  };
}

function shellShadowRoot(): ShadowRoot | null {
  if (typeof document === 'undefined') {
    return null;
  }
  return document.querySelector<HTMLElement>('julos-shell')?.shadowRoot ?? null;
}

function findLauncherButton(applicationId: string): HTMLButtonElement | null {
  for (const button of shellShadowRoot()?.querySelectorAll<HTMLButtonElement>('#application-launcher-entries button') ?? []) {
    if (button.dataset['applicationId'] === applicationId && button.dataset['launchTargetId'] === undefined) {
      return button;
    }
  }
  return null;
}

function parseMessage(value: unknown): WorkspaceMessage | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const candidate = value as Record<string, unknown>;
  if (
    typeof candidate['type'] !== 'string'
    || typeof candidate['senderId'] !== 'string'
    || typeof candidate['startedAt'] !== 'number'
    || !Number.isFinite(candidate['startedAt'])
  ) {
    return null;
  }

  switch (candidate['type']) {
    case 'presence':
    case 'leave':
      return candidate as unknown as PresenceMessage | LeaveMessage;
    case 'state':
      return Array.isArray(candidate['windows']) ? candidate as unknown as StateMessage : null;
    case 'closed':
      return typeof candidate['windowId'] === 'string' ? candidate as unknown as ClosedMessage : null;
    case 'transfer-request':
      return hasTransferFields(candidate) && (candidate['direction'] === 'left' || candidate['direction'] === 'right')
        && typeof candidate['normalizedY'] === 'number'
        ? candidate as unknown as TransferRequestMessage
        : null;
    case 'transfer-ready':
      return hasTransferFields(candidate) ? candidate as unknown as TransferReadyMessage : null;
    case 'transfer-rejected':
      return typeof candidate['requestId'] === 'string' && typeof candidate['targetId'] === 'string'
        ? candidate as unknown as TransferRejectedMessage
        : null;
    case 'transfer-commit':
      return hasTransferFields(candidate) ? candidate as unknown as TransferCommitMessage : null;
    default:
      return null;
  }
}

function hasTransferFields(candidate: Record<string, unknown>): boolean {
  return typeof candidate['requestId'] === 'string'
    && typeof candidate['targetId'] === 'string'
    && typeof candidate['window'] === 'object'
    && candidate['window'] !== null;
}

function createIdentifier(): string {
  return globalThis.crypto?.randomUUID?.() ?? `display-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => globalThis.setTimeout(resolve, milliseconds));
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}

export const desktopMultiDisplayWorkspace = new MultiDisplayWorkspace();
