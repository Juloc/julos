# Application catalog and Docker application lifecycle

Status: Accepted target specification. This document defines user-installable applications. `PACKAGES.md` remains authoritative for JulOS platform-extension packages.

## 1. Product model

JulOS presents one Store, but it does not pretend that every installable thing has the same trust or runtime model.

| Concept | Meaning |
|---|---|
| **Catalog app** | Store metadata with one stable identity and one or more delivery options |
| **App installation** | One selected delivery option connected to or deployed on one target |
| **JulOS extension package** | Versioned JulOS frontend, widget, worker or capability installed into the JulOS platform |
| **External application** | A service that remains authoritative outside JulOS, whether JulOS deployed it or only connected it |

A catalog app may offer several delivery options:

1. `connection` — connect an existing service; deploy nothing.
2. `docker-image` — install one image; JulOS normalizes it to one-service Compose.
3. `docker-compose` — install a standard Compose application.
4. `native-extension` — install a JulOS extension package.

One catalog entry may combine a Docker delivery with an optional native integration. Home Assistant, for example, may be connected or deployed, then optionally gain a native JulOS integration that uses its public API.

## 2. Stable identity

Catalog identity:

```text
CatalogSourceId + AppId
```

Release identity:

```text
CatalogSourceId + AppId + Version + DefinitionDigest
```

Installation identity:

```text
AppInstallationId
```

Display names, repository URLs, image tags, container IDs and IP addresses are never identity.

## 3. Catalog format

Catalogs use two versioned formats:

- `app-catalog-index.v1` for source identity and immutable entry references;
- `app-manifest.v1` for one application version and its delivery options.

Standard Compose remains standard Compose. JulOS metadata lives under `x-julos`; JulOS does not invent a second orchestration language.

Every source root contains `catalog.json`:

```json
{
  "schema": "app-catalog-index.v1",
  "sourceId": "community.example",
  "generatedAtUtc": "2026-08-25T12:00:00Z",
  "keySet": {
    "path": "keys.json",
    "sha256": "..."
  },
  "entries": [
    {
      "appId": "home-assistant",
      "version": "2026.8.0",
      "path": "apps/home-assistant/2026.8.0/app.json",
      "sha256": "..."
    }
  ]
}
```

`sourceId` is stable and must match the configured source after first successful import. Entry paths are relative, normalized, cannot escape the source root and must match their digest before parsing. Duplicate `(appId, version)` entries fail the complete refresh transaction.

Bundle layout:

```text
catalog.json
keys.json                              optional
apps/<app-id>/<version>/
  app.json
  compose.yaml                    optional
  assets/
  signature.json                  optional
```

Files referenced by one app manifest must remain below its version directory, must not be symlinks and are size-limited by the validator.

An optional `keys.json` uses `app-catalog-keyset.v1`:

```json
{
  "schema": "app-catalog-keyset.v1",
  "publisherId": "juloc-official",
  "keys": [
    {
      "keyId": "official-2026-01",
      "algorithm": "ecdsa-p256-sha256-p1363",
      "publicKeySpkiBase64": "...",
      "publicKeyFingerprint": "sha256:...",
      "validFromUtc": "2026-01-01T00:00:00Z",
      "validUntilUtc": null,
      "revokedAtUtc": null
    }
  ]
}
```

The key-set path is relative to the source root and digest-checked before parsing. HTTPS/Git/OCI transport trust does not automatically make a publisher key trusted. Official key fingerprints are pinned by the JulOS release; an administrator may explicitly trust a custom fingerprint.

Minimum app manifest:

```json
{
  "schema": "app-manifest.v1",
  "appId": "home-assistant",
  "version": "2026.8.0",
  "name": { "en": "Home Assistant", "de": "Home Assistant" },
  "description": { "en": "Home automation", "de": "Hausautomation" },
  "icon": { "path": "assets/icon.svg", "sha256": "..." },
  "architectures": ["linux/amd64", "linux/arm64"],
  "deliveryOptions": [
    {
      "key": "connect",
      "kind": "connection",
      "connectionKind": "home-assistant-api",
      "endpointPolicy": "https-or-private-http"
    },
    {
      "key": "compose",
      "kind": "docker-compose",
      "composePath": "compose.yaml",
      "composeSha256": "..."
    }
  ]
}
```

Entries may additionally declare:

- localized release notes and screenshots;
- homepage, source and support links;
- categories and search terms;
- minimum Docker Engine and Compose versions;
- configuration parameters and secret parameters;
- UI entry service, port, scheme and path;
- health probes;
- persistent data and backup declarations;
- optional extension package references;
- license metadata;
- update notes and migration warnings.

