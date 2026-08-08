import { PackageCapabilityClient } from './package-capability-client.js';
import type { PackageFrontendContext, PackageLaunchTarget } from './package-frontend-host.js';

export interface PackageFrontendContextOptions {
  readonly packageId: string;
  readonly language: 'en' | 'de';
  readonly theme: 'light' | 'dark';
  readonly openApplication: (applicationId: string, targetId?: string) => void | Promise<void>;
  readonly saveLaunchTarget: (
    applicationStableKey: string,
    externalIdentity: string,
    displayName: string,
  ) => Promise<PackageLaunchTarget>;
  readonly deleteLaunchTarget: (launchTargetId: string) => Promise<void>;
}

/** Creates the token-free package frontend context with a package-bound capability caller. */
export function createPackageFrontendContext(
  options: PackageFrontendContextOptions,
  capabilityClient = new PackageCapabilityClient(),
): PackageFrontendContext {
  return Object.freeze({
    packageId: options.packageId,
    language: options.language,
    theme: options.theme,
    invokeCapability: (
      name: string,
      operation: string,
      payload: unknown,
    ): Promise<unknown> => capabilityClient.invoke(
      options.packageId,
      name,
      operation,
      payload,
    ),
    openApplication: options.openApplication,
    saveLaunchTarget: options.saveLaunchTarget,
    deleteLaunchTarget: options.deleteLaunchTarget,
  });
}
