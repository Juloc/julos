/**
 * Computes the next highlighted index when navigating a wrapping list with the
 * arrow keys. Moving past the last item wraps to the first and vice versa; an
 * empty list stays at 0 and an out-of-range current index is clamped first.
 */
export function nextSelectionIndex(current: number, count: number, direction: 1 | -1): number {
  if (count <= 0) {
    return 0;
  }
  const base = ((current % count) + count) % count;
  return (base + direction + count) % count;
}