Unknown ordinary fields fail validation. Unknown keys beginning with `x-` are preserved as inert metadata and are never executed. `requiredFeatures` lists extension features whose absence makes the entry incompatible. Unsupported schema versions or required features fail before preview.

## 4. Compose metadata

`x-julos` may contain presentation and lifecycle metadata only:

```yaml
x-julos:
  ui:
    service: home-assistant
    port: 8123
    scheme: http
    path: /
  data:
    - service: home-assistant
      volume: config
      path: /config
      backup: stop-consistent
  health:
    service: home-assistant
    type: http
    path: /
  parameters:
    - key: timezone
      type: string
      required: true
```

The app manifest is authoritative for `AppId`; Compose metadata cannot override it. JulOS semantically normalizes the documented supported Compose subset into a typed deployment plan, validates every field and sends only that typed plan. Unsupported semantics fail preview; no field is silently ignored or passed through as executable extension data.

### 4.1 Initial supported Compose subset

Supported top-level keys:

```text
name, services, volumes, networks, x-julos
```

Supported service keys:

```text
image, entrypoint, command, environment, ports, expose, volumes, networks,
depends_on, restart, healthcheck, labels, user, working_dir, read_only, tmpfs,
init, stop_grace_period, cap_add, cap_drop, security_opt, devices, privileged,
network_mode, pid, ipc, hostname
```

Requirements:

- every service has an image; tags resolve to digests before apply;
- catalog parameter expansion occurs only in schema-declared value positions;
- bind mounts are absolute after parameter resolution and appear in preview;
- named volumes/networks are namespaced to the installation unless explicitly external;
- `privileged`, devices, host namespace modes, Docker-socket mounts and external host paths are critical rights that require explicit confirmation and may be denied by host policy;
- dependency conditions and health checks are preserved exactly when supported by the target Compose version.

Initially unsupported and rejected:

```text
build, extends, include, develop, deploy (Swarm), profiles selected implicitly,
container_name, env_file from catalog paths, configs/secrets sourced from host
files, relative bind paths and arbitrary Compose extension execution
```

Support for one rejected key requires a contract/test update; it is never silently ignored.

Single-image input is normalized into a generated Compose model so image and Compose installations share one lifecycle, ownership and backup implementation.

### 4.2 Closed `julos-compose-v1` value grammar

`julos-compose-v1` is the normative baseline; it does not mean “whatever the installed Compose version accepts.” The Docker provider advertises this profile and rejects a plan when its engine cannot preserve the defined semantics. CAT-001 vendors an upstream Compose JSON schema at one reviewed commit/digest for comparison, but the narrower rules below remain authoritative.

Document rules:

- UTF-8 YAML 1.2 core schema, one document, maximum 1 MiB;
- exact case-sensitive keys, no duplicate keys, custom tags, anchors, aliases or merge keys;
- no top-level `version`, host-environment interpolation or implicit `.env`;
- `$$` represents one literal `$`; every other `$NAME` or `${...}` form is rejected;
- ordinary parameters expand only from exact `{{julos:param:<key>}}` tokens in schema-declared string values; the key must be declared in `x-julos.parameters` and the result is revalidated in its destination type;
- secret parameters are never expanded into the normalized/persisted model and use Secret Bindings only;
- 1–64 services and at most 128 declared volumes/networks; names match `[A-Za-z0-9][A-Za-z0-9_.-]{0,62}`.

The supported value forms are closed:

