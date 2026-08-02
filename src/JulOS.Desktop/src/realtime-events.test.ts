import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  RealtimeEventService,
  splitSignalRFrames,
  type RealtimeConnection,
  type RealtimeEventEnvelope,
} from './realtime-events.js';

class FakeConnection implements RealtimeConnection {
  #eventHandler: (event: RealtimeEventEnvelope) => void | Promise<void> = () => undefined;
  #reconnectedHandler: () => void | Promise<void> = () => undefined;

  public started = false;
  public stopped = false;

  public setEventHandler(handler: (event: RealtimeEventEnvelope) => void | Promise<void>): void {
    this.#eventHandler = handler;
  }

  public setReconnectedHandler(handler: () => void | Promise<void>): void {
    this.#reconnectedHandler = handler;
  }

  public async start(): Promise<void> {
    this.started = true;
  }

  public async stop(): Promise<void> {
    this.stopped = true;
  }

  public async emit(event: RealtimeEventEnvelope): Promise<void> {
    await this.#eventHandler(event);
  }

  public async reconnect(): Promise<void> {
    await this.#reconnectedHandler();
  }
}

const event: RealtimeEventEnvelope = {
  eventId: '0198f5c1-a0f0-7000-8000-000000000001',
  eventType: 'problem.changed',
  contractVersion: 1,
  occurredAtUtc: '2026-08-02T20:00:00Z',
  correlationId: 'api010-test',
  resourceId: 'problem-1',
  revision: 3,
  payload: {},
};

test('duplicate delivery changes client state once', async () => {
  const connection = new FakeConnection();
  const received: RealtimeEventEnvelope[] = [];
  const service = new RealtimeEventService(
    connection,
    (receivedEvent) => received.push(receivedEvent),
    () => undefined,
  );

  await service.start();
  await connection.emit(event);
  await connection.emit(event);

  assert.equal(connection.started, true);
  assert.deepEqual(received, [event]);
});

test('a reconnect requests one authoritative API refresh', async () => {
  const connection = new FakeConnection();
  let refreshCount = 0;
  const service = new RealtimeEventService(
    connection,
    () => undefined,
    () => {
      refreshCount += 1;
    },
  );

  await service.start();
  await connection.reconnect();
  assert.equal(refreshCount, 1);

  await service.stop();
  assert.equal(connection.stopped, true);
});

test('SignalR records are split at the protocol separator', () => {
  assert.deepEqual(splitSignalRFrames('{}\u001e{"type":6}\u001e'), ['{}', '{"type":6}']);
});

test('an unknown event contract does not mutate client state', async () => {
  const connection = new FakeConnection();
  let received = false;
  const service = new RealtimeEventService(
    connection,
    () => {
      received = true;
    },
    () => undefined,
  );

  await service.start();
  await connection.emit({ ...event, contractVersion: 2 });
  assert.equal(received, false);
});
