# JulOS 0.3.0 alpha

This directory installs the first testable JulOS alpha from immutable container tags. It is intended for evaluation on a trusted host, not direct public exposure.

## Requirements

- Docker Engine with Docker Compose
- OpenSSL for initial key generation
- access to `ghcr.io/juloc/julos-server:0.3.0-alpha.1`

## Install

```bash
mkdir -p julos-alpha
cd julos-alpha
curl -O https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.1/deploy/alpha/compose.yaml
curl -o .env.example https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.1/deploy/alpha/.env.example
cp .env.example .env
mkdir -p secret-keys
umask 077
openssl rand -base64 32 > secret-keys/primary.key
```

Set a long random `JULOS_POSTGRES_PASSWORD` in `.env`, then start the stack:

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

The server container is healthy only after the database accepts queries. Database migration runs as a one-shot service before the server starts.

## Backup

Back up both items. One without the other is incomplete:

```bash
docker compose --env-file .env exec -T postgres \
  pg_dump --username julos --dbname julos --format=custom > julos-alpha.dump

tar -czf julos-alpha-secret-keys.tar.gz secret-keys
```

Store the key-ring backup separately and protect it like a password. Losing `primary.key` makes encrypted secret values unreadable.

## Upgrade

Read the target release notes first. Then change `JULOS_VERSION` and `JULOS_SERVER_IMAGE` in `.env` to the exact target version and run:

```bash
docker compose --env-file .env pull
docker compose --env-file .env up -d
```

The migration service applies committed schema migrations before the new server starts.

## Rollback

A container rollback does not reverse database migrations. Before upgrading, create a database and key-ring backup. To return to this alpha after an incompatible migration:

```bash
docker compose --env-file .env down
docker compose --env-file .env up -d postgres
cat julos-alpha.dump | docker compose --env-file .env exec -T postgres \
  pg_restore --clean --if-exists --username julos --dbname julos
```

Restore the matching key ring, set the previous exact image tag in `.env`, and start the complete stack.

## Reset

This permanently removes the alpha database. It does not delete the host key directory.

```bash
docker compose --env-file .env down --volumes
```

## Alpha boundaries

This build is for checking the JulOS direction: desktop shell, account setup, settings, package platform and the current Agent/Remote/Browser foundations. Docker, Proxmox, Files, Caddy, discovery, production hardening and Julgate migration are not complete. Remote providers and Browser runtime still require environment-specific deployment configuration and validation.
