# Implementation Plan: E1 — Infrastructure Integration

**Branch**: `phase-1E` | **Date**: 2026-05-10 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/phase-1E/E1-infrastructure-integration/spec.md`

## Summary

Provision a production-grade Azure runtime in Saudi Arabia Central that hosts the dental commerce backend API, admin web, customer Flutter web build, managed Postgres, and self-hosted Meilisearch — with Key Vaults (Staging + Production) holding all ADR-007/008/009 provider secrets, OIDC-federated GitHub deploy workflows, audit-logged deploy/secret/IaC events, and four alert paths. E1 is infrastructure-only: it provisions the secret-storage contract that 025/026/027 will populate, and provides the Staging stack that spec 029 will load-test. No application features, no UI surfaces, no provider selection.

Technical approach (from research): Bicep IaC decomposed into 16 modules under `infra/azure/`; OIDC federated credentials with subject-claim-pinned access (`repo:<org>/<repo>:environment:<env>` for deploys, `repo:<org>/<repo>:ref:refs/heads/main` for drift); audit emission via a dedicated CLI verb in the existing seed/admin tool (chosen over a managed-identity-bound HTTP endpoint — it reuses the EF context, it works without the backend being up, and it removes a moving part on the deploy critical path); Meilisearch persistence on Azure File (chosen over Azure Disk for cross-AZ flexibility and snapshot simplicity); Postgres with geo-redundant 30-day backup, public access disabled, private endpoint only.

## Technical Context

**Language/Version**: Bicep (latest GA), Bash 5+ for scripts, GitHub Actions YAML, .NET 9 (consumed by audit-emit CLI verb). PostgreSQL 16. Meilisearch latest stable.
**Primary Dependencies**: Azure CLI 2.60+, Bicep CLI 0.27+, `actionlint`, `shellcheck`, `bicep build`, `bicep lint`, `az deployment sub what-if`. Existing Phase 1A `AddLayeredConfiguration()` and seed framework. Spec 003 `audit_log_entries` table.
**Storage**: PostgreSQL 16 Flexible Server (1 instance per environment, 256 GB, geo-redundant backup). Azure File volume for Meilisearch index persistence. Log Analytics workspace for diagnostic logs. Audit-log entries persisted in Postgres (existing spec 003 table — no new application tables in E1).
**Testing**: `bicep build` + `bicep lint` (zero warnings) on PR; `az deployment sub what-if` against a sandbox subscription; `actionlint` on `.github/workflows/**`; `shellcheck` on `scripts/azure/**`; smoke probes (5 bash scripts) executed by deploy workflow; synthetic-failure injection scripts under `scripts/azure/synthetic/` exercise the four alert paths.
**Target Platform**: Azure Container Apps (Saudi Arabia Central). GitHub Actions runners (`ubuntu-latest`). Azure Static Web Apps Standard tier.
**Project Type**: Cross-cutting infrastructure workstream (no `Modules/` slice in `services/backend_api/Modules/`). Adds `infra/azure/`, `scripts/azure/`, three deploy workflows, plus a single CLI verb in the existing admin/seed tool.
**Performance Goals**: SC-1 (60-min clean-clone-to-green), SC-2 (≤ 15-min main-merge-to-Staging-green for 95% of merges), SC-4 (≤ 10-min MTTR rollback), SC-3 (audit emission within 60s of terminal state).
**Constraints**: Single region (KSA Central, ADR-010). No public Postgres ingress. No long-lived Azure secrets in GitHub (OIDC only). No image rebuild in E1's workflows. `seed --mode=apply` Staging-only. Migrations always before app activation. RBAC-only on Key Vaults (no access policies). Soft-delete + purge-protection on vaults. Tags mandatory on every resource.
**Scale/Scope**: 17 Azure resources per environment × 2 environments = 34 resources. 16 Bicep module files + 1 root + 1 keyvault-bootstrap. 3 GitHub workflows. 5 smoke probe scripts. 4 synthetic-failure injection scripts. 1 runbook. 1 decisions log. Zero new application tables.

## Constitution Check

*GATE: Pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | E1 posture | Status |
|---|---|---|
| 5 — Market config (EG + KSA) | Resources tagged `market_codes=sa,eg`. Secret naming has per-market path segment. | PASS |
| 22 — Locked tech | No substitution proposed. Hosts .NET 9 backend, Next.js admin, Flutter web, Postgres 16, Meilisearch. | PASS |
| 23 — Modular monolith | Single backend container, single admin container, single Postgres. No premature service splitting. | PASS |
| 24 — State machines | Deploy lifecycle modeled as explicit 5-state machine; transitions enumerated in spec.md. | PASS |
| 25 — Audit | Every deploy attempt + secret rotation + IaC apply + drift event emits audit-log row with seven required fields. Weekly completeness check. | PASS |
| 28 — AI-build standard | Implementation-ready: Bicep module list, secret taxonomy, RBAC matrix, smoke probes all enumerated. | PASS |
| 29 — Required spec output | spec.md has all 12 sections; this plan satisfies the planning-stage requirement. | PASS |
| ADR-001 — Monorepo | `infra/` folder respected. `.github/workflows/` respected. No new top-level dirs. | PASS |
| ADR-003 — Vertical slice + MediatR | The audit-emit CLI verb lives as a thin adapter in the existing admin/seed tool; no new Modules/ slice. | PASS |
| ADR-004 — EF Core + migrations | Migrations job uses `dotnet ef database update`; one-shot ACA job; runs before revision activation. | PASS |
| ADR-005 — Meilisearch | Self-hosted on ACA per clarify. KSA Central. | PASS |
| ADR-006 — Next.js admin | Admin web hosted on ACA (`ca-admin-web-*`). | PASS |
| ADR-010 — Cloud + residency | All resources in Saudi Arabia Central. Postgres geo-backup is recovery-only (still KSA-bound at rest). | PASS |
| Guardrail #1 — Lint/format | `bicep lint`, `actionlint`, `shellcheck`. CI job `lint-format-infra`. | PASS |
| Guardrail #2 — Contract diff | E1 introduces no application API contracts. The audit-emit CLI verb is internal-only. If any HTTP surface ever ships from this work, it is added to the OpenAPI artifact and contract-diffed. | PASS (vacuous + guarded) |
| Guardrail #3 — Fingerprint | `scripts/compute-fingerprint.sh` runs on every PR; constitution + ADR fingerprint required. | PASS |
| Guardrail #4 — Code-owner approval | `infra/**`, `.github/workflows/deploy-*.yml`, `.github/workflows/infra-drift.yml`, and `services/backend_api/**/AuditEmit*` listed in CODEOWNERS. | PASS |

No principle violations. No gate is unjustified. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/phase-1E/E1-infrastructure-integration/
├── plan.md                            # This file
├── spec.md                            # Feature spec (already authored)
├── research.md                        # Phase 0 output
├── data-model.md                      # Phase 1 output (resource inventory + secret taxonomy + audit schema)
├── quickstart.md                      # Phase 1 output (60-min clean-clone-to-green walkthrough)
├── contracts/
│   └── infrastructure-contract.md     # Operator-facing contract (workflow IO + secret regex + audit schema + RBAC matrix)
├── checklists/
│   └── requirements.md                # Quality checklist (already authored)
└── tasks.md                           # Phase 2 output (created by /speckit-tasks)
```

