export interface KeyboardCommand {
  readonly id: string;
  readonly key: string;
  readonly alt?: boolean;
  readonly control?: boolean;
  readonly shift?: boolean;
  readonly meta?: boolean;
  readonly execute: () => void | Promise<void>;
}

export interface KeyboardInput {
  readonly key: string;
  readonly altKey: boolean;
  readonly ctrlKey: boolean;
  readonly shiftKey: boolean;
  readonly metaKey: boolean;
  readonly targetIsEditable: boolean;
}

export class KeyboardCommandRouter {
  readonly #commands: readonly KeyboardCommand[];

  public constructor(commands: readonly KeyboardCommand[]) {
    const identifiers = new Set<string>();
    this.#commands = commands.map((command) => {
      if (command.id.trim().length === 0 || identifiers.has(command.id)) {
        throw new TypeError(`Keyboard command '${command.id}' is invalid or duplicated.`);
      }
      identifiers.add(command.id);
      return command;
    });
  }

  public async handle(input: KeyboardInput): Promise<string | null> {
    const command = this.#commands.find((candidate) => matches(candidate, input));
    if (command === undefined) {
      return null;
    }

    if (input.targetIsEditable && !command.alt && !command.control && !command.meta) {
      return null;
    }

    await command.execute();
    return command.id;
  }
}

export interface FocusableItem {
  readonly id: string;
  readonly disabled: boolean;
  readonly hidden: boolean;
}

export function nextFocusable(
  items: readonly FocusableItem[],
  currentId: string | null,
  offset: 1 | -1,
): string | null {
  const available = items.filter((item) => !item.disabled && !item.hidden);
  if (available.length === 0) {
    return null;
  }

  const currentIndex = available.findIndex((item) => item.id === currentId);
  const start = currentIndex < 0 ? (offset === 1 ? -1 : 0) : currentIndex;
  const index = (start + offset + available.length) % available.length;
  return available[index]?.id ?? null;
}

export function clampZoomPercent(value: number): number {
  if (!Number.isFinite(value)) {
    throw new RangeError('Zoom percentage must be finite.');
  }
  return Math.min(400, Math.max(50, Math.round(value)));
}

export function reducedMotionEnabled(
  storedPreference: 'full' | 'reduced' | null,
  systemPrefersReducedMotion: boolean,
): boolean {
  return storedPreference === 'reduced'
    || (storedPreference === null && systemPrefersReducedMotion);
}

function matches(command: KeyboardCommand, input: KeyboardInput): boolean {
  return command.key.toLocaleLowerCase('en-US') === input.key.toLocaleLowerCase('en-US')
    && (command.alt ?? false) === input.altKey
    && (command.control ?? false) === input.ctrlKey
    && (command.shift ?? false) === input.shiftKey
    && (command.meta ?? false) === input.metaKey;
}
