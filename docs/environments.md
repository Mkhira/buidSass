# Environments

Three runtime environments, one binary, config swapped by `ASPNETCORE_ENVIRONMENT`.

| Environment  | Hosted on            | Config source                             | Seed data                     | Integrations       |
|--------------|----------------------|-------------------------------------------|-------------------------------|--------------------|
| `Development`| Developer laptop     | `appsettings.json` + `.env`               | Local via `scripts/dev/seed.sh` | Mocks / sandboxes  |
| `Staging`    | Azure SA Central     | `appsettings.Staging.json` + Key Vault    | Auto-applied on deploy        | Sandbox / TestOnly |
| `Production` | Azure SA Central     | `appsettings.Production.json` + Key Vault | **Hard-blocked** (SeedGuard)  | Live               |

## Config layering (precedence low→high)

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. Environment variables (12-factor — double-underscore = nesting, e.g. `ConnectionStrings__DefaultConnection`)
4. Azure Key Vault (Staging, Production only) — bound via `DefaultAzureCredential`

Key Vault URIs are held in the env-specific json (`kv-dental-stg`, `kv-dental-prd`). Secret values are never committed.

## Seed-guard contract

- Production: `SeedGuard.EnsureSafe` throws regardless of config flags. Belt + braces: `Seeding:Enabled=false` in `appsettings.Production.json`.
- Staging: requires `Seeding:Enabled=true` and `Seeding:AutoApply=true`.
- Development: unrestricted; defaults to `Seeding:DatasetSize=small`.

## Phase 1E boundary

Provisioning Key Vault, managed Postgres, Meilisearch HA, and the actual staging deploy target is deferred to Phase 1E. `deploy-staging.yml` is a placeholder until then. See `docs/missing-env-docker-plan.md` §10.
