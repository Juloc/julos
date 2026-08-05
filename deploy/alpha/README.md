# JulOS 0.3.0 alpha

This directory installs the testable JulOS alpha from immutable container tags. It is intended for evaluation on a trusted host, not direct public exposure.

## Requirements

- Docker Engine with Docker Compose
- OpenSSL for generating the two required values
- access to `ghcr.io/juloc/julos-server:0.3.0-alpha.2`

## Install

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

Use the first value for `JULOS_POSTGRES_PASSWORD` and the second for `JULOS_PRIMARY_KEY`. No host directory or key file is required. Docker Compose supplies the primary key as an internal secret, and ASP.NET Data Protection uses its own named volume.

Start the stack:

```bash
docker compose --env-file .env pull
docker compose --env-file .env up -d
docker compose --env-file .env ps
```

JulOS answers on `http://127.0.0.1:8080` unless the bind address or port was changed. On a fresh database, open JulOS and create the initial administrator. Setup is accepted only once.

## Health and logs

```bash
docker compose --env-file .env ps
docker compose --env-file .env logs --tail=200 server migrate postgres
```

The server starts only after the one-shot migration service completed successfully and PostgreSQL accepts queries.

## Backup

Back up the database and keep the current `JULOS_PRIMARY_KEY` in your protected password store:

```bash
docker compose --env-file .env exec -T postgres \
  pg_dump --username julos --dbname julos --format=custom > julos-alpha.dump
```

The named Data Protection volume preserves login and antiforgery keys across server-container replacements. Losing `JULOS_PRIMARY_KEY` makes encrypted JulOS secrets and protected Data Protection keys unreadable.

## Upgrade

Read the target release notes and create a database backup. Change `JULOS_VERSION` and `JULOS_SERVER_IMAGE` in `.env`, keep the same `JULOS_PRIMARY_KEY`, then run:

```bash
docker compose --env-file .env pull
docker compose --env-file .env up -d
```

The migration service applies committed schema migrations before the new server starts.

## Rollback

A container rollback does not reverse database migrations. Restore the matching database backup when an upgrade changed the schema incompatibly, set the previous exact image tag, and keep the same primary key.

## Reset

This permanently removes the alpha database and Data Protection keys:

```bash
docker compose --env-file .env down --volumes
```

## Alpha boundaries

This build is for checking the JulOS direction: desktop shell, account setup, settings, package platform and the current Agent/Remote/Browser foundations. Docker, Proxmox, Files, Caddy, discovery, production hardening and Julgate migration are not complete. Remote providers and Browser runtime still require environment-specific deployment configuration and validation.
