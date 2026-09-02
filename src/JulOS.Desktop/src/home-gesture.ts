/** Gestures recognised on the mobile home indicator. */
export type HomeIndicatorGesture =
  | 'tap'
  | 'switch-next'
  | 'switch-previous'
  | 'reveal'
  | 'hide';

/**
 * Classifies a home-indicator pointer gesture from its total travel.
 *
 * A short travel is a tap (toggles the dock). A predominantly horizontal swipe
 * switches applications — left advances to the next window, right returns to the
 * previous. A predominantly vertical swipe reveals the dock when moving up and
 * hides it when moving down. Screen coordinates grow downward, so an upward
 * swipe has a negative `dy`.
 */
export function classifyHomeIndicatorGesture(
  dx: number,
  dy: number,
  tapThreshold = 14,
): HomeIndicatorGesture {
  if (Math.abs(dx) < tapThreshold && Math.abs(dy) < tapThreshold) {
    return 'tap';
  }
  if (Math.abs(dx) > Math.abs(dy)) {
    return dx < 0 ? 'switch-next' : 'switch-previous';
  }
  return dy < 0 ? 'reveal' : 'hide';
}