| Field | Accepted v1 shape |
|---|---|
| `name` | Optional display hint matching the name rule. It never becomes identity; Apply uses `julos-<AppInstallationId>` as the actual project name. |
| `services.<name>.image` | Required non-empty OCI image reference; resolved to a digest before Apply. |
| `entrypoint`, `command` | String or array of strings. No host shell is invoked by JulOS; Compose container semantics apply. |
| `environment` | String/scalar map or `KEY=value` array. Null values and bare keys are rejected because they read host environment. |
| `ports` | Short `[IP:]PUBLISHED:TARGET[/tcp|udp]` or long object containing only `target`, `published`, `host_ip`, `protocol`, `name`, `app_protocol`, `mode`; ports are single integers 1–65535, protocol is TCP/UDP and mode is `host`. Ranges and random published ports are rejected. |
| `expose` | Array of single target ports with optional `/tcp` or `/udp`; no ranges. |
| `volumes` | Short `SOURCE:ABSOLUTE_TARGET[:ro|rw]` or long object containing only `type`, `source`, `target`, `read_only`, plus `bind.create_host_path=false`, `volume.nocopy`, or `tmpfs.size/mode`. Type is `bind`, `volume` or `tmpfs`; bind sources are absolute existing host paths, named sources are declared, and relative/anonymous sources are rejected. |
| `networks` | Array of declared names or map from name to an object containing only `aliases`. Static addresses, link-local addresses and driver options at service level are rejected in v1. |
| `depends_on` | Service-name array or map containing only `condition`, `restart`, `required`; condition is `service_started`, `service_healthy` or `service_completed_successfully`. References must exist and cycles fail. |
| `restart` | `no`, `always`, `unless-stopped` or `on-failure[:1..100]`. |
| `healthcheck` | Object containing only `test`, `interval`, `timeout`, `start_period`, `start_interval`, `retries`, `disable`; test is `NONE` or an array beginning `CMD`/`CMD-SHELL`, durations are positive `ns/us/ms/s/m/h` segment strings and retries are 1–100. |
| `labels` | String/scalar map or `KEY=value` array. Null/bare keys and the reserved `com.juloc.julos.*` namespace are rejected. |
| `user`, `working_dir`, `hostname` | Strings; working directory is an absolute container path. |
| `read_only`, `init`, `privileged` | Booleans. |
| `tmpfs` | Array of absolute container paths with optional `:size=<bytes>,mode=<octal>` options. |
| `stop_grace_period` | Positive duration using the healthcheck duration grammar. |
| `cap_add`, `cap_drop` | Arrays of uppercase Linux capability names. Every `cap_add` is a critical right. |
| `security_opt` | Array containing only `no-new-privileges:true`, `seccomp=unconfined` or `apparmor=unconfined`; either unconfined value is a critical right. |
| `devices` | Short `ABSOLUTE_HOST:ABSOLUTE_CONTAINER[:rwm]` array only; every entry is a critical right. |
| `network_mode` | `bridge`, `none`, `host` or `service:<declared-service>`; `host` is critical and `container:<id>` is rejected. |
| `pid`, `ipc` | `private`, `host` or `service:<declared-service>`; `host` is critical. |

Top-level volume entries are null or objects containing only `name`, `external`, `labels`. Top-level network entries are null or objects containing only `name`, `external`, `driver`, `internal`, `attachable`, `labels`; v1 permits only absent/`bridge` driver. When `external=true`, `name` is required and other creation settings are rejected. External resources are never owned or deleted by JulOS.

Cross-field validation rejects host-network plus published ports, missing healthchecks referenced by `service_healthy`, undeclared resources, duplicate host ports, overlapping container targets and any unknown nested key. Privileged mode, Docker socket mounts, all devices, all added capabilities, host PID/IPC/network, `seccomp=unconfined`, `apparmor=unconfined` and bind paths outside an administrator allowlist enter `CriticalRights` with exact service/field/value. Host policy may deny them even after acknowledgement.

## 5. Catalog sources

Supported source kinds:

- `official` — built-in JulOS source;
- `https` — static versioned catalog over HTTPS;
- `git` — public or private Git repository;
- `oci` — catalog artifact stored in an OCI registry;
- `local` — administrator-managed local catalog.

Source record:

```text
CatalogSourceId
SourceKind
DisplayName
Location
AuthenticationSecretReferenceId
TrustLevel                  official | administrator-trusted | custom
Enabled
DeletedAtUtc                    nullable tombstone; installed apps retain the reference
LastSuccessfulRevision
LastSuccessfulDigest
LastRefreshAtUtc
LastRefreshState
LastFailureCode
Revision
```

Rules:

- private credentials use Secret References;
- Git sources resolve a branch/tag to a commit and cache the commit, never only the moving name;
- HTTPS and OCI sources cache the immutable response/artifact digest;
- a failed refresh keeps the last valid catalog with an explicit stale marker;
- a refresh never replaces a valid cache with a partially parsed source;
- removing a source does not remove installed applications.

## 6. Trust and integrity

Source trust and artifact signature are independent indicators.

Signature states:

| State | Installation behavior |
|---|---|
| `trusted-signed` | Normal confirmation |
| `unknown-signed` | Warning; administrator may continue unless explicit distrust/revocation policy denies the key |
| `unsigned` | Simple warning; administrator may continue |
| `invalid-signature` | Refuse because the artifact claims authenticity but fails or cannot complete verification with the referenced key |

Every fetched definition, Compose file, extension artifact and resolved container image is recorded with SHA-256 or the registry-native immutable digest. Image tags are allowed as input but are resolved to digests before apply.

