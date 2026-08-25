# Official package releases

Official JulOS extension packages use the same immutable package format as custom extensions. They always ship signed and use the high-trust native frontend/worker path. Custom unsigned or unknown-publisher extensions remain installable only under the warning and isolation rules in `PACKAGES.md` and `APPLICATION_CATALOG.md`; Core contains no first-party authorization bypass.

## Integrity contract

A released package consists of:

- `<package>.zip` — the complete immutable package archive;
- `<package>.zip.sig` — ECDSA P-256/SHA-256 signature over the exact ZIP bytes;
- `<package>.zip.sha256` — SHA-256 digest of the exact ZIP bytes;
- `<package>.zip.json` — publisher ID, signing-key ID, digest and signature filename.

JulOS authenticates the complete ZIP before opening or extracting it. Changing a worker binary, frontend file, migration, schema or any other archive member therefore invalidates the publisher signature even when `manifest.json` itself is unchanged.

The manifest remains strictly validated after archive authentication. Frontend modules additionally keep their manifest-declared SHA-256 digest so the browser host can verify the module again immediately before execution.

## Publisher identity

The current official publisher ID is `juloc-official`, matching `deploy/official-packages/catalog.json` and the official package manifests.

The signing key ID is supplied to the release workflow. Key IDs must identify one immutable public key. Rotation uses a new key ID; an existing key ID must never silently point to different key material.

## Private key handling

The private signing key must never be committed, copied into a container image or stored in a package artifact.

The repository workflow expects this GitHub Actions secret:

`JULOS_PACKAGE_SIGNING_PRIVATE_KEY_PEM`

It must contain an ECDSA P-256 private key in PEM format. The workflow writes it only to a temporary runner file, signs the archives and removes the file when the signing step exits.

## Trusted public key

An installation classifies a package as `trusted-signed` only when the matching publisher/key pair is present in `JulOS:Packages:TrustedPublishers`.

For example, a key published as `juloc-official` / `official-alpha-2026` is configured under:

`JulOS:Packages:TrustedPublishers:juloc-official:official-alpha-2026`

The value is the ECDSA public key PEM. The package workflow derives and publishes `juloc-package-signing-public.pem` so operators can verify/configure the exact public key without handling the private key.

JulOS intentionally does not auto-trust an unknown public key downloaded beside a package. A package without configured trust is `unknown-signed` and requires explicit acknowledgement plus the isolated native-code path; it never becomes trusted because its key was bundled beside it.

## Building and signing

`.github/workflows/publish-official-packages.yml` performs the official build:

1. rebuild the Remote frontend;
2. run the repository validator;
3. publish process workers in Release configuration;
4. create deterministic package ZIPs with `tools/build-package-artifact.sh`;
5. compute SHA-256 for each ZIP;
6. sign each exact ZIP with the configured private key;
7. derive the matching public key and verify every signature;
8. upload the signed archives, signatures, digests, metadata and public key as one workflow artifact.

The workflow currently builds:

- `JulOS.HostMetrics`;
- `JulOS.Remote`;
- `JulOS.Browser`.

A package is not considered released merely because its source directory exists in the monorepo. It becomes an installable official artifact only after this workflow succeeds with a configured signing key.

## Local package artifact build

The archive builder can also be used without signing:

```bash
bash tools/build-package-artifact.sh packages/JulOS.HostMetrics ./artifacts/JulOS.HostMetrics-1.0.0.zip
```

This is useful for validating archive contents. An unsigned archive is never an **official release**. After `PKG-013`/`PKG-014`, an administrator may install it as custom untrusted content under the documented warning and isolation policy.

## Release gate

Before official packages are attached to an alpha or stable JulOS release:

- the signing secret exists and its matching public key is recorded under a stable key ID;
- repository validation is green;
- every generated signature verifies against the published public key;
- package installation through the production Package Manager succeeds;
- enable/disable/remove and launcher/widget refresh behavior succeeds without a page reload;
- private key material is absent from repository, images, release assets and logs.
