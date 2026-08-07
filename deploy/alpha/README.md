# JulOS 0.3.0 alpha

This directory installs the testable JulOS alpha from immutable container tags. It is intended for evaluation on a trusted host, not direct public exposure.

## Requirements

- Docker Engine with Docker Compose
- OpenSSL for generating the required values
- access to `ghcr.io/juloc/julos-server:0.3.0-alpha.6`

## SQLite single-service stack

`compose.sqlite.yaml` is the recommended evaluation deployment. It runs one JulOS server container with one persistent volume. PostgreSQL and a migration service are not required.

```bash
mkdir -p julos-alpha
cd julos-alpha
curl -O https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.6/deploy/alpha/compose.sqlite.yaml
curl -o .env.example https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.6/deploy/alpha/.env.sqlite.example
cp .env.example .env
openssl rand -base64 32
```

Store the generated value as `JULOS_PRIMARY_KEY` in `.env`, then start JulOS:

```bash
docker compose --env-file .env -f compose.sqlite.yaml pull
docker compose --env-file .env -f compose.sqlite.yaml up -d
docker compose --env-file .env -f compose.sqlite.yaml ps
```

SQLite is intended for one server replica on one host. Do not share its database file between containers or hosts. Core data is stored below `/var/lib/julos/data`, package data below `/var/lib/julos/packages`, and protected Data Protection keys below `/var/lib/julos/data-protection`. Process package workers use their own isolated SQLite files and are supported in this profile. Container package runtimes still require the Runtime Manager transport and its deployment-specific Docker access.

On a fresh SQLite volume the server creates the schema before it accepts requests. Alpha SQLite schema upgrades are not migrated automatically; back up data before changing to a release with a different schema and follow that release's upgrade notes.

JulOS answers on `http://127.0.0.1:8080` unless the bind address or port was changed. On a fresh database, open JulOS and create the initial administrator. Setup is accepted only once.

## PostgreSQL stack

Use `compose.yaml` when PostgreSQL is preferred. It runs PostgreSQL, a one-shot migration container and the JulOS server.

```bash
curl -O https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.6/deploy/alpha/compose.yaml
curl -o .env.example https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.6/deploy/alpha/.env.example
cp .env.example .env
```

Fill the two required values in `.env`:

```bash
openssl rand -hex 32
openssl rand -base64 32
```

Use the first value for `JULOS_POSTGRES_PASSWORD` and the second for `JULOS_PRIMARY_KEY`, then start the stack:

```bash
docker compose --env-file .env pull
docker compose --env-file .env up -d
docker compose --env-file .env ps
```

## Health and logs

SQLite stack:

```bash
docker compose --env-file .env -f compose.sqlite.yaml ps
docker compose --env-file .env -f compose.sqlite.yaml logs --tail=200 server
```

PostgreSQL stack:

```bash
docker compose --env-file .env ps
docker compose --env-file .env logs --tail=200 server migrate postgres
```

## Backup

SQLite:

```bash
docker compose --env-file .env -f compose.sqlite.yaml stop server
docker run --rm -v julos-alpha_julos-data:/data -v "$PWD":/backup alpine \
  cp /data/data/julos.db /backup/julos-alpha.db
docker compose --env-file .env -f compose.sqlite.yaml start server
```

PostgreSQL:

```bash
docker compose --env-file .env exec -T postgres \
  pg_dump --username julos --dbname julos --format=custom > julos-alpha.dump
```

Keep the current `JULOS_PRIMARY_KEY` in a protected password store. Losing it makes encrypted JulOS secrets and protected Data Protection keys unreadable.

## Upgrade

Read the target release notes and create a database backup. Keep the same `JULOS_PRIMARY_KEY`, change the exact server image tag, then run the `pull` and `up -d` commands for the selected Compose file. PostgreSQL uses the migration service. For SQLite, follow the target release notes whenever the schema changes.

If an earlier failed alpha created an unusable disposable SQLite evaluation volume, remove that volume only when it contains no data you need. Existing usable alpha.4 volumes upgrade in place to alpha.6.

## Rollback

A container rollback does not reverse database schema changes. Restore the matching database backup when an upgrade changed the schema incompatibly, set the previous exact image tag, and keep the same primary key.

## Reset

This permanently removes the selected database and Data Protection keys:

```bash
docker compose --env-file .env down --volumes
# or
docker compose --env-file .env -f compose.sqlite.yaml down --volumes
```

## Alpha boundaries

This build is for checking the JulOS direction: desktop shell, account setup, settings, package platform and the current Agent/Remote/Browser foundations. Docker, Proxmox, Files, Caddy, discovery, production hardening and Julgate migration are not complete. Browser container sessions and deployed Remote providers still require their Runtime Manager/provider environment before their full end-to-end flows can be exercised.