Optional detached signature envelope:

```json
{
  "schemaVersion": 1,
  "publisherId": "juloc-official",
  "keyId": "official-2026-01",
  "publicKeyFingerprint": "sha256:...",
  "algorithm": "ecdsa-p256-sha256-p1363",
  "artifactSha256": "...",
  "createdAtUtc": "2026-08-25T12:00:00Z",
  "signature": "base64..."
}
```

`DefinitionDigest` is SHA-256 over RFC 8785 JSON Canonicalization Scheme bytes of the app manifest. Every executable or displayed referenced file has its own digest in that manifest, so the definition digest binds the complete declared bundle. The exact signature input is UTF-8 `julos-app-definition-v1\n<lowercase-definition-sha256>\n`; the first accepted algorithm is ECDSA P-256/SHA-256 P1363 over those bytes. `artifactSha256` must equal `DefinitionDigest`.

A source publishes or references the versioned key set above. The calculated SPKI fingerprint must match both key set and envelope before verification. Key IDs bind immutably to one public key. A signature that verifies with a source-provided but untrusted key is `unknown-signed`; absence of a usable key is not treated as a verified signature and fails with `catalog.signature_key_unavailable`. Rotation introduces a new key ID; it never changes an existing ID. Revocation affects future trust evaluation and update policy but does not rewrite installed locks or delete running applications. An update whose publisher/key/trust state changed requires a fresh administrator acknowledgement.

Trust evaluation first verifies bytes, fingerprint and the key validity interval at `createdAtUtc`. Missing key, mismatched fingerprint, bad signature or not-yet-valid signature is `invalid-signature`. A cryptographically valid key is `trusted-signed` only while it is official-pinned or administrator-trusted; otherwise it is `unknown-signed`. `distrusted` or `revoked` adds `deny-policy`; `expired` adds a warning for custom content and `deny-policy` for the official source. Revocation or expiry discovered later never rewrites an installed snapshot, but update Preview compares it to current policy.

Trust is persisted as evidence, not recomputed into history. `CatalogPublisherKey` stores:

```text
CatalogSourceId
PublisherId
KeyId
Algorithm
PublicKeySpki
PublicKeyFingerprint
ValidFromUtc
ValidUntilUtc
RevokedAtUtc
AdministratorTrustState         unknown | trusted | distrusted
TrustedByUserId                 nullable
TrustedAtUtc                    nullable
FirstObservedSourceRevision
LastObservedSourceRevision
Revision
```

The built-in official fingerprint set is release configuration and cannot be replaced by source content. Administrator trust is bound to `(CatalogSourceId, PublisherId, KeyId, PublicKeyFingerprint)`; a reused key ID with different bytes fails the whole refresh.

Each cached entry records source revision/digest, Definition Digest, publisher ID, key ID, public-key fingerprint and evaluated signature state. Each successful deployment stores an immutable trust snapshot in its Deployment Lock. Current trust can later differ, but history remains explainable and the difference is part of update Preview.

`DeploymentLockSchemaVersion = 1` contains exactly:

```text
CatalogSourceId + source revision/digest
AppId + version + delivery key
DefinitionDigest
DeliveryProviderCapabilityName + version
TargetConnectionId
NormalizedPlanDigest
resolved extension/image artifact digests
publisher ID + key ID + public-key fingerprint + signature state at apply
CriticalRightsDigest
NonSecretConfigurationDigest
CreatedAtUtc
```

Resolved artifact digests form an object keyed by stable artifact role with lowercase immutable digest values and lexicographically sorted keys. The lock has no secret, display-only or extension fields. `DeploymentLockDigest` is lowercase SHA-256 over RFC 8785 canonical JSON bytes of the complete schema-v1 lock, including its schema version. Unknown fields or a different schema version fail; implementations do not calculate a partial digest.

`AppDeploymentApproval` stores Preview ID, Plan Digest, Definition Digest, Trust Assessment Digest, sorted warning codes, approving user, approval time, expiry, consuming Operation ID and optional resulting App Installation ID. It stores no secret value. An approval is single-use and cannot be consumed after expiry or by another Operation, target, definition, artifact, rights set or trust assessment.

The confirmation screen shows, in one place:

- source and publisher status;
- unsigned/unknown warning when applicable;
- selected host;
- ports and networks;
- volumes and host paths;
- devices, Docker socket use and privileged settings;
- secret parameters;
- resources that will be owned or merely connected.

