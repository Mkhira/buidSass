# Local setup

One-command bring-up of the full backend stack (Postgres + Meilisearch + Mailhog + API).

## Prerequisites

- Docker Desktop (or compatible) with Compose v2
- .NET 9 SDK (for running the seed CLI / `dotnet ef`)
- ~4 GB free RAM

## First run

```bash
cp infra/local/.env.example infra/local/.env
scripts/dev/up.sh
```

Target: warm bring-up completes in under 90 seconds. On success:

- API: <http://localhost:5000/health> → `200`
- Meilisearch: <http://localhost:7700/health>
- Mailhog UI: <http://localhost:8025>
- Postgres: `localhost:5432` (user `dental` / see `.env`)

## Common scripts

| Script                        | Purpose                                           |
|-------------------------------|---------------------------------------------------|
| `scripts/dev/up.sh`           | Start stack, run EF migrations, apply seeders     |
| `scripts/dev/reset.sh`        | `compose down -v` then `up.sh` (wipes volumes)    |
| `scripts/dev/migrate.sh`      | Apply EF migrations only                          |
| `scripts/dev/seed.sh [mode]`  | `apply` (default) \| `fresh` \| `dry-run`         |
| `scripts/dev/logs.sh [svc]`   | Tail logs for a service (default: `backend_api`)  |
| `scripts/dev/down.sh`         | Stop stack, **preserve volumes**                  |

## Observability profile

OpenTelemetry collector runs under a profile:

```bash
docker compose --profile observability up -d otel-collector
```

## Troubleshooting

- **Port already in use** → change host-side port in `infra/local/docker-compose.yml` or stop the conflicting service.
- **Postgres healthcheck never passes** → `docker compose logs postgres`, most often a stale `pgdata` volume; run `reset.sh`.
- **`dotnet ef` fails** → ensure `ASPNETCORE_ENVIRONMENT=Development` and the API container is stopped (migrations run from host).
- **Seeder refuses to run in Production locally** → expected. `ASPNETCORE_ENVIRONMENT=Production dotnet run -- seed` exits non-zero by design.
