# Development stack

This directory runs JulOS Server and PostgreSQL locally. It is a development stack, not a deployment reference.

## Start

```bash
cd deploy/compose
cp .env.example .env
```

Set `JULOS_POSTGRES_PASSWORD` in `.env`. The stack refuses to start without it, so no weak default password can reach a running system.

```bash
docker compose up --build
```

The server answers on `http://127.0.0.1:8080`.

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

PostgreSQL data lives in the named volume `julos-dev_postgres-data` and survives `docker compose down`.

```bash
docker compose down --volumes   # discards the development database
```

No database port is published. The database is reachable only from the stack network. To inspect it, attach to the container:

```bash
docker compose exec postgres psql --username julos --dbname julos
```

## Scope

Runtime Manager, package workers, Browser runtimes and Remote runtimes are not part of this stack. They are added by the work items that implement them.