An administrator can proceed after one explicit acknowledgement. The UI does not require a multi-step trust ceremony. A host policy may still deny a capability that the deployment owner has forbidden globally.

Signing a definition does not make privileged settings safe. Runtime-right changes are always shown on install and update.

## 7. Native extension isolation

Shadow DOM prevents style leakage; it is not a hostile-JavaScript sandbox. Before an unsigned or unknown-publisher native frontend can run, `PKG-013` provides:

- a separate package origin or sandboxed frame;
- no JulOS session cookie on that origin;
- a versioned `postMessage` bridge with exact request/response schemas;
- capability and permission checks on every bridged operation;
- no direct Shell DOM, global state or arbitrary Core API access;
- explicit navigation, localization, theme and lifecycle messages.

Until that isolation is implemented, unsigned Docker/Compose/connection catalog entries remain installable, but unsigned native extension code does not execute. This is one clear warning and one technical boundary, not a hidden fallback.

Unknown or unsigned backend workers likewise cannot run as a local `process` child under the Server identity. They require a Runtime-Manager-created, resource-limited control-plane worker container with only the declared package channel/storage/network, or the extension remains disabled. Administrator-trusted signed extensions may use the existing high-trust process-worker path.

## 8. App installation model

Core-owned records:

```text
CatalogSource
CatalogEntryCache
CatalogPublisherKey
ConnectionSecretBinding
AppInstallation
AppInstallationConfiguration
AppSecretBinding
AppResourceReference
AppDeploymentApproval
AppDeploymentLock
AppBackup
```

`AppInstallation`:

```text
AppInstallationId
CatalogSourceId
AppId
DeliveryKey
DeliveryKind
TargetConnectionId               null only for targetless local native-extension delivery
DeliveryProviderCapabilityName
DeliveryProviderCapabilityVersion
ResolvedProviderPackageInstallationId
OwnershipMode                    managed | adopted | external
InstalledVersion
DesiredVersion
InstalledDefinitionDigest
DesiredDefinitionDigest
ResolvedArtifactLockVersion
DeploymentLockSchemaVersion
DeploymentLockDigest
UpdatePolicy                     off | notify | automatic
LifecycleState
DesiredRuntimeState              running | stopped
ObservedRuntimeState             unknown | starting | running | stopped | missing
HealthState
LastOperationId                  nullable
LastFailureCode                  nullable
RemovedAtUtc                     nullable
CreatedAtUtc
UpdatedAtUtc
Revision
```

Lifecycle states:

```text
Installing
Installed
Updating
Removing
Failed
Removed
```

Valid lifecycle transitions:

```text
create             -> Installing
Installing success -> Installed
Installing failure -> Failed
Installing cancel/no resources -> Removed, only after inspection
Installed update   -> Updating
Updating success   -> Installed
Updating failure   -> Failed
Installed remove   -> Removing
Removing cancel/reconcile -> Installed, only after inspection proves the installed lock healthy
Failed retry       -> Installing | Updating, selected from the failed Operation kind
Failed cleanup     -> Removing
Failed reconcile   -> Installed, only after provider inspection proves the installed lock healthy
Removing success   -> Removed
Removed            -> terminal; reinstall creates a new AppInstallationId
any active state   -> Failed only with a stable failure code and Operation reference
```

Only one deployment Operation may hold the installation lock. Cancellation and retry must inspect the provider before choosing a next transition; they never assume that a timed-out external mutation did nothing. A cancellation request sets the Operation's `CancellationRequested` flag but leaves the lifecycle state unchanged until inspection proves one outcome: `Installed` for a healthy installed lock, `Removed` when an interrupted first install created no retained resources, or `Failed` with an exact cleanup/retry choice for partial or unknown state. A failed update/remove may reconcile to `Installed` only after provider inspection proves the stored installed lock remains present and healthy. A failed first install with partial owned resources uses `Failed cleanup -> Removing`; it cannot be silently deleted. Preview is mutation-free and therefore never appears as an `AppInstallation` state. Runtime desire, runtime observation and health (`unknown`, `starting`, `healthy`, `unhealthy`, `offline`) are separate axes. Backup/restore are durable Operations and do not invent lifecycle states.

`native-extension` delivery delegates authority to the existing `PackageInstallation` lifecycle. The catalog stores a link to that package installation; it does not create a competing App Installation state machine for the same extension.

`TargetConnectionId` identifies the provider-validated target. Docker delivery requires a `docker-engine` Connection whose versioned settings identify one configured engine and whose optional `RouteHostConnectorId` selects the Connector that can reach it. A host may expose several engine Connections. Connection delivery selects an existing `ready` Connection. The Store may guide the user through creating a separate draft Connection first, but Preview itself never creates it. Native local-platform delivery has no target Connection. Core never interprets provider settings or substitutes a Host Connector ID, URL, socket path or container ID for the Connection identity.