### Source Code (repository root)

```text
infra/
└── azure/
    ├── main.bicep                       # Subscription-level entry. Parameters: env, locationCode (default ksacentral), marketCodes (default 'sa,eg'), costCenter, ownerAadGroupOid.
    ├── keyvault-bootstrap.bicep         # Provisions placeholder secrets with sentinel values + tags. Run separately from main.bicep.
    ├── parameters/
    │   ├── staging.bicepparam           # env=staging, replica counts, SKUs.
    │   └── production.bicepparam        # env=production, manual approval gate.
    ├── modules/
    │   ├── network.bicep                # VNet + 2 subnets (snet-cae, snet-pg-pe).
    │   ├── postgres.bicep               # Flex server (Postgres 16, Standard_D2s_v3, 256 GB, geo-backup, public-disabled, private endpoint).
    │   ├── aca-environment.bicep        # Container Apps Environment + Log Analytics binding.
    │   ├── aca-app-backend.bicep        # backend_api container app, min 2 / max 10, managed-identity attached, KEY_VAULT_URI env var.
    │   ├── aca-app-admin.bicep          # admin_web container app, min 1 / max 5.
    │   ├── aca-job-migrate.bicep        # One-shot ACA job for `dotnet ef database update`.
    │   ├── meili.bicep                  # Self-hosted Meilisearch container app + Azure File volume mount.
    │   ├── keyvault.bicep               # Both vaults (kv-dental-stg, kv-dental-prd). RBAC, soft-delete, purge-protection.
    │   ├── log-analytics.bicep          # Workspace.
    │   ├── app-insights.bicep           # Workspace-based App Insights.
    │   ├── static-web-app.bicep         # SWA Standard tier for Flutter customer web.
    │   ├── managed-identity.bicep       # User-assigned managed identity per environment.
    │   ├── alerts.bicep                 # 4 alert rules + action group.
    │   └── role-assignments.bicep       # RBAC: identity → vault, GHA federated → ACA + vault, etc.
    ├── DECISIONS.md                     # Locked clarify decisions (Meilisearch self-host on ACA, SWA for Flutter web).
    └── RUNBOOK.md                       # Secret rotation, migrations re-run, rollback, seed cadence, failover deferral, Postgres major upgrade, Meili master-key + reindex.

scripts/
├── azure/
│   ├── setup-federated-credentials.sh   # One-shot bootstrap for 4 GHA federated credentials.
│   ├── smoke/
│   │   ├── 01-health.sh                 # backend /health == 200.
│   │   ├── 02-seed-dryrun.sh            # `seed --mode=dry-run` exits 0 with zero new rows.
│   │   ├── 03-meili-query.sh            # One Meilisearch query returns ≥ 1 result.
│   │   ├── 04-admin-index.sh            # admin_web index returns 200.
│   │   └── 05-flutter-web-index.sh      # SWA index 200 + main.dart.js reachable.
│   └── synthetic/
│       ├── inject-deploy-failure.sh     # Force a workflow failure for AC-14 verification.
│       ├── inject-health-fail.sh        # Cause /health to 5xx for 90s.
│       ├── inject-5xx-spike.sh          # Generate enough 5xx to trip the 1% threshold.
│       └── inject-kv-anomaly.sh         # Cause a non-managed-identity principal read on kv-dental-prd.
└── compute-fingerprint.sh               # (Existing, from spec 001.)

.github/
├── workflows/
│   ├── deploy-staging.yml               # OIDC → pull images → migrations job → activate → seed --mode=apply → smoke → audit emit.
│   ├── deploy-production.yml            # Same but environment=production, --mode=dry-run only, 2-of-N approval gate.
│   ├── infra-drift.yml                  # Cron 0 23 * * * UTC. bicep what-if both envs. Audit + alert on drift.
│   ├── lint-format-infra.yml            # bicep build/lint + actionlint + shellcheck on PRs touching infra/.
│   └── (existing) docker-build.yml      # Phase 1A — produces backend image. NOT modified by E1.
└── CODEOWNERS                           # Adds infra/**, .github/workflows/deploy-*.yml, .github/workflows/infra-drift.yml.

services/backend_api/
└── (existing admin/seed tool)/
    └── AuditEmitCommand.cs              # New CLI verb: `dotnet run -- audit-emit --event-type <type> --payload <json>`.
                                        # Used by deploy workflows to record terminal-state events.
                                        # Implemented as MediatR command in the admin/seed tool slice.
                                        # Reuses existing EF context + audit-log writer from spec 003.

CODEOWNERS additions:
  /infra/                                @platform-eng-team
  /.github/workflows/deploy-*.yml        @platform-eng-team
  /.github/workflows/infra-drift.yml     @platform-eng-team
  /scripts/azure/                        @platform-eng-team
  /services/backend_api/**/AuditEmit*    @platform-eng-team
```

