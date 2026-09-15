import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  backDispatcherOrder,
  departedLayers,
  isCurrentEntry,
  leavesJulOs,
  rootHistoryEntry,
  truncateForward,
  type ShellHistoryEntry,
} from './shell-navigation-contract.js';

function entry(sequence: number, kind: ShellHistoryEntry['kind'] = 'overlay'): ShellHistoryEntry {
  return { julos: 1, epoch: 'epoch-1', sequence, kind, token: `token-${sequence}` };
}

test('the dispatcher offers layers outermost first and root last', () => {
  assert.deepEqual(backDispatcherOrder, [
    'overlay',
    'application',
    'detail',
    'task-switcher',
    'root',
  ]);
  assert.equal(backDispatcherOrder.at(-1), 'root');
});

test('the root entry is sequence zero and pushes no guard', () => {
  const root = rootHistoryEntry('epoch-1');

  assert.equal(root.sequence, 0);
  assert.equal(root.kind, 'root');
  assert.equal(root.token, null);
  assert.equal(leavesJulOs(root.sequence), true);
});

test('a stale epoch is not treated as a current JulOS entry', () => {
  assert.equal(isCurrentEntry(entry(1), 'epoch-1'), true);
  assert.equal(isCurrentEntry(entry(1), 'epoch-2'), false);
  assert.equal(isCurrentEntry({ julos: 2, epoch: 'epoch-1', sequence: 1 }, 'epoch-1'), false);
  assert.equal(isCurrentEntry(null, 'epoch-1'), false);
});

test('a multi-entry browser jump unwinds every departed layer, newest first', () => {
  const stack = [entry(1), entry(2), entry(3, 'detail'), entry(4, 'task-switcher')];

  const departed = departedLayers(stack, 1);

  assert.deepEqual(departed.map((item) => item.sequence), [4, 3, 2]);
});

test('a jump to the root departs every layer', () => {
  const stack = [entry(1), entry(2)];

  assert.deepEqual(departedLayers(stack, 0).map((item) => item.sequence), [2, 1]);
});

test('no layer is departed when the target is already current', () => {
  assert.deepEqual(departedLayers([entry(1), entry(2)], 2), []);
});

test('forward navigation truncates abandoned layers', () => {
  const stack = [entry(1), entry(2), entry(3)];

  assert.deepEqual(truncateForward(stack, 1).map((item) => item.sequence), [1]);
});

test('back above the root stays inside JulOS', () => {
  assert.equal(leavesJulOs(1), false);
  assert.equal(leavesJulOs(0), true);
});

test('a history entry carries no secret or runtime descriptor', () => {
  const fields = Object.keys(entry(1)).sort();

  assert.deepEqual(fields, ['epoch', 'julos', 'kind', 'sequence', 'token']);
});