`AppResourceReference` stores a provider-owned stable identity plus `owned`, `adopted` or `external`. An ephemeral container ID may be cached as an observation but never becomes the resource key.

`AppSecretBinding` stores only `AppSecretBindingId`, `AppInstallationId`, destination parameter name, `SecretReferenceId`, purpose and revision. It never stores the secret value.

`ConnectionSecretBinding` is the independent record for a Connection: `ConnectionSecretBindingId`, `ConnectionId`, destination parameter name, `SecretReferenceId`, purpose and revision. It does not require an App Installation. Connection APIs use it; deployment APIs use `AppSecretBinding`.

## 9. Ownership modes

### Managed

JulOS created the resources and may update, stop, back up and remove them within the approved definition.

### Adopted

The administrator explicitly adopted an existing Compose project or service. JulOS may perform only the actions approved during adoption. Data and shared resources default to retain.

### External

JulOS stores a connection and launch metadata only. It never stops, updates or deletes the service.

Discovery creates a proposal. It never silently changes `external` to `adopted` or `managed`.

## 9.1 Connection contract

A Connection is a provider-managed standalone resource that one or more App Installations may reference:

```text
ConnectionId
ProviderCapabilityName
ProviderCapabilityVersion
ConnectionKind
Name
SanitizedEndpoint
RouteHostConnectorId            nullable
SettingsVersion
Settings
Enabled
LastValidationState
LastValidationAtUtc
LastFailureCode
Revision
```

Secrets are referenced through `ConnectionSecretBinding`; an app deployment never copies their values into `AppSecretBinding`. APIs:

```text
GET    /api/v1/connections
POST   /api/v1/connections
GET    /api/v1/connections/{connectionId}
PUT    /api/v1/connections/{connectionId}
POST   /api/v1/connections/{connectionId}/validation
PUT    /api/v1/connections/{connectionId}/secret-bindings/{destinationName}
DELETE /api/v1/connections/{connectionId}/secret-bindings/{destinationName}
DELETE /api/v1/connections/{connectionId}
```

Create stores non-secret settings as disabled `draft`. Binding requests contain only Secret Reference ID and expected Connection revision. Validation is a durable Operation when it contacts an external service and moves Configuration State to `ready` or `invalid`; only `ready` can be selected by App Preview. Removing a referenced Connection fails `connection.in_use`. After references are detached, deletion destroys only JulOS metadata/connection-scoped Secret References after confirmation and never removes the external service.

## 10. Public API

Catalogs:

```text
GET    /api/v1/catalog/sources
POST   /api/v1/catalog/sources
GET    /api/v1/catalog/sources/{sourceId}
PUT    /api/v1/catalog/sources/{sourceId}
DELETE /api/v1/catalog/sources/{sourceId}
POST   /api/v1/catalog/sources/{sourceId}/refresh
GET    /api/v1/catalog/apps
GET    /api/v1/catalog/apps/{sourceId}/{appId}
```

Installations:

```text
POST   /api/v1/app-installations/previews
POST   /api/v1/app-installations
GET    /api/v1/app-installations
GET    /api/v1/app-installations/{installationId}
POST   /api/v1/app-installations/{installationId}/update-previews
POST   /api/v1/app-installations/{installationId}/updates
POST   /api/v1/app-installations/{installationId}/backups
POST   /api/v1/app-installations/{installationId}/restores
POST   /api/v1/app-installations/{installationId}/uninstall-previews
DELETE /api/v1/app-installations/{installationId}
```

Builder:

```text
POST /api/v1/catalog/builder/imports
POST /api/v1/catalog/builder/validations
POST /api/v1/catalog/builder/exports
```

All mutations require antiforgery protection. Long-running mutations return a durable Operation. Preview results contain a digest and expiry; apply must reference the exact preview digest so changed input cannot bypass confirmation.

## 11. Permissions

```text
catalog.read
catalog.sources.manage
catalog.builder.use
connections.read
connections.manage
connections.validate
apps.read
apps.install
apps.update
apps.backup
apps.restore
apps.uninstall
apps.adopt
docker.read
docker.control
docker.container.terminal
```

Read, deployment, data deletion and terminal access remain separate. A catalog entry cannot grant permissions to itself.

## 12. Preview and apply

Preview performs no mutation. It checks:

