# Tasks: E1 — Infrastructure Integration

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/infrastructure-contract.md](./contracts/infrastructure-contract.md)
**Phase**: 1E — Integrations · Milestone 8 (cross-cutting workstream)
**Created**: 2026-05-10

E1 is a cross-cutting infrastructure workstream — not a vertical-slice feature with multiple user stories. It still maps cleanly onto the Spec Kit phase model: Phase 0 is one-time scaffolding; Phases 1–8 mirror plan.md's pre-grouped acceptance-criteria phases; Phase 9 is polish.

Every Acceptance Criterion (AC-1 through AC-25) maps to at least one task. The traceability matrix at the bottom is the canonical lookup. Tasks are sequenced so each phase is independently completable and testable. `[P]` marks tasks that may run in parallel within the same phase (different files, no incomplete-task dependencies).

---

## Phase 0 — Setup & scaffolding (one-time)

Goal: stand up the cross-cutting plumbing (CI, CODEOWNERS, audit-emit CLI verb) so Phases 1–8 can land cleanly.
Independent test: `lint-format-infra.yml` runs successfully on a no-op PR; CODEOWNERS check passes; `dotnet run --project services/backend_api -- audit-emit --help` prints usage.

- [X] T001 [P] Add CODEOWNERS entries for `infra/`, `.github/workflows/deploy-*.yml`, `.github/workflows/infra-drift.yml`, `scripts/azure/`, and `services/backend_api/**/AuditEmit*` under `@platform-eng-team` in `CODEOWNERS`.
- [X] T002 [P] Create `.github/workflows/lint-format-infra.yml` with eight jobs (`bicep-lint`, `actionlint`, `shellcheck`, `no-client-secret-grep`, `tag-completeness-check`, `secret-pattern-guard`, `fingerprint`, `bicep-whatif-sandbox` advisory) per plan.md "CI gates" section. Trigger on PRs touching `infra/**`, `.github/workflows/deploy-*.yml`, `.github/workflows/infra-drift.yml`, `scripts/azure/**`.
- [X] T003 [P] Create `scripts/ci/check-tag-completeness.py` (Python script invoked by `tag-completeness-check` job) that parses every `.bicep` file under `infra/azure/` and verifies each `Microsoft.*` resource carries the four mandatory tags (`environment`, `market_codes`, `cost_center`, `owner`) per data-model.md §1.
- [X] T004 [P] Create `scripts/ci/check-secret-naming.sh` that grep-validates every secret reference in `infra/azure/keyvault-bootstrap.bicep` and `scripts/azure/**/*.sh` against the secret-naming regex from `contracts/infrastructure-contract.md` §4.
- [X] T005 [P] Create `scripts/ci/check-no-client-secret.sh` that fails if any of `.github/workflows/`, `infra/`, or `scripts/` contains a string matching `AZURE_CLIENT_SECRET|client-secret\s*[:=]`.
- [X] T006 [P] Add `infra-no-http-surface-guard` step to `lint-format-infra.yml` that fails if any new `[ApiController]`, `[HttpPost]`, `[HttpGet]`, etc. attribute is added under any path matching `services/backend_api/**/AuditEmit*` (per plan.md Four-Guardrails §"Contract diff").
- [X] T007 Add an `audit-emit` CLI verb to the existing admin/seed tool at `services/backend_api/AdminTool/Commands/AuditEmitCommand.cs`. The verb accepts `--event-type <type>`, `--payload <json>`, `--correlation-id <uuid>`, and writes a row to `audit_log_entries` via the existing `IAuditLogWriter` from spec 003. Captures the executor's Azure managed-identity object id from `IMDS` (`http://169.254.169.254/metadata/identity/oauth2/token`) and includes it as `actor_id`. ≤ 80 LOC. Wired into the existing DI container. **Implementation note**: spec 003 ships `IAuditEventPublisher` (not `IAuditLogWriter` — those are synonymous names for the same contract); E1 uses the existing publisher. IMDS lookup lives in a sibling file (`AuditEmitImdsClient.cs`) so the command itself stays ≤ 80 LOC.
- [X] T008 [P] Add unit tests for `AuditEmitCommand` at `services/backend_api/Tests/AdminTool.Tests/AuditEmitCommandTests.cs` covering: (a) happy path writes one row with seven mandatory fields; (b) unknown `--event-type` is permitted (free-string column); (c) malformed `--payload` JSON exits non-zero; (d) missing `--correlation-id` defaults to a fresh UUID v4.
- [X] T009 [P] Create `scripts/azure/audit-emit.sh` wrapper that translates workflow-friendly flags into a `dotnet AdminTool.dll audit-emit` invocation via `az containerapp exec`. Handles the `--skip-audit-emit` no-op path used during the very first deploy. Exit code mirrors the underlying CLI.
- [X] T010 Add `infra/azure/DECISIONS.md` documenting the five clarify-locked decisions (Meilisearch self-hosted on ACA, Flutter web on SWA, Postgres `Standard_D2s_v3`, manual drift remediation, 2-of-N production approvers) plus the three deferred-default decisions (email + Teams alerts, 5-min secret cache, Postgres 16). Each entry: decision, source, rationale, alternatives considered. Additionally, include a one-line residency-clearance attestation for the SWA `westeurope` exception (per BR-1 carve-out): "The Flutter customer web bundle hosted on `swa-customer-flutter-*` is non-personal compiled static content; KSA PDPL and Egypt Law 151/2020 localization clauses do not apply to this artifact at rest. Approved by `<platform-engineer-handle>` on `<date>`."

