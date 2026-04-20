# Amendment A1 — Environments, Docker, and Seed Data Retrofit

**Status**: proposed (Phase 1B amendment)
**Date**: 2026-04-20
**Owners**: platform / backend
**Supersedes**: nothing. Amends `docs/implementation-plan.md`.
**Does NOT amend**: the Constitution (32 principles), ADR-001..ADR-010.

---

## §1. Context & Scope

### 1.1 Why this retrofit exists

Phase 1A closed with the repo skeleton, CI hardening, and the audit/storage baseline migration. Phase 1B spec work is in flight (specs 004–008 authored through Spec-Kit). Before any 004–008 code lands, the project is missing three load-bearing capabilities that every subsequent implementation PR will depend on:

1. **Explicit runtime environments** — today the backend has only `appsettings.json` and `appsettings.Development.json`. There is no staging config, no production config, no secrets contract, no way to run the same container in all three places.
2. **Docker-based local development** — there is no `Dockerfile`, no `docker-compose.yml`, no `.dockerignore`. Developers currently have to install Postgres + (eventually) Meilisearch + .NET 9 SDK on their host. This will collapse under spec 008's timezone-aware batch sweeper (needs IANA tzdata) and spec 006's Meilisearch dependency.
3. **Synthetic seed data** — there is no seed framework. Spec 005/006/007/008 cannot be demoed, manually tested, or load-tested without realistic per-market data covering catalog, pricing, inventory, and search.

### 1.2 Critical correction to the original draft

The original (ChatGPT-authored) amendment treated specs 001–008 as *implemented* and asked for seed data that covers those implemented domains. The repo audit shows otherwise: specs 004–008 are specs-only, and only migrations `InitialBaseline` + `RevokeAuditWriteGrants` exist in `services/backend_api/Migrations/`. Seeding domains that don't exist would fail.

