export type ThemeMode = 'system' | 'light' | 'dark';
export type MotionMode = 'enabled' | 'reduced';
export type ResolvedTheme = 'light' | 'dark';

export interface AppearanceTarget {
  readonly dataset: DOMStringMap;
}

export function isThemeMode(value: string): value is ThemeMode {
  return value === 'system' || value === 'light' || value === 'dark';
}

export function isMotionMode(value: string): value is MotionMode {
  return value === 'enabled' || value === 'reduced';
}

export function resolveTheme(mode: ThemeMode, prefersDark: boolean): ResolvedTheme {
  return mode === 'system' ? (prefersDark ? 'dark' : 'light') : mode;
}

export function applyAppearance(
  target: AppearanceTarget,
  theme: ThemeMode,
  motion: MotionMode,
): void {
  target.dataset['theme'] = theme;
  target.dataset['motion'] = motion;
}