1. source, schema, integrity and trust state;
2. target Host Connector state and advertised capability version;
3. CPU architecture and Docker/Compose compatibility;
4. image resolution to immutable digests;
5. ports, volumes, host paths, networks and name conflicts;
6. required ordinary and secret parameters;
7. privileged mode, host network/PID/IPC, devices and Docker-socket mounts;
8. available storage;
9. existing managed/adopted/external resource collisions;
10. declared health and backup support.

Preview request:

```text
CatalogSourceId
AppId
RequestedVersion                 optional
DeliveryKey
TargetConnectionId               required for Docker and connection delivery; null for local native extension
ConfigurationValues
SecretBindings                   destination name + SecretReferenceId only
```

Preview response:

```text
PreviewId
ExpiresAtUtc
DefinitionDigest
PlanDigest
TrustAssessment
ResolvedTarget                   ConnectionId, provider capability and routed HostConnectorId when applicable
ResolvedImages
RequiredExtensions
RequestedResources
CriticalRights
Conflicts
DataEffects
BackupCapability
CanApply
```

Apply request contains only `PreviewId`, exact `PlanDigest`, acknowledged warning codes and Idempotency Key. Server verifies that every required warning for that exact plan was acknowledged; a changed plan cannot reuse approval.

Apply references the preview, creates a durable Operation and revalidates the immutable inputs. Dispatch is delivery-specific and typed:

- `connection` invokes the selected provider capability to validate/create metadata and deploys no container;
- `docker-image` and `docker-compose` invoke `docker.apps/1`;
- `native-extension` delegates to Package Manager and links the resulting Package Installation.

There is no generic delivery payload. Success is recorded only after the owning provider inspects the requested state and declared health policy; native delivery follows the Package Installation state machine.

Secret values never enter the persisted preview, installation configuration, Operation target, Connector request payload or logs. `API-011` adds app-installation Secret References and an operation-bound lease for each required binding. The Docker package obtains the lease only while the matching Operation is running and hands the value to the target over an authenticated out-of-band secret channel bound to the same Operation, Host Connector, installation, approved Plan Digest and expiry. The Connector keeps it only in memory for Docker apply and zeroes/disposes it afterward. A retry obtains a new one-use lease; serialized requests contain only App Secret Binding identifiers and destination parameter names.

Connector requests are typed operations:

```text
preflight
apply
inspect
remove
backup
restore
```

The request contains a normalized Compose model and policy values, never shell text. Resources are labeled with at least:

```text
com.juloc.julos.app-installation-id
com.juloc.julos.catalog-source-id
com.juloc.julos.app-id
com.juloc.julos.project-identity
```

The Connector refuses to mutate a resource whose ownership label does not match the installation, except during an explicit adoption operation.

## 13. UI launch registration

After connection or verified deployment health, JulOS creates or updates an Application launch target using the declared UI service/port/path. Local proxy or streamed rendering follows `WEB-APP-RENDERING.md`.

No private URL containing credentials is returned to Desktop. The target retains the stable app-installation and service identity across container recreation.

## 14. Updates

Update policy per installation:

- `off` — no checks initiated by policy;
- `notify` — show an available update;
- `automatic` — apply only when the stored policy still permits it.

Update sequence:

1. fetch and verify the new definition;
2. resolve image digests;
3. produce definition, rights and data diff;
4. pause automatic update for source/trust changes or new critical rights;
5. create the declared pre-update backup when required;
6. apply desired state;
7. verify health;
8. update the installation lock;
9. offer rollback only when the definition and data policy declare it safe.

There is no silent fallback to an older implementation. A failed update remains visible and retains exact diagnostics and the last verified backup.

## 15. Backups and restore

An app backup contains:

- normalized definition and exact definition digest;
- resolved image digests;
- non-secret configuration;
- Secret Reference identifiers, never values;
- declared owned volumes/data;
- ownership map;
- JulOS and provider-package versions;
- checksum manifest.

The first implementation may stop the app for a consistent volume backup. Application-consistent online backup requires a later typed integration capability. Optional image export for offline restore is separate from normal backup.

A restore verifies checksums and compatibility before mutation, restores to staging where possible, applies the saved definition and verifies health. Backups outlive app uninstallation unless explicitly deleted through a separate action.

## 16. Uninstallation

Uninstall preview enumerates every affected resource and classifies it as owned, adopted, external or shared.

Choices:

- remove runtime and retain data;
- create backup, then remove owned data;
- remove owned data permanently.

External, shared and adopted data default to retain and cannot be deleted by an ordinary managed-app removal. Images are cleanup candidates, never assumed exclusive.

