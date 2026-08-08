# Development stack

This directory runs JulOS Server and PostgreSQL locally. It is a development stack, not a deployment reference.

## Start

```bash
cd deploy/compose
cp .env.example .env
```

Set `JULOS_POSTGRES_PASSWORD` in `.env`. The stack refuses to start without it, so no weak default password can reach a running system.

Create the external secret-encryption key ring before the first start:

```bash
mkdir -p secret-keys
umask 077
openssl rand -base64 32 > secret-keys/primary.key
```

`JULOS_SECRET_KEY_RING_PATH` points at this host directory. The directory is mounted read-only at `/run/julos-secret-keys`; it is not part of the PostgreSQL volume or database backup. Keep the key ring in a separate protected backup. Losing every key file makes existing secret references undecryptable.

```bash
docker compose up --build
```

The one-shot `migrate` service applies committed Entity Framework Core migrations after PostgreSQL becomes healthy. Server starts only after migration succeeds. A migration failure therefore leaves Server stopped instead of running against an unknown schema.

The server answers on `http://127.0.0.1:8080`.


A fresh database has no account. `GET /api/v1/auth/status` reports `setupRequired: true`; `POST /api/v1/auth/setup` creates the only initial administrator and returns the secure session cookie. Setup cannot be repeated after the database transaction records completion.

To apply migrations outside Compose, set `ConnectionStrings__CoreDatabase` and run:

```bash
dotnet tool restore
dotnet run --project src/JulOS.Server -- --migrate-database
```

Do not edit core tables or migration history manually.

## Health

| Endpoint | Meaning |
|---|---|
| `/health/live` | the process is running; no dependency is checked, so a database outage cannot cause a restart loop |
| `/health/ready` | the core database accepted a query |

The container health check runs the application itself:

```bash
dotnet /application/JulOS.Server.dll --health-check
```

The runtime image ships no HTTP client tool, so the probe adds no attack surface.

## Data

PostgreSQL data lives in the named volume `julos-dev_postgres-data` and survives `docker compose down`. Encrypted secret values are stored in PostgreSQL, while the AES key ring remains in the separately managed host directory configured by `JULOS_SECRET_KEY_RING_PATH`.

```bash
docker compose down --volumes   # discards the development database
```

No database port is published. The database is reachable only from the stack network. To inspect it, attach to the container:

```bash
docker compose exec postgres psql --username julos --dbname julos
```

## Remote (RDP/VNC/SSH)

The `runtime-manager` service and the `Remote__*` variables in `.env.example` are optional and disabled by default; Remote sessions fail closed with no configured provider until you set them. To enable Remote:

1. Install the signed `de.juloc.julos.remote` package through the running server (`POST /api/v1/packages/install`).
2. Build and publish (or build locally and reference by digest) the provider runtime image from `packages/JulOS.Remote/runtime/Dockerfile`; see `docs/REMOTE-PROVIDER-RUNTIME.md`.
3. Fill in the Remote variables in `.env` (`openssl rand -hex 32` for both key values), pointing `JULOS_REMOTE_PROVIDER_0_IMAGE` at that digest-pinned image.
4. Start the stack with the `remote` profile so `runtime-manager` also runs:

```bash
docker compose --profile remote up --build
```

`runtime-manager` needs the host's Docker socket to create the narrow, labeled containers it owns, so it is not part of the default profile.

## Scope

Package workers and Browser session runtimes are not part of this stack. They are added by the work items that implement them.
