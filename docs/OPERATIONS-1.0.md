# JulOS 1.0 operations

## Safe mode

Set `JULOS_SAFE_MODE=true` before Server startup. The Server reports the state at `GET /api/v1/system/safe-mode` and rejects optional package enablement while safe mode is active. Disable safe mode only after the failing package or configuration has been corrected.

## Agent enrollment

Create a short-lived enrollment token through the authenticated Server API. Configure a new Linux Agent with:

- `JULOS_SERVER_URL`: HTTPS URL of the JulOS Server. Plain HTTP is accepted only for a loopback development endpoint.
- `JULOS_AGENT_ENROLLMENT_TOKEN`: one short-lived token used only until durable enrollment succeeds.
- `JULOS_AGENT_NAME`: optional administrator-visible host name.
- `JULOS_AGENT_IDENTITY_PATH`: optional absolute path; defaults to `/var/lib/julos-agent/identity.json` on Linux.
- `JULOS_AGENT_MACHINE_ID_PATH`: optional absolute machine-identity source; defaults to `/etc/machine-id`.

The Agent creates its durable credential locally, writes a pending identity document atomically, and retries only the exact same enrollment attempt after transient failures. The Server accepts an exact retry for recovery from a lost response and rejects any changed reuse of the token.

On Linux, the identity document must be a regular file with mode `0600`. Symbolic links and group- or world-readable files are rejected. After successful enrollment, remove `JULOS_AGENT_ENROLLMENT_TOKEN` from the service configuration. Restarts load the durable Agent ID and credential from the protected identity document.

Do not print, copy into diagnostics, or place enrollment tokens or identity documents in Compose files, command history, issue reports, or logs. Revoke the Agent from JulOS before intentionally deleting its identity document.

## Backup

Run `tools/backup.sh` with `JULOS_BACKUP_POSTGRES` and, when package data is outside the default path, `JULOS_PACKAGE_ROOT`. The command creates a PostgreSQL custom-format dump, package-data archive, metadata and SHA-256 manifest in a temporary directory before atomically publishing the backup.

A backup is not accepted operationally until a restore drill has completed against a separate environment.

## Restore

Stop Server, Agent-facing mutations and package workers. Run:

```sh
tools/restore.sh <backup-directory> --confirm-destructive-restore
```

The restore verifies every checksum before changing PostgreSQL or package data. PostgreSQL restore uses one transaction. Package data is extracted to staging and swapped only after successful extraction.

## Diagnostics

Run `tools/diagnostics.sh`. The bundle contains version, kernel, health responses and JulOS-managed container status. It does not collect environment variables, connection strings, cookies, Agent credentials, secret references or database rows.

## Upgrade order

1. Produce and verify a backup.
2. Read release notes and irreversible migration warnings.
3. Pull immutable image digests.
4. Run database migration as a one-shot job.
5. Start Server and verify readiness.
6. Upgrade Agents without automatic downgrade.
7. Update packages only after preview approval.
8. Verify package, Agent and problem-center health.

## Rollback

Application image rollback is allowed only when database and package migrations are backward compatible. Otherwise restore the matching backup. Agent downgrade always requires explicit approval and a verified artifact digest.