## 17. App Builder

The App Builder supports:

- importing a container image reference;
- pasting or uploading Compose;
- importing a catalog URL or Git source;
- editing localized name, icon and description;
- defining ordinary and secret parameters;
- selecting the UI service, port and path;
- declaring persistent data and health;
- validating architecture, conflicts and dangerous rights;
- test-installing through the normal preview/apply path;
- exporting a portable `app-catalog-index.v1` plus `app-manifest.v1` directory.

Builder output has no implicit trust. A user-created unsigned entry is clearly marked and remains installable under the same rules as any other custom source.

## 18. Examples used for acceptance

### Home Assistant

- connect an existing instance without deployment;
- install the official/community Compose definition on a chosen host;
- open its UI through an approved launch target;
- retain configuration across update and uninstall-with-data-retention.

### Hermes

Hermes is normative only as an ordinary workload category: if the user supplies a Hermes image, Compose definition or existing connection, JulOS uses the same generic delivery, inventory and optional container-terminal contracts as for any other app. Telegram, CLI conventions and a native chat surface remain Hermes-owned and require the concrete Hermes specification before JulOS documents an integration. No Hermes-specific Core or Host Connector code is permitted.

## 19. Stable errors

Minimum codes:

```text
catalog.source_unavailable
catalog.source_stale
catalog.schema_unsupported
catalog.definition_invalid
catalog.integrity_mismatch
catalog.signature_invalid
catalog.signature_key_unavailable
catalog.compose_feature_unsupported
catalog.preview_expired
catalog.preview_changed
connection.in_use
connection.not_ready
app.target_unavailable
app.architecture_unsupported
app.port_conflict
app.mount_conflict
app.rights_changed
app.resource_not_owned
app.deployment_locked
app.secret_lease_denied
app.health_verification_failed
app.backup_failed
app.restore_failed
app.uninstall_conflict
```

Raw Compose parser errors may be sanitized into field errors; raw secret values, registry credentials and host paths outside the user's authorized view are never returned.

## 20. Work items and dependency order

1. `CAT-001` — commit index/manifest/key-set schemas, bundle rules, fixtures, validator and catalog-validation stage.
2. `CAT-002` — implement source persistence, HTTPS/Git/OCI/local refresh, atomic cache and trust evaluator.
3. `CONN-001` — implement provider-neutral Connection domain, API, validation and Secret Bindings.
4. `PKG-013` — isolate unknown native frontends and backend workers.
5. `PKG-014` — implement optional signatures, four trust states and digest-bound acknowledgement.
6. `APP-001` — implement App Installation domain, deployment lock, preview/apply and Operations.
7. `API-011` — implement app-installation Secret Bindings and operation-bound Host Connector leases.
8. `DKR-001` — implement bounded Host Connector Docker inventory/control adapter.
9. `DKR-002` — implement stable Docker inventory and ownership mapping.
10. `APP-002` — implement connection delivery and external launch registration.
11. `DKR-007` — implement `docker.apps/1`, single-image normalization and apply/inspect.
12. `DKR-008` — implement supported Compose parsing, preflight and multi-service apply.
13. `APP-006` — implement explicit Docker adoption after stable inventory/deployment exists.
14. `CAT-003` — implement Store, source management, My Apps and App Builder UI.
15. `APP-004` — implement app backup and restore before destructive lifecycle work.
16. `APP-003` — implement update policy and rights/data diff, using verified backup where required.
17. `APP-005` — implement safe uninstall and data-retention choices, using backup before selected data deletion.
18. `REL-CAT-001` — publish official catalog, template, validator and release pipeline.

## 21. Acceptance

- Connection, image, Compose and native-extension delivery all work from official and custom sources.
- Unsigned definitions install after one warning; a known invalid signature is rejected.
- Tags are locked to digests before apply.
- Server and Runtime Manager never receive user-workload Docker access.
- A Connector can mutate only the installation named by matching stable ownership labels.
- Preview detects unsupported architecture, conflicts and new critical rights before apply.
- Source outage serves the last valid cache with a stale marker.
- Update preserves data and restore succeeds from a verified backup.
- Uninstall does not delete external, shared or retained data or backups.
- Unsigned native frontend code cannot reach Shell DOM, cookies or arbitrary Core APIs.
- Unknown native process workers cannot run under the Server identity.
- Secret values are absent from database request payloads, Operations, Connector queues, audit and logs.
- Real Docker/Compose end-to-end tests cover volumes, recreation, backup and uninstall.