**Structure Decision**: E1 is a cross-cutting workstream, not a vertical slice. It touches `infra/`, `scripts/`, `.github/workflows/`, and adds **one** thin CLI verb in `services/backend_api/` (the audit-emit command). It does NOT add a `Modules/<Name>/` slice because there is no domain entity, no MediatR handler chain beyond the single audit-emit command, and no API surface beyond the operator CLI. This is consistent with how Phase 1A's `docker-build.yml` and Phase 1C-Infra's `admin-docker-build.yml` were structured.

## Phase 0: Outline & Research

(See `research.md` in the same directory for the full Phase 0 output.)

Research areas resolved:

1. **Bicep module decomposition** — 16 modules + 1 root + 1 bootstrap. Justification in research.md §1.
2. **OIDC federated credential subject-claim shape** — environment-scoped for deploys, branch-scoped for drift. Justification in research.md §2.
3. **Meilisearch persistent storage** — Azure File (not Azure Disk) for cross-AZ flexibility, easier snapshots, simpler ACA volume mount. Justification in research.md §3.
4. **Audit-emit transport** — CLI verb in the existing admin/seed tool (NOT an HTTP endpoint). Justification in research.md §4.
5. **Postgres backup strategy** — geo-redundant 30-day backup, point-in-time recovery enabled, no cross-region active workloads (residency-compliant). Justification in research.md §5.

