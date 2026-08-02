export type SupportedLanguage = 'en' | 'de';

export const shellMessageKeys = [
  'about',
  'accessDenied',
  'agentStatus',
  'close',
  'commandPalette',
  'component',
  'desktop',
  'launcher',
  'loading',
  'noApplicationsBody',
  'noApplicationsTitle',
  'notifications',
  'offline',
  'problems',
  'reference',
  'requestFailed',
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
    accessDenied: 'You do not have permission to view this information.',
    agentStatus: 'Agent status',
    close: 'Close',
    commandPalette: 'Search and commands',
    component: 'Component',
    desktop: 'Desktop',
    launcher: 'Open launcher',
    loading: 'Loading',
    noApplicationsBody: 'Open the launcher to start an installed application.',
    noApplicationsTitle: 'No applications are open',
    notifications: 'Notifications',
    offline: 'The JulOS server cannot be reached.',
    problems: 'Problems',
    reference: 'Reference',
    requestFailed: 'The request failed.',
    serverUnavailable: 'Server information unavailable',
    settings: 'Settings',
    setupRequired: 'Initial setup required',
    signedOut: 'Sign in required',
    version: 'Version',
  },
  de: {
    about: 'Über JulOS',
    accessDenied: 'Du hast keine Berechtigung, diese Informationen anzuzeigen.',
    agentStatus: 'Agent-Status',
    close: 'Schließen',
    commandPalette: 'Suche und Befehle',
    component: 'Komponente',
    desktop: 'Desktop',
    launcher: 'Launcher öffnen',
    loading: 'Wird geladen',
    noApplicationsBody: 'Öffne den Launcher, um eine installierte Anwendung zu starten.',
    noApplicationsTitle: 'Keine Anwendungen geöffnet',
    notifications: 'Benachrichtigungen',
    offline: 'Der JulOS-Server ist nicht erreichbar.',
    problems: 'Probleme',
    reference: 'Referenz',
    requestFailed: 'Die Anfrage ist fehlgeschlagen.',
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
