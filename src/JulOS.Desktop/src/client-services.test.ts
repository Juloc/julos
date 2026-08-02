import assert from 'node:assert/strict';
import { test } from 'node:test';

import { DesktopClientServices } from './client-services.js';
import type { RealtimeConnection, RealtimeEventEnvelope } from './realtime-events.js';

class FakeConnection implements RealtimeConnection {
  #eventHandler: (event: RealtimeEventEnvelope) => void | Promise<void> = () => undefined;
  #reconnectedHandler: () => void | Promise<void> = () => undefined;

  public startCount = 0;
  public stopCount = 0;

  public setEventHandler(handler: (event: RealtimeEventEnvelope) => void | Promise<void>): void {
    this.#eventHandler = handler;
  }

  public setReconnectedHandler(handler: () => void | Promise<void>): void {
    this.#reconnectedHandler = handler;
  }

  public async start(): Promise<void> {
    this.startCount += 1;
  }

  public async stop(): Promise<void> {
    this.stopCount += 1;
  }

  public async reconnect(): Promise<void> {
    await this.#reconnectedHandler();
  }

  public async emit(event: RealtimeEventEnvelope): Promise<void> {
    await this.#eventHandler(event);
  }
}

test('start loads authoritative API state before opening realtime delivery', async () => {
  const sequence: string[] = [];
  const connection = new FakeConnection();
  const services = new DesktopClientServices(
    connection,
    () => sequence.push('event'),
    () => sequence.push('refresh'),
  );

  await services.start();
  await services.start();

  assert.deepEqual(sequence, ['refresh']);
  assert.equal(connection.startCount, 1);
});

test('a successful reconnect refreshes authoritative API state', async () => {
  let refreshCount = 0;
  const connection = new FakeConnection();
  const services = new DesktopClientServices(
    connection,
    () => undefined,
    () => {
      refreshCount += 1;
    },
  );

  await services.start();
  await connection.reconnect();

  assert.equal(refreshCount, 2);
  await services.stop();
  assert.equal(connection.stopCount, 1);
});

test('event delivery remains deduplicated through the coordinated service', async () => {
  const connection = new FakeConnection();
  let eventCount = 0;
  const services = new DesktopClientServices(
    connection,
    () => {
      eventCount += 1;
    },
    () => undefined,
  );
  const event: RealtimeEventEnvelope = {
    eventId: '0198f5c1-a0f0-7000-8000-000000000101',
    eventType: 'profile.changed',
    contractVersion: 1,
    occurredAtUtc: '2026-08-02T21:00:00Z',
    correlationId: 'desk002-test',
    resourceId: 'profile-1',
    revision: 2,
    payload: {},
  };

  await services.start();
  await connection.emit(event);
  await connection.emit(event);
  assert.equal(eventCount, 1);
});
