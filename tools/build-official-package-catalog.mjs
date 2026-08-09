import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const outputDirectory = path.resolve(process.argv[2] ?? '');
const browserRuntimeImage = process.argv[3] ?? '';
if (!outputDirectory || !/^ghcr\.io\/[a-z0-9./_-]+@sha256:[0-9a-f]{64}$/u.test(browserRuntimeImage)) {
  throw new Error('usage: node build-official-package-catalog.mjs <output-directory> <digest-pinned-browser-runtime-image>');
}

const packageDefinitions = [
  {
    packageId: 'de.juloc.julos.browser',
    version: '1.0.0',
    archive: 'JulOS.Browser-1.0.0.zip',
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
    version: '1.0.0',
    archive: 'JulOS.Remote-1.0.0.zip',
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
    version: '1.0.0',
    archive: 'JulOS.HostMetrics-1.0.0.zip',
    displayNameEn: 'Host Metrics',
    displayNameDe: 'Host-Metriken',
    descriptionEn: 'Core host status and metrics surfaces.',
    descriptionDe: 'Status- und Metrikansichten für JulOS-Hosts.',
    defaultConfiguration: {},
  },
];

fs.mkdirSync(outputDirectory, { recursive: true });
const { privateKey, publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
const publisherId = 'juloc-official';
const keyId = 'release-local-p256-v1';
const publicKeyFile = 'juloc-package-signing-public.pem';
fs.writeFileSync(path.join(outputDirectory, publicKeyFile), publicKey.export({ type: 'spki', format: 'pem' }));

const packages = [];
for (const definition of packageDefinitions) {
  const archivePath = path.join(outputDirectory, definition.archive);
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