**Phase 0 checkpoint**: PR opens against `phase-1E` branch with the workflow + scripts; `lint-format-infra.yml` runs green; `audit-emit --help` works locally with a Postgres dev container.

---

## Phase 1 — Provisioning (Bicep + bootstrap)

Goal: deliver a clean-subscription Bicep apply that provisions all 17 resources for Staging.
Covers: **AC-1, AC-2, AC-3, AC-4, AC-5, AC-22**.
Independent test: `az deployment sub create --location ksacentral --template-file infra/azure/main.bicep --parameters infra/azure/parameters/staging.bicepparam` exits 0; `az resource list -g rg-dental-stg-ksa` returns the expected 17+ resources, all four tags present.

- [X] T011 [P] Create `infra/azure/modules/network.bicep` provisioning `vnet-dental-<env>-ksa` with subnets `snet-cae` (delegated to `Microsoft.App/environments`) and `snet-pg-pe` (no delegation, hosts private endpoint). Address space `10.0.0.0/16`; subnets `10.0.1.0/24` and `10.0.2.0/24`. All four mandatory tags applied.
- [X] T012 [P] Create `infra/azure/modules/log-analytics.bicep` provisioning `log-dental-<env>-ksa` with retention 90 days and SKU `PerGB2018`. All four mandatory tags applied.
- [X] T013 [P] Create `infra/azure/modules/keyvault.bicep` provisioning `kv-dental-stg` and `kv-dental-prd` (both vaults from a single module call to ensure parity), with `enableRbacAuthorization=true`, `enableSoftDelete=true`, `softDeleteRetentionInDays=90`, `enablePurgeProtection=true`. No legacy access policies. All four mandatory tags applied.
- [X] T014 [P] Create `infra/azure/modules/managed-identity.bicep` provisioning user-assigned managed identity `id-aca-<env>` per environment. All four mandatory tags applied.
- [X] T015 [P] Create `infra/azure/modules/static-web-app.bicep` provisioning `swa-customer-flutter-<env>` (SKU `Standard`, location `westeurope` per data-model.md §1 note on SWA regional restriction). All four mandatory tags applied.
- [X] T016 Create `infra/azure/modules/app-insights.bicep` provisioning workspace-based `appi-dental-<env>-ksa` linked to the Log Analytics workspace from T012. Depends on T012.
- [X] T017 Create `infra/azure/modules/postgres.bicep` provisioning Postgres Flexible Server `pg-dental-<env>-ksa` with version 16, SKU `Standard_D2s_v3`, storage 256 GB, `geoRedundantBackup=Enabled`, `backupRetentionDays=30`, `publicNetworkAccess=Disabled`, and a private endpoint into `snet-pg-pe`. Creates database `dental` with charset `UTF8`. Depends on T011 (subnet must exist).
- [X] T018 Create `infra/azure/modules/aca-environment.bicep` provisioning `cae-dental-<env>-ksa` with workload profiles (Consumption + Dedicated D4), VNet integration into `snet-cae`, and Log Analytics binding. Depends on T011 and T012.
- [X] T019 [P] Create `infra/azure/modules/role-assignments.bicep` codifying the RBAC matrix from `contracts/infrastructure-contract.md` §6: `id-aca-<env>` → KV `Secrets User`; `gha-deploy-<env>` (federated) → RG `Container Apps Contributor` + KV `Secrets User`; `gha-drift-<env>` → RG `Reader`; AAD groups (PIM-only) for break-glass. Depends on T013 + T014.
- [X] T020 Create `infra/azure/modules/meili.bicep` provisioning the self-hosted Meilisearch ACA container app `ca-meili-<env>` with image `getmeili/meilisearch:vX.Y` (pin Y at module-level constant), min/max replicas = 1, Azure File volume mount (`vol-meili-<env>` storage account share with 100 GB quota and daily snapshot policy), `MEILI_MASTER_KEY` env var sourced from `kv-dental-<env>` secret `meili/multi/self-hosted/master-key`. Depends on T013 + T018.
- [X] T021 Create `infra/azure/modules/aca-app-backend.bicep` provisioning `ca-backend-api-<env>` with min replicas 2, max replicas 10, image placeholder (replaced at deploy time), managed identity attached (from T014), env var `KEY_VAULT_URI` set to the env's vault URI. All four mandatory tags. Depends on T013 + T014 + T018.
- [X] T022 Create `infra/azure/modules/aca-app-admin.bicep` provisioning `ca-admin-web-<env>` with min replicas 1, max replicas 5, image placeholder, managed identity attached. All four mandatory tags. Depends on T013 + T014 + T018.
- [X] T023 Create `infra/azure/modules/aca-job-migrate.bicep` provisioning the one-shot ACA job `caj-ef-migrate-<env>` with `replicaTimeout=900`, `replicaRetryLimit=0`, manual trigger, image placeholder. Depends on T017 + T018 + T013.
- [X] T024 [P] Create `infra/azure/modules/alerts.bicep` provisioning action group `ag-oncall-<env>` (email + Microsoft Teams webhook per deferred-default decision in DECISIONS.md) and four metric alerts: `alert-deploy-failure-<env>` (workflow run source), `alert-health-probe-<env>` (3 consecutive non-200 in 90s), `alert-high-5xx-<env>` (5xx > 1% over 5 min), `alert-kv-anomaly-<env>` (any read by principal ≠ `id-aca-<env>`). Depends on T016 (App Insights binding for two of the four alerts).
- [X] T026 [P] Create `infra/azure/parameters/staging.bicepparam` with Staging values (env=staging, replica counts per data-model.md §1, Postgres `Standard_D2s_v3`). Authored alongside T011–T024; consumed by T025 + T029. References `infra/azure/main.bicep` (created in T025) for the `using` declaration but the file itself is independent of T025's body.
- [X] T027 [P] Create `infra/azure/parameters/production.bicepparam` mirroring staging but `env=production` and Production-specific overrides (e.g., higher Postgres backup retention if Stage 7 sizing requires). Same dependency note as T026.
- [X] T025 Create `infra/azure/main.bicep` (`targetScope = 'subscription'`) that orchestrates module calls in dependency order per plan.md §"Bicep module structure". Accepts `env`, `locationCode` (default `ksacentral`), `marketCodes` (default `sa,eg`), `costCenter` (default `dental-platform`), `ownerAadGroupOid`. Computes a `commonTags` object and passes it down so every module applies the four mandatory tags identically. Depends on T011–T024 + T026 + T027 (parameter-file shapes must be agreed before main.bicep finalizes parameter signatures).
- [X] T028 Create `infra/azure/keyvault-bootstrap.bicep` (resource-group-scoped) provisioning the 12 ADR-007/008/009 placeholder secrets (using the regex-friendly `tbd-by-NNN` slug form per data-model.md §2 + storage-encoding flatten) and the 4 E1-owned secrets (`db/multi/postgres-flex/connection-string`, `meili/multi/self-hosted/master-key`, `app/multi/backend-api/jwt-signing-key`, `app/multi/backend-api/data-protection-key`) with sentinel value `__placeholder_set_by_E1__` for the 12 placeholders, real values for the 4 E1-owned, and tags `set_by_spec=E1`, `expected_real_value_in=025|026|027` (placeholders only), `rotation_cadence_days=<90|30|365>`. Depends on T013. **Note**: emission of the `secret.placeholder_replaced` audit event when 025/026/027 overwrites a sentinel is owned by those downstream specs, NOT by E1. T028 only writes the placeholders.
- [ ] T029 Verify acceptance: deploy `main.bicep` against a sandbox subscription with `az deployment sub create --location ksacentral --template-file infra/azure/main.bicep --parameters infra/azure/parameters/staging.bicepparam`; capture wall-clock time and confirm ≤ 25 minutes (AC-1). After the apply succeeds, immediately invoke `./scripts/azure/audit-emit.sh --event-type infra.iac.applied --payload "$(jq -n --arg sha "$(git rev-parse HEAD:infra/azure)" --arg actor "$(az ad signed-in-user show --query id -o tsv)" --arg env staging --argjson count <resource-changes-count> '{bicep_template_sha:$sha,actor_oid:$actor,resource_changes_count:$count,environment:$env}')"` to record the apply (Principle 25; addresses the audit-emission gap for Bicep applies). Then run `keyvault-bootstrap.bicep` against the resulting RG. Run `az resource list -g rg-dental-stg-ksa --query 'length([?tags.environment==\`staging\`])'`; assert ≥ 17 (AC-2). Run `az postgres flexible-server show -n pg-dental-stg-ksa --query '{public:publicNetworkAccess,fwRules:length(@)}'`; assert `Disabled` and 0 (AC-3). Run `az keyvault show -n kv-dental-stg --query '{rbac:properties.enableRbacAuthorization,soft:properties.enableSoftDelete,purge:properties.enablePurgeProtection}'`; assert `true,true,true` (AC-4). Query `az keyvault secret list --vault-name kv-dental-stg --query 'length([?contains(tags.set_by_spec, \`E1\`)])'`; assert 16 (AC-5). Run `az deployment sub what-if` against `staging.bicepparam` and `production.bicepparam` and diff modulo names/sizes; assert zero structural differences (AC-22). Confirm an `infra.iac.applied` row now exists in `audit_log_entries` with `payload->>'environment' = 'staging'`.

