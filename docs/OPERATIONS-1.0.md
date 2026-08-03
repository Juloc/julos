# JulOS 1.0 operations

## Safe mode

Set `JULOS_SAFE_MODE=true` before Server startup. The Server reports the state at `GET /api/v1/system/safe-mode` and rejects optional package enablement while safe mode is active. Disable safe mode only after the failing package or configuration has been corrected.

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