All NEEDS CLARIFICATION resolved (none remain — clarify session locked all decisions).

## Phase 1: Design & Contracts

### Bicep module structure

(See `data-model.md` §1 for the full resource inventory. Brief structural overview here.)

`infra/azure/main.bicep` is the **subscription-level entry**. It calls each module in dependency order:

```
main.bicep
  ├─→ network.bicep              (independent)
  ├─→ log-analytics.bicep        (independent)
  ├─→ app-insights.bicep         (depends on log-analytics)
  ├─→ keyvault.bicep             (independent)
  ├─→ managed-identity.bicep     (independent)
  ├─→ role-assignments.bicep     (depends on keyvault + managed-identity)
  ├─→ postgres.bicep             (depends on network for private endpoint)
  ├─→ aca-environment.bicep      (depends on network + log-analytics)
  ├─→ meili.bicep                (depends on aca-environment + keyvault)
  ├─→ aca-app-backend.bicep      (depends on aca-environment + keyvault + managed-identity + meili + postgres)
  ├─→ aca-app-admin.bicep        (depends on aca-environment + keyvault + managed-identity)
  ├─→ aca-job-migrate.bicep      (depends on aca-environment + keyvault + postgres)
  ├─→ static-web-app.bicep       (independent)
  └─→ alerts.bicep               (depends on app-insights + log-analytics)

keyvault-bootstrap.bicep         (run separately, after main.bicep, populates 12 placeholder secrets)
```

Every module accepts `env`, `locationCode`, `marketCodes`, `costCenter`, `ownerAadGroupOid` as inputs and applies the four mandatory tags via Bicep `tags: union(commonTags, moduleSpecificTags)` pattern.

### Federated identity setup

`scripts/azure/setup-federated-credentials.sh` creates four federated credentials on a User-Assigned Managed Identity per environment, plus one for drift detection per environment:

| Credential | Subject claim | Audience | Used by |
|---|---|---|---|
| `gha-deploy-stg` | `repo:<org>/<repo>:environment:staging` | `api://AzureADTokenExchange` | `deploy-staging.yml` |
| `gha-deploy-prd` | `repo:<org>/<repo>:environment:production` | `api://AzureADTokenExchange` | `deploy-production.yml` |
| `gha-drift-stg` | `repo:<org>/<repo>:ref:refs/heads/main` (job id pinned) | `api://AzureADTokenExchange` | `infra-drift.yml` (Staging job) |
| `gha-drift-prd` | `repo:<org>/<repo>:ref:refs/heads/main` (job id pinned) | `api://AzureADTokenExchange` | `infra-drift.yml` (Production job) |

The script is idempotent (checks for existing credential by name before creating). RBAC for each identity is also created idempotently and scoped narrowly (Staging deploy → Staging RG only; same for Production).

### GitHub Actions workflow shapes

**`deploy-staging.yml`**:

```yaml
name: deploy-staging
on:
  push: { branches: [main] }
  workflow_dispatch:
    inputs:
      image_tag: { type: string, required: false }
      skip_migrations: { type: boolean, default: false }
      skip_seed_apply: { type: boolean, default: false }

permissions:
  id-token: write   # OIDC
  contents: read
  packages: read    # GHCR pull

concurrency:
  group: deploy-staging-${{ github.ref }}
  cancel-in-progress: false  # never cancel an in-flight Staging deploy

jobs:
  deploy:
    environment: staging
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - id: image
        run: |
          echo "tag=${{ inputs.image_tag || github.sha }}" >> "$GITHUB_OUTPUT"
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_DEPLOY_STG_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - name: Audit deploy.attempted
        run: |
          ./scripts/azure/audit-emit.sh \
            --event-type deploy.attempted \
            --commit-sha ${{ github.sha }} \
            --image-tag ${{ steps.image.outputs.tag }} \
            --run-id ${{ github.run_id }} \
            --environment staging
      - name: Run EF migrations job
        if: '!inputs.skip_migrations'
        timeout-minutes: 15
        run: ./scripts/azure/run-migrations-job.sh staging ${{ steps.image.outputs.tag }}
      - name: Activate backend revision
        run: ./scripts/azure/activate-revision.sh staging backend-api ${{ steps.image.outputs.tag }}
      - name: Activate admin revision
        run: ./scripts/azure/activate-revision.sh staging admin-web ${{ steps.image.outputs.tag }}
      - name: Seed (apply mode, Staging only)
        if: '!inputs.skip_seed_apply'
        run: ./scripts/azure/run-seed-job.sh staging apply
      - name: Smoke probes
        run: ./scripts/azure/smoke/run-all.sh staging
      - name: Audit deploy.completed
        if: always()
        run: |
          ./scripts/azure/audit-emit.sh \
            --event-type deploy.completed_${{ job.status == 'success' && 'succeeded' || 'failed' }} \
            --commit-sha ${{ github.sha }} \
            --run-id ${{ github.run_id }} \
            --smoke-results-file /tmp/smoke-results.json \
            --environment staging
```

