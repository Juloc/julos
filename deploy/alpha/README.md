# JulOS 0.3.0 alpha

This directory installs the testable JulOS alpha from immutable container tags. It is intended for evaluation on a trusted host, not direct public exposure.

## Requirements

- Docker Engine with Docker Compose
- OpenSSL for generating the required values
- access to `ghcr.io/juloc/julos-server:0.3.0-alpha.2`

## PostgreSQL stack

Use `compose.yaml` for the normal PostgreSQL deployment. It runs PostgreSQL, a one-shot migration container and the JulOS server.

```bash
mkdir -p julos-alpha
cd julos-alpha
curl -O https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.2/deploy/alpha/compose.yaml
curl -o .env.example https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.2/deploy/alpha/.env.example
cp .env.example .env
```

Fill the two required values in `.env`:

```bash
openssl rand -hex 32
openssl rand -base64 32
```

Use the first value for `JULOS_POSTGRES_PASSWORD` and the second for `JULOS_PRIMARY_KEY`.

Start the stack:

```bash
docker compose --env-file .env pull
docker compose --env-file .env up -d
docker compose --env-file .env ps
```

## SQLite single-service stack

Use `compose.sqlite.yaml` when one JulOS container and one persistent volume are preferred. PostgreSQL and the migration service are not started. The server creates and updates `/var/lib/julos/julos.db` itself.

```bash
curl -O https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.2/deploy/alpha/compose.sqlite.yaml
openssl rand -base64 32
```

Store the generated value as `JULOS_PRIMARY_KEY` in `.env`, then start only the server:

```bash
docker compose --env-file .env -f compose.sqlite.yaml pull
docker compose --env-file .env -f compose.sqlite.yaml up -d
docker compose --env-file .env -f compose.sqlite.yaml ps
```

SQLite is intended for one server replica on one host. Do not share its database file between containers or hosts. Packages without a separate runtime work in this mode. Process and container package runtimes remain disabled in the single-service profile; use the PostgreSQL deployment when those runtimes are required.

JulOS answers on `http://127.0.0.1:8080` unless the bind address or port was changed. On a fresh database, open JulOS and create the initial administrator. Setup is accepted only once.

## Health and logs

PostgreSQL stack:

```bash
docker compose --env-file .env ps
docker compose --env-file .env logs --tail=200 server migrate postgres
```

SQLite stack:

```bash
docker compose --env-file .env -f compose.sqlite.yaml ps
docker compose --env-file .env -f compose.sqlite.yaml logs --tail=200 server
```

## Backup

PostgreSQL:

```bash
docker compose --env-file .env exec -T postgres \
  pg_dump --username julos --dbname julos --format=custom > julos-alpha.dump
```

SQLite:

```bash
docker compose --env-file .env -f compose.sqlite.yaml stop server
docker run --rm -v julos-alpha_julos-data:/data -v "$PWD":/backup alpine \
  cp /data/julos.db /backup/julos-alpha.db
docker compose --env-file .env -f compose.sqlite.yaml start server
```

Keep the current `JULOS_PRIMARY_KEY` in a protected password store. Losing it makes encrypted JulOS secrets and protected Data Protection keys unreadable.

## Upgrade

Read the target release notes and create a database backup. Change `JULOS_VERSION` and `JULOS_SERVER_IMAGE` in `.env`, keep the same `JULOS_PRIMARY_KEY`, then run the `pull` and `up -d` commands for the selected Compose file. PostgreSQL uses the migration service. SQLite is initialized by the server before it accepts requests.

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

This build is for checking the JulOS direction: desktop shell, account setup, settings, package platform and the current Agent/Remote/Browser foundations. Docker, Proxmox, Files, Caddy, discovery, production hardening and Julgate migration are not complete. Remote providers and Browser runtime still require environment-specific deployment configuration and validation.