**Phase 1 checkpoint**: clean-subscription apply succeeds; all five ACs (1, 2, 3, 4, 5) plus AC-22 verifiably pass.

---

## Phase 2 — Deploy workflow + rollback

Goal: a `main`-merge auto-deploys Staging via OIDC; rollback by image tag works.
Covers: **AC-6, AC-7, AC-8, AC-9, AC-10**.
Independent test: pushing a commit to `main` triggers `deploy-staging.yml`; the workflow obtains an OIDC token (no client secret in workflow), runs migrations before activation, emits success audit; rollback workflow with `image_tag=<previous_sha>` and `skip_migrations=true` activates the prior revision and `/health` reports the prior version within 10 minutes.

- [X] T030 [P] Create `scripts/azure/setup-federated-credentials.sh` (idempotent; `--dry-run` flag prints subject claims without creating). Creates four federated credentials: `gha-deploy-stg` (subject `repo:<org>/<repo>:environment:staging`), `gha-deploy-prd` (subject `repo:<org>/<repo>:environment:production`), `gha-drift-stg` (subject `repo:<org>/<repo>:ref:refs/heads/main`), `gha-drift-prd` (same). Audience `api://AzureADTokenExchange`. Each credential attached to a per-environment user-assigned managed identity. Reads required env vars (`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `GITHUB_ORG`, `GITHUB_REPO`, AAD group OIDs).
- [X] T031 [P] Create `scripts/azure/run-migrations-job.sh` that takes `<env>` and `<image_tag>` as positional args, updates the `caj-ef-migrate-<env>` job's image to the given tag, starts the job (`az containerapp job start`), polls for terminal status with a 15-minute hard timeout, and exits 0 on `Succeeded` / non-zero otherwise. Emits a structured log line on each poll.
- [X] T032 [P] Create `scripts/azure/activate-revision.sh` that takes `<env>`, `<app_name>` (`backend-api` or `admin-web`), and `<image_tag>` and updates the named ACA app's image to that tag, suffixed with the sha as the revision-name suffix; awaits `Provisioning Succeeded` then `Active`.
- [X] T033 [P] Create `scripts/azure/run-seed-job.sh` that takes `<env>` and `<mode>` (`apply` or `dry-run`); refuses `apply` if `<env>=production` (defense in depth on top of spec 003's environment guard); runs the seed CLI inside `ca-backend-api-<env>` via `az containerapp exec`; captures stdout into `/tmp/seed-output.<env>.json`; exits non-zero if the CLI exits non-zero.
- [X] T034 Create `.github/workflows/deploy-staging.yml` with: `on: { push: { branches: [main] }, workflow_dispatch: { inputs: { image_tag, skip_migrations, skip_seed_apply, skip_audit_emit } } }`; `permissions: { id-token: write, contents: read, packages: read }`; `concurrency: { group: deploy-staging-${{ github.ref }}, cancel-in-progress: false }`; `environment: staging`; steps per plan.md "GitHub Actions workflow shapes" — OIDC login, `deploy.attempted` audit, migrations job, backend revision activation, admin revision activation, `seed --mode=apply`, smoke probes via `scripts/azure/smoke/run-all.sh`, `deploy.completed_*` audit. Migrations job MUST run before activation (AC-7). Uses `vars.AZURE_DEPLOY_STG_CLIENT_ID`, `vars.AZURE_TENANT_ID`, `vars.AZURE_SUBSCRIPTION_ID`. No GitHub Secrets named `AZURE_CLIENT_SECRET` (AC-6 grep-guarded by T005).
- [X] T035 [P] Create `scripts/azure/smoke/01-health.sh` that verifies `GET /health` on `ca-backend-api-<env>` returns 200 within 30 s; emits structured-JSON line per `contracts/infrastructure-contract.md` §7.
- [X] T036 [P] Create `scripts/azure/smoke/02-seed-dryrun.sh` that runs `seed --mode=dry-run` via `az containerapp exec`; asserts exit 0 and zero new `seed_applied` rows reported in stdout JSON.
- [X] T037 [P] Create `scripts/azure/smoke/03-meili-query.sh` that fetches the master key from KV, queries `/indexes/products/search` with `{"q":"test"}`, asserts ≥ 1 hit.
- [X] T038 [P] Create `scripts/azure/smoke/04-admin-index.sh` that asserts `GET /` on `ca-admin-web-<env>` returns 200.
- [X] T039 [P] Create `scripts/azure/smoke/05-flutter-web-index.sh` that asserts `GET /` AND `GET /main.dart.js` on `swa-customer-flutter-<env>` both return 200.
- [X] T040 [P] Create `scripts/azure/smoke/run-all.sh` orchestrator that runs probes 01–05 with `set -e`, aggregates JSON to `/tmp/smoke-results.json`, exits 0 only if all five pass; exit non-zero with a summary on any failure.
- [ ] T041 Verify acceptance for AC-6: `git grep -nE 'AZURE_CLIENT_SECRET|client-secret\s*[:=]' .github/workflows/` returns zero matches; trigger `deploy-staging.yml` via push and confirm OIDC token obtained (visible in `azure/login@v2` step output).
- [ ] T042 Verify acceptance for AC-7: inspect a real `deploy-staging.yml` run; confirm migrations-job step's `Completed` timestamp precedes the `activate-revision.sh backend-api` step's `Started` timestamp; if migrations fail, confirm the new revision is NOT activated (force a failing migration in a feature branch).
- [ ] T043 Verify acceptance for AC-8: read `deploy-staging.yml` and confirm `--mode=apply` is invoked; read `deploy-production.yml` (created in Phase 7) and confirm `--mode=dry-run` is invoked. Document both as a static check in `infra/azure/RUNBOOK.md`.
- [ ] T044 Verify acceptance for AC-9: after a successful Staging deploy, run all five smoke probes manually and confirm each passes within its 30-second timeout. Capture `/tmp/smoke-results.json` as evidence.
- [ ] T045 Verify acceptance for AC-10: on Staging, deploy revision A; deploy revision B; trigger `deploy-staging.yml` via `workflow_dispatch` with `image_tag=<sha-of-A>` and `skip_migrations=true`; confirm `/health` reports A's version within 10 minutes; confirm migrations job did NOT run.

**Phase 2 checkpoint**: a Staging deploy on every `main` merge is operational, audit-emitted, and rollback-capable.

---

## Phase 3 — Identity & isolation

Goal: vault and RG access boundaries are enforced; no permanent privileged role assignments.
Covers: **AC-11, AC-12**.
Independent test: a runtime probe from `ca-backend-api-stg` reads `kv-dental-stg` (200) and is denied (403) when reading `kv-dental-prd`. `az role assignment list --all` shows zero permanent (non-PIM) `Officer`/`Administrator` assignments.

- [ ] T046 Verify acceptance for AC-11: deploy a one-shot debug revision of `ca-backend-api-stg` that runs `az keyvault secret show --vault-name kv-dental-prd --name <any>` using the attached managed identity; assert 403. Then run the same against `kv-dental-stg`; assert 200. Capture both responses in the runbook's exercise log.
- [ ] T047 Verify acceptance for AC-12: query `az role assignment list --all --query "[?roleDefinitionName=='Key Vault Secrets Officer'||roleDefinitionName=='Key Vault Administrator'] | [?!conditionVersion]"` (PIM eligible assignments report `conditionVersion`; permanent assignments do not). Assert empty list. Document the query and expected output in the runbook.
- [ ] T048 Add a daily Azure Policy compliance scan (via `infra/azure/modules/role-assignments.bicep` or a separate `infra/azure/policies/` module if the role-assignments module would exceed 200 LOC) that fails compliance if any permanent (non-PIM) `Officer`/`Administrator` role assignment exists on either vault.

**Phase 3 checkpoint**: vaults are mutually isolated; no permanent privileged grants.

---

## Phase 4 — Audit & alerting

Goal: every deploy attempt and secret rotation produces a complete audit-log entry; the four alert paths fire on synthetic injection.
Covers: **AC-13, AC-14, AC-15, AC-16**.
Independent test: query `audit_log_entries` for the past 14 days; row count == workflow-run count for `deploy-*.yml`. Inject each of the four synthetic failures; confirm each alert fires within its SLA.

- [ ] T049 [P] Create `scripts/azure/parse-whatif.sh` that takes a raw `az deployment sub what-if --no-pretty-print` JSON file and reduces it to a structured `{ resource_id, change_kind, fields_changed[] }` array per `data-model.md` §4.
- [ ] T050 Create `.github/workflows/infra-drift.yml` with `on: { schedule: [{ cron: '0 23 * * *' }], workflow_dispatch: {} }`; two jobs (`drift-stg`, `drift-prd`) each running `az login --federated-token` (gha-drift-* identity), `az deployment sub what-if`, `parse-whatif.sh`, and `audit-emit.sh` with `event_type=infra.drift.detected` if drift is detected. `if: github.ref == 'refs/heads/main'` guard.
- [ ] T051 [P] Create `scripts/azure/synthetic/inject-deploy-failure.sh` that triggers `deploy-staging.yml` via `workflow_dispatch` with an invalid `image_tag` (e.g., `nonexistent-sha`) and asserts the workflow exits non-zero. Logs `event_type=synthetic.injection` audit row before triggering.
- [ ] T052 [P] Create `scripts/azure/synthetic/inject-health-fail.sh` that deploys a revision known to 5xx on `/health` (a small "always-503" image hosted internally) for 90 seconds, then rolls back.
- [ ] T053 [P] Create `scripts/azure/synthetic/inject-5xx-spike.sh` that runs a small load generator (`hey` or `k6`) against `ca-backend-api-stg` against an endpoint that has been temporarily configured to 5xx, generating > 1% errors over 5 minutes.
- [ ] T054 [P] Create `scripts/azure/synthetic/inject-kv-anomaly.sh` that uses a PIM-elevated test account to read a secret from `kv-dental-prd` (a non-managed-identity principal access).
- [ ] T055 Verify acceptance for AC-13: query `SELECT count(*) FROM audit_log_entries WHERE event_type LIKE 'deploy.%' AND event_timestamp > now() - interval '14 days'` and compare to `gh api /repos/<org>/<repo>/actions/workflows/deploy-staging.yml/runs --paginate` count. Assert equality (or document permitted skew of 0).
- [ ] T056 Verify acceptance for AC-14: run T051; confirm `alert-deploy-failure-stg` fires within 2 minutes (check action group history). Run T052; confirm `alert-health-probe-stg` fires within 90 s. Run T053; confirm `alert-high-5xx-stg` fires within 5 min. Run T054; confirm `alert-kv-anomaly-prd` fires within 5 min. Capture firing timestamps in the runbook exercise log.
- [ ] T057 Verify acceptance for AC-15: rotate a non-production secret in `kv-dental-stg`; query Log Analytics `KeyVaultDataPlaneAuditLogs` for `OperationName=='SecretSet'`; assert event observed within 60 s of rotation.
- [ ] T058 Verify acceptance for AC-16: in Staging, manually mutate a tag on a non-critical resource (e.g., change `cost_center` value); trigger `infra-drift.yml` via `workflow_dispatch`; confirm drift is detected, audit event emitted, alert fired. Restore the tag; confirm the next drift run is empty.
- [ ] T059 Add a weekly audit-completeness verification job to `.github/workflows/lint-format-infra.yml` (or a separate `audit-completeness.yml`) that queries `audit_log_entries` for the past 7 days and asserts every `deploy.attempted` has a paired `deploy.completed_*` within 30 minutes (correlation_id-matched). Failure = P1 incident.

**Phase 4 checkpoint**: audit + alerting paths verified end-to-end on Staging.

---

## Phase 5 — Configuration & secret hygiene

Goal: backend fails closed if KV is unreachable; secret rotations propagate without restart; no secrets in `appsettings*.json`.
Covers: **AC-17, AC-18, AC-19**.
Independent test: deploy a probe revision with invalid `KEY_VAULT_URI`; container fails to start. Rotate a sentinel secret; running container picks up the new value within 5 minutes without restart. CI guard scans `appsettings*.json` for secret-shaped values.

- [ ] T060 Verify acceptance for AC-17: deploy a one-shot revision of `ca-backend-api-stg` with `KEY_VAULT_URI=https://invalid-vault.vault.azure.net/`; observe the container's startup log records a fail-closed exception from `AddLayeredConfiguration()` and the revision never reaches `Active`. Document the exact log line in the runbook.
- [ ] T061 Verify acceptance for AC-18: write a sentinel value (`__sentinel_<timestamp>__`) to a non-production key (e.g., `app/multi/backend-api/test-rotation-key` provisioned for this purpose); add a tiny test endpoint to the running backend that reads this key from `IConfiguration` (or use existing diagnostic endpoint) and returns its current cached value. Wait 5 minutes; query the endpoint; assert the new sentinel value is returned without container restart.
- [ ] T062 Verify acceptance for AC-19: extend the existing spec-003 secret-pattern guard (or add a new `scripts/ci/check-no-secrets-in-appsettings.sh`) that scans every `appsettings*.json` file for: API-key-shaped strings, JWT-shaped strings, base64-encoded blobs > 32 bytes, AAD client secrets. Wire it into `lint-format-infra.yml` as a blocking job. Add at least one positive-test PR (with a fake secret) to confirm the guard rejects it; revert the PR.

**Phase 5 checkpoint**: secret hygiene + fail-closed posture verified.

---

## Phase 6 — Runbook & operability

Goal: an on-call engineer with only the runbook can complete a dry-run rotation and a dry-run rollback in under 30 minutes.
Covers: **AC-20, AC-21**.
Independent test: a representative on-call engineer (rotating shadow) runs a dry-run rotation + dry-run rollback against Staging using only `infra/azure/RUNBOOK.md` as guidance; total wall-clock under 30 minutes.

- [ ] T063 Create `infra/azure/RUNBOOK.md` covering: (a) **Secret rotation procedure** with documented cadences (90 / 30 / 365 days) and step-by-step PIM elevation flow; (b) **Re-running migrations** (manual `az containerapp job start --name caj-ef-migrate-<env>` with image-pin); (c) **Rollback by image tag** (`gh workflow run deploy-staging.yml -f image_tag=<sha> -f skip_migrations=true`); (d) **Seed-dataset refresh cadence** (Staging: monthly; Production: never via `--mode=apply`); (e) **Cross-region failover deferral** with explicit ADR-010 reference; (f) **Postgres major-version upgrade** two-step procedure (logical replica + cutover); (g) **Meilisearch master-key rotation** + index-rebuild path; (h) **Disaster recovery** (geo-redundant backup restore drill, quarterly cadence); (i) **Exercise log** scaffold for AC verifications. Each section ends with a "verified by" line listing the AC ids it covers.
- [ ] T064 Verify acceptance for AC-20: peer-review of `RUNBOOK.md` by a platform engineer not involved in writing it; confirm all seven required sections (a–g per AC-20) plus DR are present and self-contained.
- [ ] T065 Verify acceptance for AC-21: schedule a runbook-only dry-run with a representative on-call engineer; the engineer performs (1) rotate the `meili/multi/self-hosted/master-key` in Staging using only RUNBOOK.md, and (2) rollback Staging to a previous image sha. Time-box at 30 minutes total. Record the timing in the runbook exercise log; if it exceeds 30 min, file a runbook-improvement issue and re-test.

**Phase 6 checkpoint**: runbook is operationally proven under timed dry-run.

---

## Phase 7 — Production parity

Goal: Production deploys go through a 2-of-N approval gate; both environments use the same Bicep with only `env` differing.
Covers: **AC-23**.
Independent test: a `workflow_dispatch` of `deploy-production.yml` blocks at the GitHub Environments approval gate when no approver is configured. `bicep what-if` diff between staging.bicepparam and production.bicepparam shows zero structural differences (already verified in T029 for AC-22).

- [ ] T066 Create `.github/workflows/deploy-production.yml` mirroring `deploy-staging.yml` shape with these overrides: `on: workflow_dispatch` only (no `push`); `environment: production` (with the GitHub Environments approval gate); `concurrency.group: deploy-production-${{ github.ref }}`; seed step hard-coded to `--mode=dry-run` (no `skip_seed_apply` input); uses `vars.AZURE_DEPLOY_PRD_CLIENT_ID`. Identity must NOT have any KV access on `kv-dental-stg`.
- [ ] T067 Configure the GitHub Environments `production` with required reviewers = `ProductionDeployers` team, minimum approvers = 2 (clarify-locked: 2-of-N), and `prevent_self_review=true` so the actor who triggered the workflow cannot count toward their own approvals. Use:
  ```bash
  TEAM_ID=$(gh api "/orgs/$GITHUB_ORG/teams/ProductionDeployers" --jq .id)
  gh api -X PUT "/repos/$GITHUB_ORG/$GITHUB_REPO/environments/production" --input - <<EOF
  {
    "wait_timer": 0,
    "prevent_self_review": true,
    "reviewers": [{ "type": "Team", "id": $TEAM_ID }],
    "deployment_branch_policy": { "protected_branches": true, "custom_branch_policies": false }
  }
  EOF
  # The "minimum approvers = 2" setting is not currently exposable via gh api as a single field;
  # it is set via the UI under Settings → Environments → production → Required reviewers (count = 2).
  # Document the UI step in RUNBOOK.md and add an audit query that validates the current setting via
  # `gh api /repos/$GITHUB_ORG/$GITHUB_REPO/environments/production --jq '.protection_rules'`.
  ```
  Document the configuration AND the audit-query in `infra/azure/RUNBOOK.md` §"Production deploy gate".
- [ ] T068 Verify acceptance for AC-23: trigger `deploy-production.yml` via `workflow_dispatch` from a feature branch with no approvers configured; assert the workflow blocks at the approval gate before any Azure call (the workflow run shows status `Waiting` with no Azure log lines). Add a single approver and re-run; confirm it remains blocked (1-of-2 not satisfied). Add a second approver; confirm it proceeds.

**Phase 7 checkpoint**: Production deploy is gated, parity-verified, and operationally distinct from Staging.

---

## Phase 8 — Hosting decisions made (Meilisearch + SWA)

Goal: the two clarify-locked hosting decisions are implemented and documented.
Covers: **AC-24, AC-25**.
Independent test: `swa-customer-flutter-<env>` is provisioned and serves the Flutter web bundle; `ca-meili-<env>` is provisioned with persistent Azure File volume and master-key sourced from KV. `infra/azure/DECISIONS.md` records both decisions with rationale.

- [ ] T069 Verify acceptance for AC-24: confirm `swa-customer-flutter-stg` and `swa-customer-flutter-prd` exist and serve the Flutter web bundle (`/` returns 200, `/main.dart.js` returns 200 — already smoke-tested in T039). Confirm `infra/azure/DECISIONS.md` (created in T010) records the SWA-vs-ACA rationale and the future ACA-container migration path.
- [ ] T070 Verify acceptance for AC-25: confirm `ca-meili-stg` and `ca-meili-prd` are running with the Azure File volume mount intact across container churn (kill the replica; verify the index persists). Confirm `infra/azure/DECISIONS.md` records the Meilisearch self-host-on-ACA rationale, master-key rotation procedure, and index-rebuild path on rotation/restore. Confirm the runbook §"Meilisearch master-key rotation" (T063 deliverable) exists.

**Phase 8 checkpoint**: both hosting decisions verifiably live in both environments.

---

## Phase 9 — Polish & cross-cutting

Goal: tighten edges, ensure all CI guards are merge-blocking where appropriate, capture acceptance evidence for the next phase (specs 025/026/027 unblock).

- [ ] T071 [P] Promote the advisory `bicep-whatif-sandbox` job in `lint-format-infra.yml` to merge-blocking once a sandbox subscription is allocated (target: end of Phase 1F per spec 029's launch-readiness checklist). Document the promotion in `infra/azure/DECISIONS.md`.
- [ ] T072 [P] Add a daily compliance dashboard query (saved Log Analytics query) showing: (a) deploy success rate over 7 days; (b) median deploy duration; (c) audit-completeness gap count; (d) drift events count; (e) alert fire count by category. Pin to the on-call team's home page.
- [ ] T073 [P] Author a one-page "Phase 1E ready-for-025/026/027" hand-off note (`infra/azure/HANDOFF-FOR-025-026-027.md`) describing: (a) which secret slots to use, (b) the secret-rotation contract for each provider domain, (c) how to consume `AddLayeredConfiguration()` from each module, (d) how to emit integration-specific audit events that ride alongside E1's deploy events.
- [ ] T074 Run the full Quickstart end-to-end against a clean sandbox subscription; time it; assert ≤ 60 minutes (SC-1 verification). Record outcome in the runbook exercise log.
- [ ] T075 Final spec-compliance check: re-read spec.md AC-1..AC-25 and confirm each is verifiably green via the verification tasks. File any gaps as P1 issues before declaring E1 at exit.

**Phase 9 checkpoint**: E1 at exit. Specs 025, 026, 027 are unblocked. Spec 029 has its Staging stack.

---

## Dependencies (phase-level)

```
Phase 0 (setup)
   │
   ▼
Phase 1 (provisioning)  ──────────┐
   │                              │
   ▼                              ▼
Phase 2 (deploy + rollback)   Phase 3 (identity & isolation)
   │                              │
   ├──────────────────────────────┘
   ▼
Phase 4 (audit & alerting)
   │
   ▼
Phase 5 (config & secret hygiene)
   │
   ▼
Phase 6 (runbook + operability)
   │
   ▼
Phase 7 (production parity)
   │
   ▼
Phase 8 (hosting decisions made)
   │
   ▼
Phase 9 (polish)
```

Phase 3 depends only on Phase 1 RBAC outputs and runs in parallel with Phase 2. Phase 4 depends on Phase 2 (workflows must exist to be audited) and Phase 1 (alerts module). Phase 5 depends on Phase 1 (vaults exist) and Phase 2 (deploy workflow runs the seed/probes). Phase 7 depends on Phase 2 (mirrors deploy-staging shape).

## Parallel execution opportunities

- Phase 0: T001–T009 are mostly independent (different files); only T007 must precede T008 (T008 tests T007). T010 is independent.
- Phase 1: T011–T015 are fully parallel (independent Bicep modules). T016 depends on T012. T017 depends on T011. T018 depends on T011 + T012. T019 depends on T013 + T014. T020 depends on T013 + T018. T021–T023 depend on T013 + T014 + T018 (and T017 for T023). T024 depends on T016. T025 depends on T011–T024. T026–T027 are parallel. T028 depends on T013. T029 is verification, depends on the full chain.
- Phase 2: T030 + T031 + T032 + T033 are fully parallel. T034 depends on all four. T035–T039 are parallel. T040 depends on T035–T039.
- Phase 4: T051–T054 are fully parallel.

## Suggested MVP boundary (if cut is required)

If implementation must ship a partial E1 to unblock a single downstream spec urgently:

- **Minimum to unblock 025/026/027**: Phases 0 + 1 + 2 + 3. This delivers a working Staging deploy with vault isolation; placeholder secrets exist; provider selection by 025/026/027 can populate them. Defer Phase 4 (audit/alerts), Phase 5 (config hygiene verifications), and Phases 6–9 to a follow-up.
- **Not safe to defer**: Phase 7 (Production parity) — Production cannot ship without it. Phase 1's AC-22 (parity-by-construction) IS safe to verify only when Production is first applied.

The recommended path is to land all 9 phases sequentially before declaring E1 at exit. The MVP boundary is documented for incident-response purposes only.

---

## Acceptance Criteria → Task ID traceability matrix

| AC | Description | Tasks |
|---|---|---|
| AC-1 | Clean-subscription apply ≤ 25 min | T011, T012, T013, T014, T015, T016, T017, T018, T019, T020, T021, T022, T023, T024, T025, T026, T028, T029 |
| AC-2 | All 17 resources tagged | T011–T024, T025, T029 |
| AC-3 | Postgres no public, no firewall rules | T017, T029 |
| AC-4 | Vaults RBAC + soft-delete + purge-protection | T013, T029 |
| AC-5 | 12 placeholder secret slots present | T028, T029 |
| AC-6 | OIDC, no client secret | T030, T034, T041 |
| AC-7 | Migrations before activation | T031, T032, T034, T042 |
| AC-8 | `apply` Staging-only / `dry-run` Production-only | T033, T034, T066, T043 |
| AC-9 | All 5 smoke probes pass | T035, T036, T037, T038, T039, T040, T044 |
| AC-10 | Rollback by image tag works | T032, T034, T045 |
| AC-11 | Cross-environment vault isolation | T019, T046 |
| AC-12 | No permanent privileged role assignments | T019, T047, T048 |
| AC-13 | Deploy audit completeness | T007, T009, T034, T055, T059 |
| AC-14 | All 4 alerts fire on synthetic injection | T024, T051, T052, T053, T054, T056 |
| AC-15 | KV diagnostic logs stream to Log Analytics | T012, T013, T057 |
| AC-16 | Drift detection daily | T049, T050, T058 |
| AC-17 | Backend fails closed on bad KV URI | T021, T060 |
| AC-18 | Secret rotation propagates without restart | T021, T028, T061 |
| AC-19 | Secret-pattern guard against appsettings.json | T004, T062 |
| AC-20 | Runbook exists with all required sections | T063, T064 |
| AC-21 | On-call engineer dry-run < 30 min | T063, T065 |
| AC-22 | Bicep what-if diff Staging vs Production = zero structural | T025, T026, T027, T029 |
| AC-23 | Production approval gate works | T066, T067, T068 |
| AC-24 | Flutter-web on SWA, decisions logged | T010, T015, T039, T069 |
| AC-25 | Meilisearch self-hosted on ACA, decisions logged | T010, T020, T037, T070 |

Every AC has at least one task; most have several. Phase 9's T075 is the final cross-check.

---

## Format validation

All 75 tasks follow the required checklist format: `- [ ] T### [P?] [Story?] Description with file path`. Story labels are absent because E1 is a cross-cutting workstream (no per-user-story subdivision). Phase labels are encoded in the section headers. File paths are anchored verbatim to plan.md's "Source Code (repository root)" section: 16 Bicep modules under `infra/azure/modules/`, 5 smoke probes under `scripts/azure/smoke/`, 4 synthetic-failure injection scripts under `scripts/azure/synthetic/`, 3 deploy workflows under `.github/workflows/`, 1 lint workflow, plus the audit-emit CLI verb and supporting scripts.

**Total tasks**: 75 across 10 phases.
**Parallelizable**: 38 tasks marked `[P]`.
**Verification tasks**: 25 (one per AC plus a final cross-check in T075).
