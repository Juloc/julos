import { NotificationCenterStore } from './notification-center.js';

/** Shared shell-level notification and problem state for realtime events and Core windows. */
export const desktopNotificationCenter = new NotificationCenterStore();