This amendment therefore separates **framework scaffolding** (shipped now, in one PR) from **per-spec seeders** (shipped with each spec's implementation PR). See §8 for the exact sequencing.

### 1.3 Intended outcome

After this retrofit an engineer can:

- `scripts/dev/up.sh` → full local stack up in under 90 seconds on an M-series laptop.
- Run the .NET test suite against Testcontainers Postgres with no host dependencies.
- Tag a container image in CI and (eventually) deploy it to staging without config rewrites.
- Load synthetic bilingual seed data for whichever spec modules are implemented at the time.
- Be hard-blocked by `SeedGuard` from ever seeding a production database.

### 1.4 Non-goals

The following are **explicitly out of scope** for this retrofit:

- Azure Container Apps IaC, Bicep/Terraform modules, Key Vault provisioning, DNS, TLS — all deferred to **Phase 1E** (spec 016) per `infra/README.md` and the implementation plan.
- Containerising `apps/customer_flutter/` or `apps/admin_web/` — those keep their existing dev servers and consume the dockerised backend over HTTP.
- Marketplace multi-tenancy — single-vendor operational model per Principle 6.
- Replacing any accepted ADR decision.

### 1.5 Constitution & ADR compliance at a glance

| Anchor | Status |
|---|---|
| Principle 22 (Fixed Tech Decisions) | unchanged — .NET / Flutter / Postgres / Next.js |
| Principle 25 (Audit) | seeding writes `seeding.applied` rows to `audit.events` |
| Principle 28 (AI-Build Standard) | deterministic, idempotent, env-explicit |
| Principle 30 (Phasing) | retrofit sits inside Phase 1B; per-spec seeders ride their PRs |
| Principle 31 (Constitution Supremacy) | no conflict introduced |
| ADR-004 (EF Core 9) | migrations + seeders use EF Core |
| ADR-005 (Meilisearch) | client + indexer wired in this retrofit |
| ADR-010 (Azure SA Central, single-region) | staging + prod both target SA Central; local is residency-agnostic |

---

## §2. Environment Strategy

### 2.1 Environment model

Three environments. Selection is driven exclusively by the `ASPNETCORE_ENVIRONMENT` env var. No other switch (no custom `AppEnv` enum, no feature flags layered on top) decides environment-level behaviour.

| Env | `ASPNETCORE_ENVIRONMENT` | Purpose |
|---|---|---|
| local | `Development` | Developer laptop via Docker Compose |
| staging | `Staging` | Production-like in Azure SA Central; synthetic data only |
| production | `Production` | Customer-facing; real data; seed hard-blocked |

### 2.2 Config layering

Standard ASP.NET Core precedence (later wins):

```
appsettings.json                     # baseline (safe defaults, no secrets)
appsettings.{Environment}.json       # env-specific overrides
environment variables (__ delimiter) # runtime overrides
Azure Key Vault (staging/prod)       # secrets, bound via DefaultAzureCredential
command-line arguments               # CLI verbs (migrate, seed) only
```

**Rules**:
- Secrets **never** live in any `appsettings.*.json` file. Commits are CI-scanned for secret patterns.
- `appsettings.json` carries safe baselines (log level Information, timeout seconds, page sizes, retry counts).
- `appsettings.Development.json` is the only file that may reference `localhost` hostnames.
- `appsettings.Staging.json` and `appsettings.Production.json` reference **Key Vault URIs**, not raw values, for every secret.
- Env vars override any file value. CI uses this for ephemeral test DB connection strings.

### 2.3 Per-environment matrix

| Concern | local | staging | production |
|---|---|---|---|
| Config source | `appsettings.Development.json` + `infra/local/.env` | `appsettings.Staging.json` + Key Vault `kv-dental-stg` | `appsettings.Production.json` + Key Vault `kv-dental-prd` |
| Database | Postgres 16 in Compose (`dental_commerce_dev`) | Azure Postgres Flexible (single-region SA Central) | Azure Postgres Flexible (single-region SA Central, HA) |
| Search | Meilisearch 1.10 in Compose | Meilisearch managed VM in SA Central | Meilisearch managed VM in SA Central (HA deferred to Phase 1.5) |
| Storage | local bind mount `./.data/files` | Azure Blob — `stgstoragedental` | Azure Blob — `prdstoragedental` |
| Logging | Serilog → Console (pretty) | Serilog → Console + Application Insights (sampled 100%) | Serilog → Console + Application Insights (sampled per SC tier) |
| Seeding | **enabled**, developer-triggered | **enabled**, idempotent, runs once per seeder version | **disabled** by `SeedGuard`; process exits code 1 if invoked |
| External integrations | mailhog (SMTP capture), payment = mock | sandbox / test keys for payment, SMS, push | live keys |
| Audit retention | 30 days | 7 years (matches prod) | 7 years |
| Data residency | n/a | SA Central (ADR-010) | SA Central (ADR-010) |
| Telemetry sampling | 100% | 100% | per-span tier (reads 5%, writes 100%) |

### 2.4 Secrets contract

Every environment-sensitive value has exactly one source per environment. Secrets listed below are the union across modules; not every spec needs all of them.

| Key | local source | staging source | production source |
|---|---|---|---|
| `ConnectionStrings:Default` | `.env` | Key Vault `pg-conn` | Key Vault `pg-conn` |
| `Meilisearch:Url` | `.env` | `appsettings.Staging.json` | `appsettings.Production.json` |
| `Meilisearch:MasterKey` | `.env` (dev-only value) | Key Vault `meili-key` | Key Vault `meili-key` |
| `Storage:ConnectionString` | n/a (bind mount) | Key Vault `blob-conn` | Key Vault `blob-conn` |
| `Identity:Jwt:SigningKey` | `.env` (dev-only value) | Key Vault `jwt-signing` | Key Vault `jwt-signing` |
| `Payment:<Provider>:SecretKey` | dev mock (no secret) | Key Vault `pay-<provider>-test` | Key Vault `pay-<provider>-live` |
| `Notifications:Sms:ApiKey` | mock | Key Vault `sms-key-test` | Key Vault `sms-key-live` |
| `OpenTelemetry:Endpoint` | `http://otel-collector:4317` | Key Vault or AppInsights connection string | Key Vault or AppInsights connection string |

Binding code lives in `services/backend_api/Configuration/ConfigurationExtensions.cs` (new):

```csharp
public static WebApplicationBuilder AddLayeredConfiguration(this WebApplicationBuilder b)
{
    var env = b.Environment.EnvironmentName;
    b.Configuration
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{env}.json", optional: true)
        .AddEnvironmentVariables();

    if (env is "Staging" or "Production")
    {
        var vaultUri = b.Configuration["KeyVault:Uri"]
            ?? throw new InvalidOperationException("KeyVault:Uri missing for " + env);
        b.Configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
    }
    return b;
}
```

### 2.5 SeedGuard

`services/backend_api/Features/Seeding/SeedGuard.cs` — a static precondition every seeder call passes through:

```csharp
public static void EnsureSafe(IHostEnvironment env, IConfiguration cfg)
{
    if (env.IsProduction())
        throw new InvalidOperationException(
            "SeedGuard: seeding is hard-blocked in Production, regardless of config flags.");

    if (env.IsStaging() && cfg.GetValue<bool>("Seeding:AutoApply") is false)
        throw new InvalidOperationException(
            "SeedGuard: Staging seeding requires Seeding:AutoApply=true.");
}
```

The guard is also wired into the CLI verb (`dotnet run -- seed`) so the process exits with non-zero before any DB write is attempted.

---

## §3. Docker / Compose

### 3.1 Dockerfile (new: `services/backend_api/Dockerfile`)

Multi-stage, distroless runtime, non-root user, IANA tzdata included (required by spec 008 warehouse-local expiry sweep).

```dockerfile
# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY backend_api.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-jammy-chiseled-extra AS runtime
# chiseled-extra includes tzdata; required for NodaTime Asia/Riyadh + Africa/Cairo
WORKDIR /app
COPY --from=build /out ./
USER 10001:10001
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Development \
    DOTNET_RUNNING_IN_CONTAINER=true
HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=3 \
  CMD ["./backend_api", "health", "--probe=ready"]
ENTRYPOINT ["./backend_api"]
```

Notes:
- `runtime-deps:9.0-jammy-chiseled-extra` is picked over the smaller `chiseled` because it ships `tzdata` and `ICU` — both needed by NodaTime + Arabic normalisation in spec 006.
- Single image for all environments. `ASPNETCORE_ENVIRONMENT` is overridden at deploy time.
- Non-root uid 10001 matches Azure Container Apps security baseline.

### 3.2 `.dockerignore` (new: `services/backend_api/.dockerignore`)

```
**/bin
**/obj
**/node_modules
**/.git
**/.github
**/Tests
**/*.md
**/appsettings.Development.json
**/*.user
**/.vs
**/.vscode
```

### 3.3 Compose (new: `infra/local/docker-compose.yml`)

```yaml
name: dental-local

services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_USER: dental
      POSTGRES_PASSWORD: dental_dev_pw
      POSTGRES_DB: dental_commerce_dev
    volumes:
      - pgdata:/var/lib/postgresql/data
    ports: ["5432:5432"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U dental -d dental_commerce_dev"]
      interval: 5s
      timeout: 3s
      retries: 20

  meilisearch:
    image: getmeili/meilisearch:v1.10
    environment:
      MEILI_ENV: development
      MEILI_MASTER_KEY: dev_master_key_not_for_prod
    volumes:
      - meilidata:/meili_data
    ports: ["7700:7700"]
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:7700/health"]
      interval: 5s
      timeout: 3s
      retries: 20

  mailhog:
    image: mailhog/mailhog:latest
    ports: ["1025:1025", "8025:8025"]

  otel-collector:
    image: otel/opentelemetry-collector-contrib:0.103.1
    command: ["--config=/etc/otel-collector.yaml"]
    volumes:
      - ./otel-collector.yaml:/etc/otel-collector.yaml:ro
    ports: ["4317:4317", "4318:4318"]
    profiles: ["observability"]

  backend_api:
    build:
      context: ../../services/backend_api
      dockerfile: Dockerfile
    env_file: .env
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Default: Host=postgres;Port=5432;Username=dental;Password=dental_dev_pw;Database=dental_commerce_dev
      Meilisearch__Url: http://meilisearch:7700
      Meilisearch__MasterKey: dev_master_key_not_for_prod
      Notifications__Smtp__Host: mailhog
      Notifications__Smtp__Port: 1025
    depends_on:
      postgres: { condition: service_healthy }
      meilisearch: { condition: service_healthy }
    ports: ["5000:5000"]

volumes:
  pgdata:
  meilidata:
```

### 3.4 `.env.example` (new: `infra/local/.env.example`)

```bash
# Copy to infra/local/.env (gitignored) before `docker compose up`.
# Values below are dev-only placeholders — never use in staging or production.

ASPNETCORE_ENVIRONMENT=Development

# DB
POSTGRES_USER=dental
POSTGRES_PASSWORD=dental_dev_pw
POSTGRES_DB=dental_commerce_dev

# Meilisearch
MEILI_MASTER_KEY=dev_master_key_not_for_prod

# Identity (dev only; staging/prod pull from Key Vault)
IDENTITY__JWT__SIGNINGKEY=dev_jwt_signing_min_32_chars_for_testing_only

# Seeding
SEEDING__AUTOAPPLY=true
SEEDING__DATASETSIZE=small   # small | medium | large
```

### 3.5 Developer scripts (new: `scripts/dev/*.sh`)

All POSIX, idempotent, exit non-zero on failure, colour-coded logs via `printf`.

| Script | Does |
|---|---|
| `up.sh` | `docker compose up -d` → waits for all healthchecks → runs `migrate.sh` → runs `seed.sh` |
| `reset.sh` | `docker compose down -v` → `up.sh` (fresh volumes + fresh seed) |
| `migrate.sh` | `dotnet ef database update` against the compose Postgres |
| `seed.sh` | `dotnet run -- seed --mode=${1:-apply}` — `apply` (idempotent) or `fresh` (wipe + reload) |
| `logs.sh` | `docker compose logs -f ${1:-backend_api}` |
| `down.sh` | `docker compose down` (preserves volumes) |

Each script begins with `#!/usr/bin/env bash` and `set -euo pipefail`.

### 3.6 Bring-up acceptance

On an Apple Silicon laptop with Docker Desktop:
- `scripts/dev/up.sh` cold (no cached image) → ≤ 5 min.
- `scripts/dev/up.sh` warm (cached image) → ≤ 90 s.
- `curl localhost:5000/health/ready` → `200 OK` after bring-up.

---

## §4. Staging vs. Production Separation

### 4.1 `appsettings.Staging.json` shape

```json
{
  "Serilog": { "MinimumLevel": { "Default": "Information" } },
  "KeyVault": { "Uri": "https://kv-dental-stg.vault.azure.net/" },
  "Meilisearch": { "Url": "https://search-stg.dental.example.com" },
  "Notifications": { "Mode": "TestOnly" },
  "Payment": { "Mode": "Sandbox" },
  "Seeding": {
    "Enabled": true,
    "AutoApply": true,
    "DatasetSize": "medium"
  },
  "DataResidency": { "Region": "sa-central" }
}
```

### 4.2 `appsettings.Production.json` shape

```json
{
  "Serilog": { "MinimumLevel": { "Default": "Warning" } },
  "KeyVault": { "Uri": "https://kv-dental-prd.vault.azure.net/" },
  "Meilisearch": { "Url": "https://search.dental.example.com" },
  "Notifications": { "Mode": "Live" },
  "Payment": { "Mode": "Live" },
  "Seeding": {
    "Enabled": false,
    "AutoApply": false
  },
  "DataResidency": { "Region": "sa-central" }
}
```

`Seeding:Enabled=false` is a belt; `SeedGuard` is the braces. Both must agree.

### 4.3 CI workflow additions

| Workflow | Trigger | Purpose |
|---|---|---|
| `.github/workflows/docker-build.yml` | push to `main` + PR | Build `services/backend_api/Dockerfile`, push to GHCR tagged `${sha}` and (for main) `latest-main`. |
| `.github/workflows/deploy-staging.yml` | manual `workflow_dispatch` | Placeholder that prints "deploy target not yet provisioned — Phase 1E spec 016". Exists so the trigger wiring lands now without blocking on IaC. |

No changes to `build-and-test.yml`, `lint-format.yml`, `contract-diff.yml`, `verify-context-fingerprint.yml`.

### 4.4 Out of scope (Phase 1E)

- Bicep / Terraform for Azure Container Apps, Postgres Flexible, Meilisearch VM, Key Vault, Blob, Front Door, DNS, TLS.
- Deployment pipeline from GHCR → Container Apps.
- Staging-to-production promotion policy.
- Blue/green or canary strategy.

---

## §5. Seed Framework

### 5.1 Module location

`services/backend_api/Features/Seeding/`

```
Seeding/
├── ISeeder.cs
├── SeedRunner.cs
├── SeedGuard.cs
├── SeedApplied.cs              # EF entity for public.seed_applied
├── SeedingOptions.cs           # bound to Seeding:* config section
├── SeedingCliVerb.cs           # `dotnet run -- seed ...`
├── Datasets/
│   ├── DatasetSize.cs          # small | medium | large
│   └── BogusLocales.cs         # ar, en, en_US ...
└── Seeders/
    ├── _004_IdentitySeeder.cs
    ├── _005_CatalogSeeder.cs
    ├── _006_SearchSeeder.cs
    ├── _007_PricingSeeder.cs
    └── _008_InventorySeeder.cs
```

Each `_00X_*Seeder.cs` file lands in the PR that implements its owning spec, not in this retrofit PR.

### 5.2 `ISeeder` contract

```csharp
public interface ISeeder
{
    string Name { get; }                  // stable identifier: "catalog-v1"
    int Version { get; }                  // bump when semantics change
    IReadOnlyList<string> DependsOn { get; }
    Task ApplyAsync(SeedContext ctx, CancellationToken ct);
}

public sealed record SeedContext(
    AppDbContext Db,
    IServiceProvider Services,
    DatasetSize Size,
    IHostEnvironment Env,
    ILogger Logger);
```

### 5.3 `SeedRunner` — idempotency mechanism

```sql
-- shipped in this retrofit PR as migration 20260420_SeedAppliedTable.cs
CREATE TABLE public.seed_applied (
    id              TEXT PRIMARY KEY,            -- ULID
    seeder_name     TEXT NOT NULL,
    seeder_version  INT  NOT NULL,
    checksum        TEXT NOT NULL,
    environment     TEXT NOT NULL,
    applied_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (seeder_name, seeder_version, environment)
);
```

Runner algorithm:
1. `SeedGuard.EnsureSafe(env, cfg)` — throws in Production.
2. Topological sort of registered seeders by `DependsOn`.
3. For each seeder:
   - Compute `checksum = SHA256(seeder.Name + seeder.Version + datasetSize)`.
   - If a row exists in `seed_applied` with that `(name, version, environment)` and matching checksum → **skip** (idempotent).
   - Otherwise open a transaction, call `ApplyAsync`, insert `seed_applied` row, commit.
4. Emit `seeding.applied` audit event via MediatR to the existing audit writer (Principle 25).

### 5.4 Invocation modes

| Mode | Command | Allowed environments | Behaviour |
|---|---|---|---|
| `apply` | `dotnet run -- seed --mode=apply` | Development, Staging | Idempotent top-up. Safe to run repeatedly. |
| `fresh` | `dotnet run -- seed --mode=fresh` | Development only | Truncates all seeder-owned tables, then `apply`. Refuses outside Development. |
| `dry-run` | `dotnet run -- seed --mode=dry-run` | all | Prints seeder plan + checksum without writing. |

### 5.5 Per-spec seeder inventory (ships with the owning spec)

| Seeder | Owning spec | Contents | Dataset sizes |
|---|---|---|---|
| `_004_IdentitySeeder` | 004 identity | 1 admin, 2 staff (catalog + ops), 5 customer accounts, 2 professional accounts (1 verified + 1 pending), roles: `admin`, `catalog.manage`, `inventory.adjust`, `inventory.reserve.manage`, `finance.view` | same size fixed |
| `_005_CatalogSeeder` | 005 catalog | 8 categories (bilingual, tree depth ≤ 3), 5 brands, 60 / 200 / 800 products (small/medium/large), 120 / 400 / 1600 variants, media placeholders (SVG), 15% restricted | small/medium/large |
| `_006_SearchSeeder` | 006 search | Triggers `ISearchIndexer.ReindexAllAsync()`; publishes `catalog.variant.created` for every seeded variant so `SearchBridge` consumes via MediatR | follows catalog size |
| `_007_PricingSeeder` | 007 pricing | VAT rules (KSA 15%, EG 14%), 3 promotions (1 active, 1 scheduled, 1 expired), 5 coupons (3 active, 2 expired), 2 business pricing tiers, 1 BOGO rule | same size fixed |
| `_008_InventorySeeder` | 008 inventory | Uses pre-seeded 2 warehouses + 7 reason codes from `V008_002`. Adds: ~120/400/1600 snapshot rows with varied state (70% in_stock, 8% low_stock, 10% out_of_stock, 12% preorder), 30/100/300 batches across varied expiry (60% fresh, 25% near-expiry ≤30d, 10% expiring within 7d, 5% already expired for sweeper fixtures), 5 historical movements per seeded variant for ledger replay tests | small/medium/large |

### 5.6 Data quality rules

- Every user-visible text field carries both AR and EN variants, not machine-translated (use curated phrase bank in `Seeding/Datasets/PhraseBank.*.json`).
- Market distribution: 50% KSA, 50% EG unless a seeder's domain dictates otherwise.
- No PII: all emails end in `@example.com`, phone numbers use `+966500000000` / `+201000000000` patterns reserved for documentation.
- Mix of active/inactive: 5% of catalog records are soft-deleted to exercise query filters.

### 5.7 PII guard CI job (new job in `.github/workflows/lint-format.yml`)

```yaml
  seed-pii-guard:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Reject obvious PII in seeders
        run: |
          if grep -rInE '@(gmail|yahoo|outlook|hotmail)\.com' services/backend_api/Features/Seeding/; then
            echo "::error::Real-looking email provider in seeder — use @example.com"; exit 1; fi
          if grep -rInE '\+9665[0-9]{8}|\+201[0-9]{9}' services/backend_api/Features/Seeding/ \
             | grep -vE '\+96650{8}|\+2010{9}'; then
            echo "::error::Non-documentation phone pattern in seeder"; exit 1; fi
```

---

## §6. Search Reindex Hook

### 6.1 Contract

`ISearchIndexer` — already implied by spec 006's service boundary — is formalised in this retrofit so `_006_SearchSeeder` has a stable surface to call.

```csharp
public interface ISearchIndexer
{
    Task IndexVariantAsync(Guid variantId, CancellationToken ct);
    Task ReindexAllAsync(CancellationToken ct);
}
```

### 6.2 Wiring

- `CatalogSeeder` publishes `CatalogVariantCreated` MediatR notifications after each variant insert.
- `SearchBridge` (spec 006 handler) consumes and batches calls to Meilisearch.
- `_006_SearchSeeder` provides an explicit `ReindexAllAsync` path for the case where catalog was seeded before search (e.g., in an older staging snapshot).

### 6.3 Local verification

```bash
curl -H "Authorization: Bearer $MEILI_MASTER_KEY" \
     "http://localhost:7700/indexes/variants/search" \
     -d '{"q":"forceps","limit":5}'
```
Expected: ≥ 1 hit after `scripts/dev/seed.sh` completes.

---

## §7. Documentation & DoD Updates

### 7.1 New docs to create

- `docs/environments.md` — content mirrors §2 (env model, layering, matrix, secrets contract, SeedGuard).
- `docs/local-setup.md` — prerequisites (Docker, .NET 9 SDK), bring-up walkthrough, troubleshooting (port conflicts, stale volumes, Meilisearch master-key mismatches).
- `docs/seed-data.md` — seeder inventory table, invocation modes, dataset sizes, how to add a new seeder.
- `docs/staging-data-policy.md` — PII rules, reset cadence (weekly automated fresh apply on Sundays 02:00 UTC once staging is live), retention (drop after 30 days), forbidden sources (never copy prod snapshots).

### 7.2 Amendments (append-only, no rewrites)

- `docs/implementation-plan.md` — append section **"Amendment A1: Environments, Docker, Seeding"** with 2026-04-20 date, link to this doc, and a one-line note in each affected spec row (004–008) that the spec's PR must include its seeder.
- `docs/dod.md` — add rows:
  - [ ] Dockerfile builds successfully in CI.
  - [ ] `scripts/dev/up.sh` completes in ≤ 90 s warm.
  - [ ] `dotnet ef database update` succeeds against compose Postgres.
  - [ ] `dotnet run -- seed --mode=apply` is idempotent (second run writes no rows).
  - [ ] `appsettings.Staging.json` + `appsettings.Production.json` exist and reference only Key Vault URIs for secrets.
  - [ ] `SeedGuard` test asserts Production invocation exits non-zero.
  - [ ] `seed-pii-guard` CI job green.
- `docs/repo-layout.md` — add lines for `infra/local/`, `scripts/dev/`, `services/backend_api/Features/Seeding/`, `services/backend_api/Dockerfile`.

---

## §8. Sequencing & Rollout

### Step 1 — this PR (docs only)

Creates `docs/missing-env-docker-plan.md` (this file). Zero code changes. Zero migration changes.

### Step 2 — follow-up PR "A1 scaffolding"

Single PR, no spec implementation, no per-spec seeders. Contents:

- `services/backend_api/Dockerfile` + `.dockerignore`
- `infra/local/docker-compose.yml` + `.env.example` + `otel-collector.yaml`
- `scripts/dev/{up,reset,migrate,seed,logs,down}.sh`
- `services/backend_api/appsettings.Staging.json`
- `services/backend_api/appsettings.Production.json`
- `services/backend_api/Configuration/ConfigurationExtensions.cs` (AddLayeredConfiguration + Key Vault binding)
- `services/backend_api/Features/Seeding/` — `ISeeder`, `SeedRunner`, `SeedGuard`, `SeedApplied` entity, `SeedingCliVerb`, `SeedingOptions`, `DatasetSize`, `BogusLocales`, empty `Datasets/` + `Seeders/` folders
- New migration `20260420_SeedAppliedTable.cs`
- `.github/workflows/docker-build.yml`
- `.github/workflows/deploy-staging.yml` (placeholder)
- `seed-pii-guard` job added to `.github/workflows/lint-format.yml`
- Package additions to `backend_api.csproj`:
  - `Meilisearch` (v0.15+)
  - `NodaTime` (v3.2+)
  - `Azure.Extensions.AspNetCore.Configuration.Secrets` + `Azure.Identity`
  - `Bogus` (v35+)
  - `Testcontainers.PostgreSql` (v3.10+, test project only)
- Docs: `environments.md`, `local-setup.md`, `seed-data.md`, `staging-data-policy.md`
- DoD + repo-layout amendments

Acceptance: `scripts/dev/up.sh` green; `dotnet run -- seed --mode=dry-run` lists zero seeders (expected — none registered yet); `ASPNETCORE_ENVIRONMENT=Production dotnet run -- seed` exits code 1.

### Step 3 — per-spec seeder PRs (004 → 005 → 006 → 007 → 008)

Each spec's implementation PR appends one seeder. The spec's `tasks.md` gains a `T0XX Seeder` task pointing at `services/backend_api/Features/Seeding/Seeders/_00X_*Seeder.cs`. Ordering matters because `DependsOn` is enforced:

1. `_004_IdentitySeeder` (no deps)
2. `_005_CatalogSeeder` (depends on identity — needs actor_id for audit)
3. `_006_SearchSeeder` (depends on catalog)
4. `_007_PricingSeeder` (depends on catalog)
5. `_008_InventorySeeder` (depends on catalog)

### Step 4 — Phase 1E (spec 016)

Lifts `appsettings.Staging.json` + `appsettings.Production.json` into Azure Container Apps via Bicep. Provisions Key Vault, Postgres Flexible, Meilisearch VM, Blob, Front Door. Wires `deploy-staging.yml` to the real target. **Out of scope for this retrofit.**

---

## §9. Verification Steps (end-to-end)

Run **after Step 2 merges**:

1. **Local bring-up**
   ```bash
   cp infra/local/.env.example infra/local/.env
   scripts/dev/up.sh
   curl -sf localhost:5000/health/ready
   ```
   Expect `200 OK` within 90 s warm.

2. **Migrations applied**
   ```bash
   docker compose -f infra/local/docker-compose.yml exec postgres \
     psql -U dental -d dental_commerce_dev -c "\dt public.*"
   ```
   Expect `seed_applied`, `audit_log_entries`, `stored_files`.

3. **Seed idempotency**
   ```bash
   scripts/dev/seed.sh apply   # first run: 0 seeders registered yet → OK, exit 0
   scripts/dev/seed.sh apply   # second run: still 0 → no writes, exit 0
   ```

4. **Production block**
   ```bash
   ASPNETCORE_ENVIRONMENT=Production dotnet run --project services/backend_api -- seed --mode=apply
   ```
   Expect exit code 1 and log line `SeedGuard: seeding is hard-blocked in Production`.

5. **Docker image publishes**
   Push a PR; `docker-build.yml` green; image tagged `ghcr.io/<org>/backend-api:<sha>`.

6. **PII guard**
   Introduce `@gmail.com` in a dummy seeder file → `seed-pii-guard` job red → revert.

Run **after each spec-seeder PR merges**:

7. **Search works against seeded data** (after 005 + 006 seeders land):
   ```bash
   curl -H "Authorization: Bearer dev_master_key_not_for_prod" \
        "http://localhost:7700/indexes/variants/search" -d '{"q":"ملقط"}'
   ```
   Expect ≥ 1 hit (Arabic for "forceps").

8. **Inventory states represented** (after 008 seeder lands):
   ```bash
   curl "localhost:5000/inventory/availability?marketCode=ksa&variantIds=..."
   ```
   Expect a mix of `in_stock | low_stock | out_of_stock | preorder` across seeded variants.

---

## §10. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Distroless image missing required native libs | Pick `chiseled-extra` (not `chiseled`); verified to include tzdata + ICU. |
| Bogus locale drift across versions | Pin `Bogus` to exact minor version in `.csproj`; regenerate phrase bank via curated JSON instead of Faker for user-facing strings. |
| Seed runner writes under wrong environment | `SeedGuard` + `seed_applied.environment` row + CI integration test that boots Production env and asserts `seed` verb refuses. |
| Meilisearch data lost on Compose reset | `reset.sh` deliberately deletes volumes; document this loudly; `up.sh` preserves them. |
| Dev mistakenly commits `.env` | Already covered by `.gitignore` (add `infra/local/.env` explicitly in Step 2). |
| Per-spec seeder gets merged out of dependency order | `SeedRunner` topological sort throws on unmet `DependsOn`; CI integration test registers all known seeders and calls `dry-run`. |
| Staging accidentally seeded with production snapshot | `staging-data-policy.md` forbids it; staging Postgres in Phase 1E uses a distinct firewall VNet that cannot reach the prod DB. |

---

## §11. Change Summary

| Change | Kind |
|---|---|
| `docs/missing-env-docker-plan.md` (this file) | new |
| `services/backend_api/Dockerfile` | new (Step 2) |
| `services/backend_api/.dockerignore` | new (Step 2) |
| `infra/local/docker-compose.yml` | new (Step 2) |
| `infra/local/.env.example` | new (Step 2) |
| `infra/local/otel-collector.yaml` | new (Step 2) |
| `scripts/dev/*.sh` | new (Step 2) |
| `services/backend_api/appsettings.Staging.json` | new (Step 2) |
| `services/backend_api/appsettings.Production.json` | new (Step 2) |
| `services/backend_api/Configuration/ConfigurationExtensions.cs` | new (Step 2) |
| `services/backend_api/Features/Seeding/**` (framework only) | new (Step 2) |
| `services/backend_api/Migrations/20260420_SeedAppliedTable.cs` | new (Step 2) |
| `services/backend_api/backend_api.csproj` | amend (Step 2; add packages) |
| `services/backend_api/Program.cs` | amend (Step 2; call `AddLayeredConfiguration`, register seed CLI verb) |
| `.github/workflows/docker-build.yml` | new (Step 2) |
| `.github/workflows/deploy-staging.yml` | new placeholder (Step 2) |
| `.github/workflows/lint-format.yml` | amend (Step 2; add `seed-pii-guard`) |
| `docs/environments.md` / `local-setup.md` / `seed-data.md` / `staging-data-policy.md` | new (Step 2) |
| `docs/implementation-plan.md` | amend — append A1 section |
| `docs/dod.md` | amend — add env/docker/seed rows |
| `docs/repo-layout.md` | amend — add new paths |
| `services/backend_api/Features/Seeding/Seeders/_00X_*Seeder.cs` | new (Step 3; ship with each spec PR) |

---

**End of amendment.** Approval of this doc authorises Step 2. Steps 3–4 ride their own PRs.