**`deploy-production.yml`**: same shape but `environment: production` (gates on the GitHub Environments approval list — 2-of-N from `ProductionDeployers`), `workflow_dispatch` only (no `on: push`), and the seed step is hard-coded to `--mode=dry-run`.

**`infra-drift.yml`**: `on: schedule: [{ cron: '0 23 * * *' }]` plus `workflow_dispatch`. Two jobs (one per environment) running `az deployment sub what-if`. A diff parser script (`scripts/azure/parse-whatif.sh`) reduces output to a structured JSON list. If non-empty, emits `infra.drift.detected` audit event and triggers the alert via the action group.

### Smoke probes

Five small bash scripts under `scripts/azure/smoke/`. Each script:
- Takes `<environment>` as a positional argument.
- Has a 30-second hard timeout (configurable via env var).
- Exits 0 on success, non-zero on failure with a clear error message.
- Writes a structured result (JSON) to `/tmp/smoke-results.json` (appended) so the audit-emit step can include all five outcomes.

`run-all.sh` orchestrates them with `set -e` and aggregates the JSON. If any one fails, the whole run exits non-zero and the workflow transitions to `failed`.

### Audit-event emission path

**Decision: dedicated CLI verb in the existing admin/seed tool**, exposed as `dotnet run --project services/backend_api -- audit-emit --event-type <type> --payload <json>`.

Rationale (full discussion in research.md §4):

1. **Reuses spec 003's EF context and audit writer.** Zero new schema. Zero duplicate audit-write code.
2. **Works without the backend being up.** The CLI connects to Postgres directly; if `/health` is failing, audit emission still works. Critical for capturing `deploy.completed_failed` events.
3. **Removes a moving part on the deploy critical path.** No HTTP endpoint to provision, no managed-identity-bound bearer-token issuance, no rate-limiter, no contract surface to drift.
4. **Auditable in itself.** The CLI logs its actor identity (the Azure managed identity object id from `az account show`) into the audit row, satisfying Principle 25.

A wrapper script `scripts/azure/audit-emit.sh` translates workflow-friendly flags into the CLI invocation. The CLI verb lives next to the existing seed CLI in the admin/seed tool, sharing its DI container.

The downstream `scripts/azure/audit-emit.sh` reads the Postgres connection string from Key Vault via the same `id-aca-stg` / `id-aca-prd` managed identity (the deploy workflow re-uses the federated identity to assume the managed identity for this single call — see `scripts/azure/audit-emit.sh` for the `az containerapp exec` invocation pattern that runs the CLI inside an existing backend container, the most network-stable path).

### Drift-detection workflow approach

`infra-drift.yml` runs daily at 02:00 KSA (`cron: '0 23 * * *'` UTC). For each environment:
1. `az login --federated-token` (gha-drift-* identity).
2. `az deployment sub what-if --location ksacentral --template-file infra/azure/main.bicep --parameters infra/azure/parameters/<env>.bicepparam --no-pretty-print > whatif-<env>.json`.
3. `scripts/azure/parse-whatif.sh whatif-<env>.json` reduces the dense Azure JSON to a structured `{ resource_id, change_kind, fields_changed[] }` list.
4. If non-empty: emit `infra.drift.detected` audit event + call action-group webhook for the alert. Save the parsed JSON as a workflow artifact.
5. If empty: emit no audit event (drift-free is the expected steady state). Workflow exits 0 silently.

