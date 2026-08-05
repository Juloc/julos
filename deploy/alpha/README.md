# JulOS 0.3.0 alpha

This directory installs the first testable JulOS alpha from immutable container tags. It is intended for evaluation on a trusted host, not direct public exposure.

## Requirements

- Docker Engine with Docker Compose
- OpenSSL for initial key generation
- `sudo` or equivalent root access for assigning the protected key ring to the container user
- access to `ghcr.io/juloc/julos-server:0.3.0-alpha.1`

## Install

```bash
mkdir -p julos-alpha
cd julos-alpha
curl -O https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.1/deploy/alpha/compose.yaml
curl -o .env.example https://raw.githubusercontent.com/Juloc/julos/v0.3.0-alpha.1/deploy/alpha/.env.example
cp .env.example .env
```

Set a long random `JULOS_POSTGRES_PASSWORD` in `.env`, then pull the exact image and create the external encryption key ring:

```bash
docker compose --env-file .env pull

server_image="$(sed -n 's/^JULOS_SERVER_IMAGE=//p' .env)"
container_user="$(docker image inspect --format '{{.Config.User}}' "$server_image")"
container_uid="${container_user%%:*}"
container_gid="${container_user#*:}"
if [ "$container_gid" = "$container_user" ]; then
  container_gid="$container_uid"
fi
case "$container_uid:$container_gid" in
  *[!0-9:]*|:|*:|:* ) echo "The server image does not declare a numeric runtime user." >&2; exit 1 ;;
esac

mkdir -p secret-keys
umask 077
openssl rand -base64 32 > secret-keys/primary.key
sudo chown -R "$container_uid:$container_gid" secret-keys
sudo chmod 700 secret-keys
sudo chmod 600 secret-keys/primary.key
```

The key directory remains unreadable to unrelated host users. Ownership matches the fixed non-root user declared by the server image, so the container can read it without running as root or weakening the file mode.

Start the stack:

```bash
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

sudo tar -czf julos-alpha-secret-keys.tar.gz secret-keys
sudo chown "$(id -u):$(id -g)" julos-alpha-secret-keys.tar.gz
```

Store the key-ring backup separately and protect it like a password. Losing `primary.key` makes encrypted secret values unreadable.

## Upgrade

Read the target release notes first. Create a database and key-ring backup, then change `JULOS_VERSION` and `JULOS_SERVER_IMAGE` in `.env` to the exact target version and run:

```bash
docker compose --env-file .env pull

server_image="$(sed -n 's/^JULOS_SERVER_IMAGE=//p' .env)"
container_user="$(docker image inspect --format '{{.Config.User}}' "$server_image")"
container_uid="${container_user%%:*}"
container_gid="${container_user#*:}"
if [ "$container_gid" = "$container_user" ]; then
  container_gid="$container_uid"
fi
sudo chown -R "$container_uid:$container_gid" secret-keys

docker compose --env-file .env up -d
```

Reapplying ownership handles a future image that deliberately changes its runtime UID. The migration service applies committed schema migrations before the new server starts.

## Rollback

A container rollback does not reverse database migrations. Before upgrading, create a database and key-ring backup. To return to this alpha after an incompatible migration:

```bash
docker compose --env-file .env down
docker compose --env-file .env up -d postgres
cat julos-alpha.dump | docker compose --env-file .env exec -T postgres \
  pg_restore --clean --if-exists --username julos --dbname julos
```

Restore the matching key ring, set the previous exact image tag in `.env`, assign the key directory to that image's runtime UID as shown in the installation steps, and start the complete stack.

## Reset

This permanently removes the alpha database. It does not delete the host key directory.

```bash
docker compose --env-file .env down --volumes
```

## Alpha boundaries

This build is for checking the JulOS direction: desktop shell, account setup, settings, package platform and the current Agent/Remote/Browser foundations. Docker, Proxmox, Files, Caddy, discovery, production hardening and Julgate migration are not complete. Remote providers and Browser runtime still require environment-specific deployment configuration and validation.
