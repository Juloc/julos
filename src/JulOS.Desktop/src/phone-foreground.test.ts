import assert from 'node:assert/strict';
import { test } from 'node:test';

import { PhoneForegroundController, PhoneForegroundError } from './phone-foreground.js';

test('opening an application replaces the visible stage in single mode', () => {
  const foreground = new PhoneForegroundController();

  const first = foreground.show('a');
  assert.equal(first.replacedWindowId, null);
  assert.equal(first.foreground.primaryWindowId, 'a');

  const second = foreground.show('b');
  assert.equal(second.foreground.primaryWindowId, 'b');
  assert.equal(second.foreground.secondaryWindowId, null);
  assert.equal(
    second.replacedWindowId,
    'a',
    'The replaced window is reported so it can take its background execution state.');
});

test('a split begins only through an explicit request', () => {
  const foreground = new PhoneForegroundController();
  foreground.show('a');

  foreground.show('b');
  assert.equal(foreground.state().secondaryWindowId, null, 'Opening an app never infers a split.');

  const split = foreground.showInSplit('c');
  assert.equal(split.foreground.primaryWindowId, 'b');
  assert.equal(split.foreground.secondaryWindowId, 'c');
  assert.equal(split.foreground.splitRatioPermille, 500);
});

test('focus belongs to exactly one pane', () => {
  const foreground = new PhoneForegroundController();
  foreground.show('a');
  foreground.showInSplit('b');

  assert.equal(foreground.focusedPane, 'secondary', 'Opening into the split focuses it.');
  foreground.focus('primary');
  assert.equal(foreground.focusedPane, 'primary');
  assert.equal(foreground.focusedWindowId, 'a');
});

test('a third application replaces the focused pane and leaves the other alone', () => {
  const foreground = new PhoneForegroundController();
  foreground.show('a');
  foreground.showInSplit('b');
  foreground.focus('primary');

  const change = foreground.show('c');

  assert.equal(change.foreground.primaryWindowId, 'c');
  assert.equal(change.foreground.secondaryWindowId, 'b', 'The unfocused pane is untouched.');
  assert.equal(change.replacedWindowId, 'a');
});

test('showing a window that is already in the foreground only moves focus', () => {
  const foreground = new PhoneForegroundController();
  foreground.show('a');
  foreground.showInSplit('b');
  foreground.focus('primary');

  const change = foreground.show('b');

  assert.equal(change.replacedWindowId, null, 'Nothing is pushed out when nothing is replaced.');
  assert.equal(foreground.focusedPane, 'secondary');
  assert.equal(change.foreground.primaryWindowId, 'a');
});

test('only positions inside the documented range persist', () => {
  const foreground = new PhoneForegroundController();
  foreground.show('a');
  foreground.showInSplit('b');

  assert.equal(foreground.setSplitRatio(120).foreground.splitRatioPermille, 250);
  assert.equal(foreground.setSplitRatio(980).foreground.splitRatioPermille, 750);
  assert.equal(foreground.setSplitRatio(640).foreground.splitRatioPermille, 640);
});

test('there is no divider to move without a second pane', () => {
  const foreground = new PhoneForegroundController();
  foreground.show('a');

  assert.throws(() => foreground.setSplitRatio(500), PhoneForegroundError);
  assert.throws(() => foreground.focus('secondary'), PhoneForegroundError);
});

test('ending a split keeps the focused window as the single foreground window', () => {
  const foreground = new PhoneForegroundController();
  foreground.show('a');
  foreground.showInSplit('b');

  const change = foreground.closeSplit();

  assert.equal(change.foreground.primaryWindowId, 'b', 'The focused pane survives.');
  assert.equal(change.foreground.secondaryWindowId, null);
  assert.equal(change.foreground.splitRatioPermille, null);
  assert.equal(change.replacedWindowId, 'a');
});

test('closing the primary window promotes the surviving split window', () => {
  const foreground = new PhoneForegroundController();
  foreground.show('a');
  foreground.showInSplit('b');

  foreground.closed('a');

  assert.equal(foreground.state().primaryWindowId, 'b');
  assert.equal(foreground.state().secondaryWindowId, null);
  assert.equal(foreground.focusedPane, 'primary');
});

test('closing the secondary window ends the split and keeps the primary', () => {
  const foreground = new PhoneForegroundController();
  foreground.show('a');
  foreground.showInSplit('b');

  foreground.closed('b');

  assert.equal(foreground.state().primaryWindowId, 'a');
  assert.equal(foreground.state().secondaryWindowId, null);
  assert.equal(foreground.state().splitRatioPermille, null);
});

test('closing a background window changes nothing in the foreground', () => {
  const foreground = new PhoneForegroundController();
  foreground.show('a');
  foreground.showInSplit('b');

  foreground.closed('z');

  assert.equal(foreground.state().primaryWindowId, 'a');
  assert.equal(foreground.state().secondaryWindowId, 'b');
});

test('a restored layout comes back in the state it was persisted in', () => {
  const foreground = new PhoneForegroundController();

  foreground.restore({ primaryWindowId: 'a', secondaryWindowId: 'b', splitRatioPermille: 640 });

  assert.deepEqual(foreground.state(), {
    primaryWindowId: 'a',
    secondaryWindowId: 'b',
    splitRatioPermille: 640,
  });
  assert.equal(foreground.focusedPane, 'primary');
});

test('a restored single layout carries no split position', () => {
  const foreground = new PhoneForegroundController();

  foreground.restore({ primaryWindowId: 'a', secondaryWindowId: null, splitRatioPermille: 640 });

  assert.equal(foreground.state().splitRatioPermille, null);
});

test('splitting with nothing on the stage simply shows the window', () => {
  const foreground = new PhoneForegroundController();

  const change = foreground.showInSplit('a');

  assert.equal(change.foreground.primaryWindowId, 'a');
  assert.equal(change.foreground.secondaryWindowId, null);
});
