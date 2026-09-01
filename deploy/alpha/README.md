# JulOS 0.4.0 beta

This directory contains the release-oriented JulOS beta deployment files. The recommended evaluation profile is the SQLite single-service stack; it keeps Core persistence in one named volume and does not require PostgreSQL.

## Requirements

- Docker Engine with Docker Compose
- OpenSSL for generating `JULOS_PRIMARY_KEY`
- access to `ghcr.io/juloc/julos-server:0.4.0-beta.26`

## Recommended SQLite stack

```bash
mkdir -p julos-beta
cd julos-beta
curl -O https://raw.githubusercontent.com/Juloc/julos/v0.4.0-beta.26/deploy/alpha/compose.sqlite.yaml
curl -o .env.example https://raw.githubusercontent.com/Juloc/julos/v0.4.0-beta.26/deploy/alpha/.env.sqlite.example
cp .env.example .env
openssl rand -base64 32
```

Store the generated value as `JULOS_PRIMARY_KEY` in `.env`, then start JulOS:

```bash
docker compose --env-file .env -f compose.sqlite.yaml pull
docker compose --env-file .env -f compose.sqlite.yaml up -d
docker compose --env-file .env -f compose.sqlite.yaml ps
```

The server listens on `http://127.0.0.1:8080` by default. Core data, package state and Data Protection keys persist in the `julos-data` named volume below `/var/lib/julos`.

SQLite supports one JulOS server replica on one host. Do not share its database file between containers or hosts.

## Package Store

This beta contains the Official Package Store. Official Browser, Remote and Host Metrics packages are signed during the release workflow and embedded into the server image. The signing private key is never copied into the image or release artifacts.

Browser and Remote package execution use the separate Runtime Manager boundary. The JulOS server itself must never receive the Docker socket. A deployment that enables Browser/Remote runtimes must run the Runtime Manager separately and grant Docker access only to that component.

The Browser Runtime remains pinned to its immutable image digest. Remote provider runtime images are also consumed by immutable digest through the configured Remote provider policy.

## Health and logs

```bash
docker compose --env-file .env -f compose.sqlite.yaml ps
docker compose --env-file .env -f compose.sqlite.yaml logs --tail=200 server
```

## Backup

```bash
docker compose --env-file .env -f compose.sqlite.yaml stop server
docker run --rm -v julos-beta_julos-data:/data -v "$PWD":/backup alpine \
  cp /data/data/julos.db /backup/julos-beta.db
docker compose --env-file .env -f compose.sqlite.yaml start server
```

Keep `JULOS_PRIMARY_KEY` in a protected password store. Losing it makes protected JulOS secret material unreadable.

## Upgrade and rollback

Before changing versions, create a database backup and read the target release notes. Keep the same `JULOS_PRIMARY_KEY`, update the immutable server image tag and run `pull` followed by `up -d`.

A container rollback does not reverse incompatible database schema changes. Restore the matching database backup when a release explicitly requires it.

## Reset

This permanently removes the selected database and Data Protection keys:

```bash
docker compose --env-file .env -f compose.sqlite.yaml down --volumes
```

## Beta boundaries

This beta is intended for active homelab testing of the desktop shell, account setup, settings, official packages, Browser and Remote foundations. Docker/Proxmox discovery and the broader 1.0 package set remain later milestones.