Auto-remediation is forbidden at v1 (clarify-locked).

### CI gates on `infra/**` and `.github/workflows/deploy-*.yml`

A single workflow `lint-format-infra.yml` runs on PRs whose paths intersect any of:
- `infra/**`
- `.github/workflows/deploy-*.yml`
- `.github/workflows/infra-drift.yml`
- `scripts/azure/**`

Jobs:
1. **`bicep-lint`** — `bicep build infra/azure/main.bicep` and `bicep lint --diagnostics-format=sarif`. Zero errors AND zero warnings (warnings treated as errors at v1).
2. **`actionlint`** — runs against all changed workflows.
3. **`shellcheck`** — runs against all changed `.sh` files under `scripts/azure/`.
4. **`no-client-secret-grep`** — `git grep -nE 'AZURE_CLIENT_SECRET|client-secret\s*[:=]' .github/workflows/ infra/` returns zero matches.
5. **`tag-completeness-check`** — Python script parses Bicep modules; for every `Microsoft.*` resource, validates the four mandatory tags are set. Fails on any missing tag.
6. **`secret-pattern-guard`** — extends the existing spec 003 CI check that scans `appsettings*.json` for secret-shaped values.
7. **`fingerprint`** — runs `scripts/compute-fingerprint.sh` (existing, spec 001) and verifies it matches the constitution + ADR fingerprint.
8. **`bicep-whatif-sandbox`** *(advisory at v1, scheduled to be merge-blocking once a sandbox subscription is provisioned)* — runs `az deployment sub what-if` against a dedicated sandbox subscription and posts the diff as a PR comment.

### Test strategy

| Surface | How tested | Coverage owner |
|---|---|---|
| Bicep IaC | (a) `bicep build` + `bicep lint` on every PR; (b) `bicep what-if` on every PR (sandbox subscription, advisory at v1, merge-blocking when sandbox quota is allocated); (c) full apply on a clean subscription = E1 acceptance test, runs once at the end of E1 implementation. | Platform engineer |
| Deploy workflow | (a) `actionlint` on PR; (b) feature-branch dry-run via `workflow_dispatch` against Staging; (c) post-merge real deploy — Staging is the integration test environment for the deploy workflow. | Platform engineer + reviewer |
| Rollback | Scripted exercise after every deploy-workflow change: deploy version A, deploy version B, rollback to A, smoke pass. Logged under `infra/azure/RUNBOOK.md` exercise log. | On-call engineer (rotating) |
| Smoke probes | (a) `shellcheck` on PR; (b) `bash -n` syntax check; (c) live execution against Staging on every deploy. | Platform engineer |
| Alert firing | Synthetic-failure injection scripts under `scripts/azure/synthetic/`, exercised quarterly + after any alert-rule change. | On-call engineer |
| Audit emission | Database-level test: query `audit_log_entries` after each deploy and verify the seven mandatory fields are populated. Continuously verified by the weekly audit-completeness job. | Backend engineer |
| Drift detection | Synthetic drift (e.g., manual tag change on a non-critical resource) → next drift run must detect it. Quarterly exercise. | Platform engineer |
| Configuration loader fail-closed (AC-17) | One-shot probe revision with `KEY_VAULT_URI` set to invalid URI; assert container fails to start. Run once per E1 acceptance. | Backend engineer |
| Secret rotation propagation (AC-18) | Sentinel-value probe: write a sentinel to a non-production key, wait for the refresh window, query the running app for the value. | Backend engineer |

### Acceptance-criteria-to-implementation phase grouping

For the eventual `tasks.md`, AC mapping is pre-grouped:

| Phase (in tasks.md) | Acceptance criteria covered |
|---|---|
| Phase 1: Provisioning (Bicep + bootstrap) | AC-1, AC-2, AC-3, AC-4, AC-5, AC-22 |
| Phase 2: Deploy workflow + rollback | AC-6, AC-7, AC-8, AC-9, AC-10 |
| Phase 3: Identity & isolation | AC-11, AC-12 |
| Phase 4: Audit & alerting | AC-13, AC-14, AC-15, AC-16 |
| Phase 5: Configuration & secret hygiene | AC-17, AC-18, AC-19 |
| Phase 6: Runbook & operability | AC-20, AC-21 |
| Phase 7: Production parity | AC-23 |
| Phase 8: Hosting decisions made (Meilisearch + SWA) | AC-24, AC-25 |

