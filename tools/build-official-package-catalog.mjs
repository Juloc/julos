import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const outputDirectory = path.resolve(process.argv[2] ?? '');
const browserRuntimeImage = process.argv[3] ?? '';
const privateKeyPem = process.env.PACKAGE_SIGNING_KEY ?? '';
const publisherId = process.env.PACKAGE_PUBLISHER_ID ?? 'juloc-official';
const keyId = process.env.PACKAGE_KEY_ID ?? '';

if (!outputDirectory || !/^ghcr\.io\/[a-z0-9./_-]+@sha256:[0-9a-f]{64}$/u.test(browserRuntimeImage)) {
  throw new Error('usage: node build-official-package-catalog.mjs <output-directory> <digest-pinned-browser-runtime-image>');
}
if (!privateKeyPem || !publisherId || !keyId) {
  throw new Error('Official package signing configuration is incomplete.');
}

const privateKey = crypto.createPrivateKey(privateKeyPem);
if (privateKey.asymmetricKeyType !== 'ec' || privateKey.asymmetricKeyDetails?.namedCurve !== 'prime256v1') {
  throw new Error('Official package signing key must use ECDSA P-256.');
}
const publicKey = crypto.createPublicKey(privateKey);

function readPackageVersion(manifestPath) {
  const raw = fs.readFileSync(path.resolve(manifestPath), 'utf8').replace(/^\uFEFF/u, '');
  const manifest = JSON.parse(raw);
  if (typeof manifest.Version !== 'string' || manifest.Version.length === 0) {
    throw new Error(`Official package manifest has no version: ${manifestPath}`);
  }
  return manifest.Version;
}

const browserVersion = readPackageVersion('packages/JulOS.Browser/manifest.json');
const remoteVersion = readPackageVersion('packages/JulOS.Remote/manifest.json');
const hostMetricsVersion = readPackageVersion('packages/JulOS.HostMetrics/manifest.json');

const packageDefinitions = [
  {
    packageId: 'de.juloc.julos.browser',
    version: browserVersion,
    archive: `JulOS.Browser-${browserVersion}.zip`,
    displayNameEn: 'Browser',
    displayNameDe: 'Browser',
    descriptionEn: 'Isolated Chromium sessions and saved URL applications.',
    descriptionDe: 'Isolierte Chromium-Sitzungen und gespeicherte URL-Apps.',
    defaultConfiguration: {
      idleTimeoutMinutes: '30',
      allowDownloads: 'false',
      allowedNetworks: 'julos-remote',
      defaultNetwork: 'julos-remote',
      runtimeImage: browserRuntimeImage,
    },
  },
  {
    packageId: 'de.juloc.julos.remote',
    version: remoteVersion,
    archive: `JulOS.Remote-${remoteVersion}.zip`,
    displayNameEn: 'Remote',
    displayNameDe: 'Remote',
    descriptionEn: 'Saved RDP, SSH and VNC connections as JulOS applications.',
    descriptionDe: 'Gespeicherte RDP-, SSH- und VNC-Verbindungen als JulOS-Apps.',
    defaultConfiguration: {
      idleTimeoutMinutes: '30',
      maximumSessionMinutes: '480',
    },
  },
  {
    packageId: 'de.juloc.julos.hostmetrics',
    version: hostMetricsVersion,
    archive: `JulOS.HostMetrics-${hostMetricsVersion}.zip`,
    displayNameEn: 'Host Metrics',
    displayNameDe: 'Host-Metriken',
    descriptionEn: 'Core host status and metrics surfaces.',
    descriptionDe: 'Status- und Metrikansichten für JulOS-Hosts.',
    defaultConfiguration: {},
  },
];

fs.mkdirSync(outputDirectory, { recursive: true });
const publicKeyFile = 'juloc-package-signing-public.pem';
fs.writeFileSync(path.join(outputDirectory, publicKeyFile), publicKey.export({ type: 'spki', format: 'pem' }));

const packages = [];
for (const definition of packageDefinitions) {
  const archivePath = path.join(outputDirectory, definition.archive);
  if (!fs.existsSync(archivePath)) {
    throw new Error(`Official package archive is missing: ${definition.archive}`);
  }

  const archive = fs.readFileSync(archivePath);
  const signature = crypto.sign('sha256', archive, { key: privateKey, dsaEncoding: 'ieee-p1363' });
  if (!crypto.verify('sha256', archive, { key: publicKey, dsaEncoding: 'ieee-p1363' }, signature)) {
    throw new Error(`Signature verification failed for ${definition.archive}.`);
  }

  const signatureFile = `${definition.archive}.sig`;
  fs.writeFileSync(path.join(outputDirectory, signatureFile), signature);
  packages.push({
    packageId: definition.packageId,
    version: definition.version,
    displayNameEn: definition.displayNameEn,
    displayNameDe: definition.displayNameDe,
    descriptionEn: definition.descriptionEn,
    descriptionDe: definition.descriptionDe,
    artifactFile: definition.archive,
    signatureFile,
    sha256: crypto.createHash('sha256').update(archive).digest('hex'),
    defaultConfiguration: definition.defaultConfiguration,
  });
}

fs.writeFileSync(path.join(outputDirectory, 'catalog.json'), `${JSON.stringify({
  schemaVersion: '1',
  publisherId,
  keyId,
  publicKeyFile,
  packages,
}, null, 2)}\n`);
