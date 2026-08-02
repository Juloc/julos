export type SupportedLanguage = 'en' | 'de';

export const shellMessageKeys = [
  'about',
  'agentStatus',
  'close',
  'commandPalette',
  'desktop',
  'launcher',
  'loading',
  'noApplicationsBody',
  'noApplicationsTitle',
  'notifications',
  'problems',
  'serverUnavailable',
  'settings',
  'setupRequired',
  'signedOut',
  'version',
] as const;

export type ShellMessageKey = (typeof shellMessageKeys)[number];

type ShellMessages = Readonly<Record<ShellMessageKey, string>>;

export const shellMessages: Readonly<Record<SupportedLanguage, ShellMessages>> = {
  en: {
    about: 'About JulOS',
    agentStatus: 'Agent status',
    close: 'Close',
    commandPalette: 'Search and commands',
    desktop: 'Desktop',
    launcher: 'Open launcher',
    loading: 'Loading',
    noApplicationsBody: 'Open the launcher to start an installed application.',
    noApplicationsTitle: 'No applications are open',
    notifications: 'Notifications',
    problems: 'Problems',
    serverUnavailable: 'Server information unavailable',
    settings: 'Settings',
    setupRequired: 'Initial setup required',
    signedOut: 'Sign in required',
    version: 'Version',
  },
  de: {
    about: 'Über JulOS',
    agentStatus: 'Agent-Status',
    close: 'Schließen',
    commandPalette: 'Suche und Befehle',
    desktop: 'Desktop',
    launcher: 'Launcher öffnen',
    loading: 'Wird geladen',
    noApplicationsBody: 'Öffne den Launcher, um eine installierte Anwendung zu starten.',
    noApplicationsTitle: 'Keine Anwendungen geöffnet',
    notifications: 'Benachrichtigungen',
    problems: 'Probleme',
    serverUnavailable: 'Serverinformationen nicht verfügbar',
    settings: 'Einstellungen',
    setupRequired: 'Ersteinrichtung erforderlich',
    signedOut: 'Anmeldung erforderlich',
    version: 'Version',
  },
};

export function normalizeLanguage(value: string | null | undefined): SupportedLanguage {
  return value?.toLowerCase().startsWith('de') === true ? 'de' : 'en';
}

export function translate(language: SupportedLanguage, key: ShellMessageKey): string {
  return shellMessages[language][key];
}