### Agent context update

Per `update-agent-context.sh claude` invocation: append a new "Active Technologies" entry for E1 noting Azure Container Apps in KSA Central, OIDC federated credentials, self-hosted Meilisearch on ACA, Azure Static Web Apps for Flutter web, Bicep IaC under `infra/azure/`, and the audit-emit CLI verb. Preserve existing entries for spec 003, 004, 007-b, 020, 022, 023, 024.

## Four Guardrails — coverage statement

1. **Lint + format on every PR**: `lint-format-infra.yml` runs `bicep lint`, `actionlint`, `shellcheck`. Red = no merge. Same posture as `dotnet format` for the backend.
2. **Contract diff on every PR**: E1 adds zero new application API surfaces. The audit-emit CLI verb is internal, invoked only by deploy workflows, and not exposed via HTTP. **Vacuous pass.** A grep guard in `lint-format-infra.yml` (`infra-no-http-surface-guard`) verifies that no new `[ApiController]`, `[HttpPost]`, etc., are added under any path matching the audit-emit work — if a future change introduces an HTTP audit endpoint, this guard fails and forces the change through OpenAPI contract diffing.
3. **Constitution + ADR fingerprint**: `scripts/compute-fingerprint.sh` (existing) is invoked by the existing CI fingerprint job; E1 inherits this gate.
4. **Code-owner approval**: CODEOWNERS adds `infra/**`, `.github/workflows/deploy-*.yml`, `.github/workflows/infra-drift.yml`, `scripts/azure/**`, and `services/backend_api/**/AuditEmit*` under `@platform-eng-team`. Constitution + ADR-table edits remain `@product-leadership` per the existing CODEOWNERS contract.

## Cross-spec dependencies

Upstream consumers (E1 reads from):
- **Spec 003** (`shared-foundations`): `audit_log_entries` table; `AddLayeredConfiguration()` precedence; environment guards on the seed CLI.
- **Phase 1A** (`docker-build.yml`): `ghcr.io/<org>/backend-api:<sha>` images.
- **Phase 1C-Infra** (`admin-docker-build.yml`): `ghcr.io/<org>/admin-web:<sha>` images.
- **Phase 1C** (`apps/customer_flutter/web` build pipeline): static bundle artifact for SWA hosting.

Downstream consumers (E1 outputs to):
- **025 (notifications)**: secret slots `notifications-{email,sms,push}/<market>/<provider>/<key>`.
- **026 (shipping)**: secret slots `shipping/<market>/<provider>/<key>`.
- **027 (payments-integration)**: secret slots `payments/<market>/<provider>/<key>`.
- **029 (qa-and-hardening)**: Staging stack for k6 load tests; Production stack for `seed --mode=dry-run` smoke + `/health` probes.

## Risks and mitigations

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| OIDC trust setup typo locks out deploys | Low | High | `scripts/azure/setup-federated-credentials.sh` is idempotent + `--dry-run` flag prints the exact subject claim that will be used; runbook lists break-glass via PIM-elevated AAD group. |
| Sandbox subscription quota exhaustion blocks PR what-if check | Medium | Medium | What-if check is **advisory** at v1 (PR comment, not blocking); promoted to blocking when sandbox quota is allocated. Spec 029's launch-readiness checklist tracks this promotion. |
| Self-hosted Meilisearch index loss on container churn | Medium | High | Azure File volume mount; backup snapshot daily; runbook documents rebuild from authoritative product table (~20 min for launch catalog size). |
| Postgres geo-backup restore tested only in tabletop | Low | High | Quarterly restore-to-sandbox drill scheduled; documented in runbook §Disaster Recovery. |
| Audit-emit CLI fails because backend container not yet started on first-ever deploy | Low | Medium | Bootstrap deploy uses `--skip-audit-emit` flag (recorded explicitly in the runbook). After the first successful deploy, the flag is dropped on every subsequent deploy. |

## Phase 2 — readiness for `/speckit-tasks`

This plan is `/speckit-tasks`-ready. Eight phase groups are pre-defined, every AC is tagged to a phase, every artifact path is named, and every external dependency is enumerated.

## Phase 3+: Future implementation

Out of scope for this plan. Implementation tasks land in `tasks.md`. Implementation execution lands during the Phase 1E coding sprint. Spec 029 picks up E1's Staging stack for load testing.
