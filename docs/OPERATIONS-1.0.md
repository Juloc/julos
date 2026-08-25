# JulOS 1.0 operations

## Safe mode

Set `JULOS_SAFE_MODE=true` before Server startup. The Server reports the state at `GET /api/v1/system/safe-mode` and rejects optional package enablement while safe mode is active. Disable safe mode only after the failing package or configuration has been corrected.

## Host Connector enrollment

After `HCON-002`, create a short-lived enrollment token through the authenticated Server API. Configure a new Linux Host Connector with:

- `JULOS_SERVER_URL`: HTTPS URL of the JulOS Server. Plain HTTP is accepted only for a loopback development endpoint.
- `JULOS_HOST_CONNECTOR_ENROLLMENT_TOKEN`: one short-lived token used only until durable enrollment succeeds.
- `JULOS_HOST_CONNECTOR_NAME`: optional administrator-visible host name.
- `JULOS_HOST_CONNECTOR_IDENTITY_PATH`: optional absolute path; defaults to `/var/lib/julos-host-connector/identity.json` on Linux.
- `JULOS_HOST_CONNECTOR_MACHINE_ID_PATH`: optional absolute machine-identity source; defaults to `/etc/machine-id`.

The Host Connector creates its durable credential locally, writes a pending identity document atomically, and retries only the exact same enrollment attempt after transient failures. The Server accepts an exact retry for recovery from a lost response and rejects any changed reuse of the token.

On Linux, the identity document must be a regular file with mode `0600`. Symbolic links and group- or world-readable files are rejected. After successful enrollment, remove `JULOS_HOST_CONNECTOR_ENROLLMENT_TOKEN` from the service configuration. Restarts load the durable Host Connector ID and credential from the protected identity document.

Do not print, copy into diagnostics, or place enrollment tokens or identity documents in Compose files, command history, issue reports, or logs. Revoke the Host Connector from JulOS before intentionally deleting its identity document.

### Legacy Agent identity migration

An existing beta Agent is migrated only by the explicit `HCON-002` identity-migration command. Pass `--legacy-identity-path` and `--host-connector-identity-path` for custom locations; otherwise configured environment values and then platform defaults are used. The command accepts only enrolled identity, validates files/permissions, writes atomically with mode `0600`, verifies the same ID/CredentialV1/MachineIdentityV1 and removes the old file only after success. If both files exist, exact protected-field equality is required; a mismatch returns `host_connector.identity_migration_conflict` and changes neither. A failed migration leaves the old file intact and blocks Connector startup; it does not enroll a second identity.

## Backup

The current beta `tools/backup.sh` supports PostgreSQL only. `DB-001` must add the provider-aware 1.0 path before HCON-002 or workspace schema migration is allowed.

- PostgreSQL: run `tools/backup.sh` with `JULOS_BACKUP_POSTGRES` and optional `JULOS_PACKAGE_ROOT`; it creates a custom-format dump, package-data archive, metadata and SHA-256 manifest in staging before atomic publication.
- SQLite after DB-001: stop Server and mutating workers, run the provider-aware backup command against the explicit Core database path; it checkpoints WAL, uses the SQLite backup API into staging, runs `PRAGMA integrity_check`, archives package data, writes metadata/checksums and publishes atomically. Raw copying only the `.db` file while Server runs is unsupported.

A backup is not accepted operationally until a restore drill has completed against a separate environment.

## Restore

Stop Server, Host Connector-facing mutations and package workers. For PostgreSQL run:

```sh
tools/restore.sh <backup-directory> --confirm-destructive-restore
```

The restore verifies every checksum before changing PostgreSQL or package data. PostgreSQL restore uses one transaction. After DB-001 the same provider-aware entry point recognizes SQLite backup metadata, verifies/checks the staged database with `integrity_check`, then atomically replaces only the configured database file while Server is stopped. Package data is extracted to staging and swapped only after successful extraction. A provider mismatch or missing WAL-consistent metadata fails before mutation.

## Diagnostics

Run `tools/diagnostics.sh`. The bundle contains version, kernel, health responses and JulOS-managed container status. It does not collect environment variables, connection strings, cookies, Host Connector credentials, secret references or database rows.

## Upgrade order

1. Produce and verify a backup.
2. Read release notes and irreversible migration warnings.
3. Pull immutable image digests.
4. Run database migration as a one-shot job.
5. For HCON-002, enter legacy-request drain, terminalize queued Commands with the documented upgrade failure code and stop if running work does not finish by the deadline.
6. Start Server and verify readiness.
7. Run the documented legacy identity migration and upgrade Host Connectors without automatic downgrade.
8. Update packages only after preview approval.
9. Verify package, Host Connector, application-installation and problem-center health.

## Rollback

Application image rollback is allowed only when database and package migrations are backward compatible. Otherwise restore the matching backup. Host Connector downgrade always requires explicit approval and a verified artifact digest; rollback across the Agent-to-Connector protocol cut is unsupported unless the release note explicitly provides a database and identity rollback.

## Session runtimes (Browser and Remote)

Interactive Browser and Remote sessions launch a per-session runtime and a Remote provider container through Runtime Manager. Those images are large (Browser runtime ~1.3 GB, Remote provider ~1.8 GB) and are pulled on first use.

- Pre-pull the digest-pinned runtime images on the runtime host after deploy or image cleanup. Otherwise the first session's runtime create can exceed the request timeout and be cancelled; the provider's connect-callback then fails with `404` and the session never leaves "connecting". Once the images are cached, sessions connect in seconds. The digests are in `.github/workflows/release.yml` (`JULOS_BROWSER_RUNTIME_IMAGE`) and the Remote provider configuration (`Remote:Providers:0:Image`).
- Give the runtime host enough CPU headroom. A Browser runtime that misses its 30-second display-startup grace under heavy load self-terminates with "The VNC display endpoint did not become ready" (exit 70) and the session fails to connect.
- The runtime network (`julos-remote`) must resolve to the literal on-host name and route to whatever internal targets the session needs; see `docs/BROWSER-RUNTIME.md` for the Browser's persistent, same-origin access model.
