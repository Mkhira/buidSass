# Feature Specification: E1 — Infrastructure Integration

**Feature Branch**: `phase-1E`
**Spec ID**: E1 (cross-cutting workstream — non-numeric, runs first in Phase 1E)
**Created**: 2026-05-10
**Status**: Draft
**Phase**: 1E — Integrations · Milestone 8
**Input**: User description: "Provision the Azure runtime that specs 025 (notifications), 026 (shipping), 027 (payments-integration) depend on. Stand up Azure Container Apps environment in KSA Central with managed Postgres Flexible Server, Meilisearch, Key Vaults (Staging + Production) holding all ADR-007/008/009 provider secrets, Log Analytics + App Insights, OIDC-federated GitHub deploy workflow, and Flutter-web hosting decision."

---

## Clarifications

### Session 2026-05-10

The following decisions were locked during `/speckit-clarify`. Each row records the source of the decision: `user` (explicit user reply within the 1-minute window) or `default` (orchestrator-applied recommended default from this spec's "Open Items" / "Assumptions" sections per the agreed workflow).

- Q: Meilisearch HA — Meilisearch Cloud vs self-hosted on ACA → A: **Self-hosted on ACA** in KSA Central with persistent storage (Azure File volume mount). Source: `default`. Rationale: keeps the entire data plane inside KSA Central per ADR-010; removes third-party residency uncertainty; matches modular-monolith posture; index re-creation procedure documented in runbook (master-key rotation path).
- Q: Flutter-web hosting — Azure Static Web Apps vs ACA container → A: **Azure Static Web Apps, Standard tier**, both environments. Source: `default`. Rationale: the implementation plan explicitly recommends SWA for static output (line 581); Flutter web compiles to a static bundle; SSR is not required at v1; ACA-container migration path documented in runbook for future SSR needs.
- Q: Postgres SKU at v1 → A: **`Standard_D2s_v3` (2 vCPU / 8 GiB) with 256 GB storage** in both environments. Source: `default`. Rationale: matches the Assumptions baseline; spec 029 k6 load tests at 5× RPS will validate sizing; runbook documents the resize procedure and downtime characteristics.
- Q: Drift auto-remediation policy → A: **Manual remediation only at v1**. Source: `default`. Rationale: Principle 25 requires human-attributable IaC changes; auto-remediation would obscure accountability; daily drift detection with audit + alert is sufficient.
- Q: Production approver count for the GitHub Environments gate → A: **2-of-N** approvers from the `ProductionDeployers` GitHub team. Source: `default`. Rationale: aligns with Principle 25's accountability posture; prevents single-actor production deploys; consistent with the spec's existing assumption.

The following three open items were deferred (their spec-default values are locked verbatim, not asked, per the orchestrator's "stop after 5 questions" cap):

- Q: Action-group fan-out targets → A: **Email + Microsoft Teams webhook**, extensible to PagerDuty / Opsgenie post-launch. Source: `deferred-default`.
- Q: Secret cache refresh window → A: **5 minutes** in `AddLayeredConfiguration()`. Source: `deferred-default`.
- Q: Postgres major version at v1 → A: **PostgreSQL 16**. Source: `deferred-default`. Rationale: matches existing EF Core migrations from spec 003; v17 upgrade path captured in runbook.

---

## ADR & Constitution Traceability

This spec implements / extends the following constitution principles and ADRs. Every functional requirement below is anchored in at least one of these.

| Source | Title | How E1 satisfies it |
|---|---|---|
| Principle 5 | Market Configuration (EG + KSA) | Per-environment resources are tagged with `market_codes` they serve. Secret naming partitions providers per market. |
| Principle 22 | Fixed Technology Decisions | Stack is locked: .NET 9 backend, Flutter customer, Next.js admin, PostgreSQL. E1 hosts all four, does not propose substitutes. |
| Principle 23 | Architecture (modular monolith) | Single backend container, single admin container, single Postgres instance with logical partitioning. No premature service splitting. |
| Principle 24 | State Machines | Deploy lifecycle is modeled as an explicit five-state machine (`pending → in_progress → smoke_validating → succeeded` ∪ `failed → rolling_back → rolled_back`). |
| Principle 25 | Data & Audit | Every deploy event, secret rotation, IaC apply, drift remediation, and rollback writes an audit-log entry containing actor identity (GitHub Actions run id + commit sha + Azure managed identity object id). Key Vault diagnostic logs are streamed to Log Analytics. |
| Principle 28 | AI-Build Standard | Spec is implementation-ready: explicit Bicep module list, explicit secret naming taxonomy, explicit acceptance probes. |
| Principle 29 | Required Spec Output Standard | All twelve required sections present below. |
| ADR-010 | Cloud & data residency | Azure Saudi Arabia Central, single region, KSA + EG residency satisfied via single-region + `market_code` logical partitioning. |
| ADR-007 | Payment providers | E1 **provisions the secret slots** under documented key names; provider selection happens in spec 027. |
| ADR-008 | Shipping providers | E1 **provisions the secret slots**; provider selection happens in spec 026. |
| ADR-009 | Notification & OTP providers | E1 **provisions the secret slots**; provider selection happens in spec 025. |
| Spec 003 | shared-foundations | E1 consumes `AddLayeredConfiguration()` from spec 003 — Key Vault is the highest-precedence configuration source for app secrets. |
| Phase 1A A1 | layered config + seed framework | E1 runs `seed --mode=apply` in Staging; `seed --mode=dry-run` in Production. |
| Phase 1C-Infra | admin_web Dockerfile + GHCR push | E1 consumes `ghcr.io/<org>/admin-web:<sha>`; does not rebuild. |

E1 does **not** modify the constitution or ADR table. It activates ADR-010's region commitment and creates the secret-storage contract that ADR-007/008/009 will satisfy in 025/026/027.

---

## Goal

Provide a production-grade Azure runtime in Saudi Arabia Central that hosts the dental commerce backend API, admin web, customer Flutter web build, managed Postgres, and Meilisearch — wired through OIDC-authenticated GitHub deploys to Staging on every `main` merge, and gated to Production by manual approval — so that:

1. Specs 025 / 026 / 027 can register their provider secrets into a defined Key Vault contract from day one.
2. Spec 029 (qa-and-hardening) has a real Staging stack to run k6 load tests against at 5× expected launch RPS.
3. Operations (deploys, rollbacks, secret rotations) are auditable, repeatable, and reversible by image tag.
4. Data residency under ADR-010 is satisfied: KSA + EG personal data never leaves the KSA Central region.

E1 is **infrastructure only**. It does not introduce business features, does not select payment / shipping / notification providers, and does not add UI surfaces.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Platform engineer provisions Staging from clean Azure tenant (Priority: P1)

A platform engineer with subscription Owner role applies the Bicep IaC against a clean Azure subscription. After a single `az deployment sub create` (or equivalent automation), the Staging environment is fully provisioned: Resource Group `rg-dental-stg-ksa`, Container Apps Environment, Postgres Flexible Server (private endpoint, no public ingress), Meilisearch, two Key Vaults (`kv-dental-stg`, `kv-dental-prd`), Log Analytics workspace, App Insights, and a managed identity attached to the ACA environment with documented RBAC against both vaults.

**Why this priority**: Without a working IaC apply, nothing else in Phase 1E can ship. This is the foundation for 025/026/027 and for spec 029's load tests.

**Independent Test**: Run the IaC apply command against a fresh subscription. Verify (a) all eleven resource types listed in `Data Model` exist, (b) `az resource list -g rg-dental-stg-ksa` returns the expected count tagged with `environment=staging` and `market_codes=eg,sa`, (c) the ACA managed identity has `Key Vault Secrets User` role on `kv-dental-stg` only (not `kv-dental-prd`), and (d) Postgres has no public IP and the firewall rule list is empty.

**Acceptance Scenarios**:

1. **Given** a clean Azure subscription with Owner permissions, **When** the IaC apply command runs, **Then** the apply completes within 25 minutes and reports zero errors, and every resource is tagged with `environment`, `market_codes`, `cost_center`, and `owner`.
2. **Given** the Staging stack is provisioned, **When** an operator queries `az containerapp env show -n cae-dental-stg-ksa`, **Then** the environment is in `Succeeded` state and is bound to the Log Analytics workspace.
3. **Given** Postgres Flexible Server is provisioned, **When** an operator attempts to connect from a public IP, **Then** the connection fails because no public ingress exists; the only path is the private endpoint inside the VNet.
4. **Given** the ACA managed identity exists, **When** the identity attempts to read a secret from `kv-dental-stg`, **Then** the read succeeds; **When** it attempts to read from `kv-dental-prd`, **Then** the read fails with a 403 Forbidden response.

---

### User Story 2 — Engineer merges a PR to `main` and Staging auto-deploys (Priority: P1)

A backend engineer merges a PR to `main`. The Phase 1A workflow `docker-build.yml` builds and pushes `ghcr.io/<org>/backend-api:<sha>` to GHCR. The Phase 1C-Infra workflow does the same for `admin-web`. Then `deploy-staging.yml` triggers automatically: it authenticates to Azure via OIDC federated credentials (no client secret in GitHub), pulls both images by sha, deploys them to the Staging container apps, runs the EF Core migrations as a one-shot ACA job, runs `seed --mode=apply` (Staging only), and runs the post-deploy smoke probe set.

**Why this priority**: The deploy workflow is the primary mechanism by which all subsequent Phase 1E and Phase 1F work reaches a running environment. Without it, every spec downstream is blocked.

**Independent Test**: Push a commit that bumps the backend version string. Confirm `deploy-staging.yml` runs end-to-end without manual intervention, the Staging `/health` endpoint reports the new version, and an audit-log entry exists with the GitHub Actions run id, commit sha, and managed identity object id.

**Acceptance Scenarios**:

1. **Given** a successful image push to GHCR for both `backend-api:<sha>` and `admin-web:<sha>`, **When** the `deploy-staging.yml` workflow runs, **Then** it obtains an OIDC token from GitHub and exchanges it for an Azure access token without using any long-lived client secret stored in GitHub.
2. **Given** the deploy workflow has authenticated, **When** it deploys both container apps, **Then** the EF Core migrations job runs **before** the `backend_api` revision is activated and reaches `Succeeded` state; the deploy workflow fails fast if migrations fail.
3. **Given** migrations succeeded, **When** the workflow runs `seed --mode=apply`, **Then** the seed run is idempotent (re-running on a previously seeded environment writes zero new `seed_applied` rows for already-applied seeders).
4. **Given** the deploy completed, **When** the smoke probe set runs, **Then** all five probes (backend `/health` 200, `seed --mode=dry-run` exit 0, Meilisearch query returns ≥1 result, admin index page renders, customer Flutter-web index page renders) pass within 150 seconds (5 probes × 30-second per-probe budget, executed sequentially by `scripts/azure/smoke/run-all.sh`); **If any** probe fails, the workflow transitions the deploy state to `failed` and blocks promotion.
5. **Given** a deploy completes (success or failure), **When** an auditor inspects the audit-log table, **Then** an entry exists with `event_type=deploy.attempted` and `event_type=deploy.completed_<state>` containing `github_run_id`, `commit_sha`, `azure_managed_identity_oid`, `images_deployed[]`, `migrations_applied_count`, `smoke_results[]`, and `correlation_id` (the same `correlation_id` value links the `attempted` and `completed_*` rows).

---

### User Story 3 — Engineer rolls Staging back to a known-good image tag (Priority: P1)

A deploy succeeds but a subsequent smoke probe in spec 029 detects a regression in the new revision. An on-call engineer triggers `deploy-staging.yml` manually with `image_tag=<previous_sha>` to roll the backend container app back to the prior revision.

**Why this priority**: Rollback by image tag is the only safe escape hatch when a deploy goes bad. Without it, a regression on `main` blocks every downstream deploy until a hotfix is built. Required by Section 13 launch-readiness checklist.

**Independent Test**: Deploy version A. Deploy version B. Run the rollback workflow with `image_tag=<sha-of-A>`. Confirm `/health` reports version A within 10 minutes and that no migration runs (rollback is image-only; downward migrations are out of scope at v1).

**Acceptance Scenarios**:

1. **Given** revision B is currently active, **When** an operator runs `deploy-staging.yml` with `image_tag=<sha-of-A>` and `skip_migrations=true`, **Then** the workflow deploys revision A and does not invoke the migrations job.
2. **Given** rollback completed, **When** the smoke probes run against revision A, **Then** all five probes pass.
3. **Given** rollback completed, **When** the audit-log is inspected, **Then** an entry exists with `event_type=deploy.rollback`, `from_sha=<B>`, `to_sha=<A>`, and the operator's GitHub identity captured.

---

### User Story 4 — Operator rotates a Key Vault secret without redeploying (Priority: P2)

An operator rotates a payment provider's webhook signing secret (e.g., the secret backing `payments/<market>/<provider>/webhook-signing-key`). The new secret value is written to the Vault as a new version. The running backend app picks it up on next read (next webhook arrival or next cache refresh, whichever comes first) without requiring a container restart.

**Why this priority**: Manual secret rotation is the daily reality of running Stage 7 integrations. If rotation requires a redeploy, operators will resist rotating, and the platform's security posture degrades.

**Independent Test**: Write a known sentinel value to a non-production secret. Confirm the running backend app's secret-cache reflects the new value within its documented refresh window without restart.

**Acceptance Scenarios**:

1. **Given** a secret has been rotated in `kv-dental-stg`, **When** the configured cache refresh window elapses, **Then** the running backend app reads the new value via `AddLayeredConfiguration()` without a container restart.
2. **Given** a secret has been rotated, **When** an auditor inspects Log Analytics, **Then** a `SecretVersionAdded` diagnostic log entry exists with the secret name, the new version id, the actor (operator AAD object id or service principal), and the timestamp.
3. **Given** the runbook documents the rotation cadence (default: 90 days for payment / shipping / notification provider secrets; 30 days for any administrative-only secret), **When** an audit reviews the last rotation timestamps, **Then** no secret has been unrotated for longer than 1.5× its documented cadence.

---

### User Story 5 — Spec 029 hardening engineer runs k6 load tests against Staging (Priority: P2)

The Phase 1F qa-and-hardening engineer points k6 at the Staging endpoints (catalog, search, checkout) at 5× expected launch RPS. The Staging stack absorbs the load with documented scaling behavior.

**Why this priority**: Spec 029 explicitly requires "k6 load tests on Staging at 5× expected launch RPS". This is the integration point E1 must satisfy.

**Independent Test**: Run a 30-minute k6 catalog-list scenario at 5× RPS. Confirm p95 latency stays below the spec-029 budget and that the ACA replicas scale within bounds without OOM or 5xx spikes.

**Acceptance Scenarios**:

1. **Given** the Staging stack is provisioned with the documented scaling profile, **When** k6 ramps to 5× RPS for 30 minutes, **Then** the backend container app scales between the configured min/max replicas (default `min=2`, `max=10` at v1) without manual intervention.
2. **Given** a load test runs, **When** it completes, **Then** App Insights metrics show p95 latency within spec 029's budget for the catalog and search routes.
3. **Given** a synthetic 5xx spike occurs during load, **When** five minutes elapse, **Then** the high-5xx alert fires (see User Story 6).

---

### User Story 6 — On-call engineer is paged when a deploy fails or health degrades (Priority: P2)

The platform alerts on four conditions: deploy-failure, health-probe failure, sustained high 5xx, and Key Vault access anomalies. Each alert routes to the on-call channel with enough context to begin diagnosis without opening a debugger.

**Why this priority**: Quiet failures on integrations cause silent revenue loss. Phase 1E is the moment to wire alert paths so 025/026/027 inherit them.

**Independent Test**: Inject a synthetic failure for each of the four conditions and confirm the alert fires within its SLA, includes runbook link, includes the relevant resource id, and includes the GitHub run id (for deploy alerts) or the managed identity object id (for Key Vault anomalies).

**Acceptance Scenarios**:

1. **Given** a `deploy-staging.yml` run fails at any state, **When** the workflow exits non-zero, **Then** the deploy-failure alert fires within 2 minutes carrying the run id, the failing state, and the runbook link.
2. **Given** the backend `/health` probe returns non-200 for three consecutive checks (default check interval: 30 seconds), **When** the third failure is recorded, **Then** the health-probe alert fires with the resource id and the most recent probe response.
3. **Given** the rolling 5xx rate over the last 5 minutes exceeds 1% of total requests, **When** the threshold is crossed, **Then** the high-5xx alert fires with the route partition.
4. **Given** any principal **other than** the documented ACA managed identity reads or writes a secret in `kv-dental-prd`, **When** the access happens, **Then** the Key Vault access anomaly alert fires within 5 minutes with the principal object id.

---

### User Story 7 — Auditor reviews 90 days of deploy and secret-rotation history (Priority: P3)

An auditor (internal or external) requests the 90-day deploy and secret-rotation history for both Staging and Production. The platform produces a report with deploy event ids, actor identities, image shas, migration counts, smoke-probe outcomes, and secret-rotation timestamps with versions, all derived from the audit-log table and Log Analytics.

**Why this priority**: Required for SOC-2 readiness and for the launch-readiness checklist (Section 13). Lower priority than P1/P2 because it is a reporting concern, not a path-blocking operational concern.

**Independent Test**: Run the report query over a known date range. Confirm every deploy and every secret rotation in that window appears with complete fields.

**Acceptance Scenarios**:

1. **Given** 90 days of audit history exist, **When** the auditor runs the report query, **Then** every deploy attempt (success and failure) is listed with the seven mandatory fields (`event_type`, `github_run_id`, `commit_sha`, `azure_managed_identity_oid`, `images_deployed[]`, `migrations_applied_count`, `smoke_results[]`).
2. **Given** a secret was rotated, **When** the report is run, **Then** the rotation appears with secret name, old version id, new version id, actor object id, and timestamp.
3. **Given** the audit retention policy requires ≥ 365 days for deploy events, **When** the auditor queries data older than 90 days but newer than 365, **Then** the data is still present.

---

### Edge Cases

- **OIDC token issuance fails** during deploy. The workflow MUST exit non-zero with an explicit error message naming the federated credential subject claim that failed, and MUST NOT fall back to a long-lived client secret.
- **GHCR image is unreachable** at deploy time (e.g., GHCR outage or image was garbage-collected). The deploy workflow MUST fail fast with a clear error, MUST NOT degrade to a previous tag automatically (operator must explicitly trigger rollback), and MUST emit an audit-log `deploy.failed` event with `failure_reason=image_unreachable`.
- **EF Core migration job hangs** beyond a 15-minute hard timeout. The job MUST be terminated by the workflow, the deploy state MUST transition to `failed`, the new revision MUST NOT be activated, and the active revision MUST remain the prior one.
- **`seed --mode=apply` runs in Production by accident**. The seed CLI MUST refuse with a non-zero exit and a clear error referencing spec 003's environment guard. The deploy workflow MUST also branch on environment and never invoke `--mode=apply` in Production.
- **Postgres Flexible Server backup window collides with deploy window**. Deploys MUST tolerate Postgres being in backup state by retrying migration job startup with bounded backoff (3 attempts, 60s/120s/240s).
- **Key Vault throttles at deploy time** (Azure throttling is real). The configuration loader MUST retry with exponential backoff, MUST cache successfully-fetched values for the documented refresh window, and MUST NOT crash the container on a transient throttle.
- **Two deploys race** (e.g., merge train pushes two commits within seconds). The deploy workflow MUST serialize per-environment via GitHub Actions concurrency groups (`concurrency: deploy-staging-${{ ref }}`); newer queued runs cancel older queued (but not in-flight) ones.
- **IaC drift** is detected by a scheduled drift-detection job (daily). On drift, the job MUST emit an audit event and an alert; it MUST NOT auto-remediate at v1 (manual remediation only).
- **Flutter-web build artifacts grow** beyond Azure Static Web Apps' free-tier limit. Hosting tier MUST be sized for the expected artifact size (assumption: < 250 MB compressed at launch; standard tier sized for headroom).
- **Cross-region failover requested**. Out of scope at v1. ADR-010 commits to single-region. Spec MUST refuse cross-region work and reference ADR-010 for the deferral.
- **Production deploy is triggered before manual approval is granted**. The Production workflow MUST require the GitHub Environments approval gate; absence of approval MUST block the deploy at the workflow level, not at the Azure level.

---

## User Roles

| Role | Responsibilities | Permissions on E1 resources |
|---|---|---|
| **Platform Engineer** | Authors and applies Bicep IaC, runs initial provisioning, owns the runbook. | Owner on `rg-dental-stg-ksa` and `rg-dental-prd-ksa`. RBAC `Key Vault Administrator` on both vaults. |
| **Backend / Admin Engineer** | Merges PRs that trigger Staging deploys; reads Staging logs for diagnostics. | No direct Azure RBAC. Triggers deploys only via GitHub Actions. Can read App Insights traces. |
| **On-Call Engineer** | Receives alerts; triggers rollbacks; rotates secrets in incident response. | RBAC `Container Apps Contributor` (revision activation) and `Key Vault Secrets Officer` (rotation) on both environments, time-boxed via PIM. |
| **Auditor (internal/external)** | Reads audit-log and Log Analytics for compliance reviews. | RBAC `Reader` and `Log Analytics Reader` on both Resource Groups. No data plane access to vaults or DB. |
| **GitHub Actions (federated identity, Staging)** | Pulls images from GHCR, deploys to Staging, runs migrations + seed apply, runs smoke probes. | RBAC `Container Apps Contributor` on `rg-dental-stg-ksa`, `Key Vault Secrets User` on `kv-dental-stg`, no access to `kv-dental-prd`. |
| **GitHub Actions (federated identity, Production)** | Same as Staging but production-scoped, gated by manual approval. | RBAC `Container Apps Contributor` on `rg-dental-prd-ksa`, `Key Vault Secrets User` on `kv-dental-prd`, no access to `kv-dental-stg`. |
| **ACA Managed Identity (per environment)** | Runtime identity used by `backend_api` and `admin_web` to read secrets at runtime. | `Key Vault Secrets User` on its own environment's vault only. No cross-environment access. |

---

## Business Rules

1. **BR-1 — Single region for personal-data-processing resources.** All Phase 1E resources that **process or store personal data** (Postgres Flexible Server, Container Apps Environment hosting `ca-backend-api-*` and `ca-admin-web-*`, ACA migrations job, Meilisearch container app, Key Vaults, Log Analytics, App Insights, Action Groups, alert rules, managed identities, network resources, role-assignment objects) MUST be provisioned in `Saudi Arabia Central` (ADR-010). The lone documented exception is the Azure Static Web App hosting the Flutter customer web bundle (`swa-customer-flutter-*`), which Azure Static Web Apps does not currently offer in KSA Central; this resource hosts **only the compiled static bundle** (no PII at rest, no personal-data processing) and is provisioned in `westeurope` with content served globally via Azure CDN. The exception is recorded in `infra/azure/DECISIONS.md` per AC-24 with explicit residency-clearance rationale. **No other Azure region may be used for any other resource.**
2. **BR-2 — No public Postgres.** Postgres Flexible Server MUST have public network access disabled. The only path to the database is the private endpoint inside the VNet. Firewall rule lists MUST be empty.
3. **BR-3 — No long-lived Azure secrets in GitHub.** GitHub Actions MUST authenticate to Azure via OIDC federated credentials only. No `AZURE_CLIENT_SECRET` or equivalent long-lived value may exist in GitHub Actions secrets.
4. **BR-4 — `seed --mode=apply` is Staging-only.** The deploy workflow MUST branch on environment. In Production it MUST run `seed --mode=dry-run` only (which exits 0 and writes zero `seed_applied` rows per spec 029 acceptance).
5. **BR-5 — Migrations run before app activation.** EF Core migrations run as a one-shot ACA job that MUST complete `Succeeded` before the new `backend_api` revision is activated. If migrations fail, the new revision MUST NOT be activated.
6. **BR-6 — Secret naming taxonomy is locked.** All ADR-007/008/009 secrets MUST follow the naming taxonomy in the Data Model section. Drift requires an amendment to this spec, not an ad-hoc rename.
7. **BR-7 — Vault isolation.** The Staging managed identity MUST NOT have access to `kv-dental-prd`, and vice versa. Cross-environment secret reads are forbidden.
8. **BR-8 — Configuration precedence honors spec 003.** App secrets MUST be consumed via `AddLayeredConfiguration()` with Key Vault as the highest-precedence source. `appsettings.json` MUST contain only non-secret keys. Any secret in `appsettings.json` is a CI-blocking violation.
9. **BR-9 — Audit every deploy and rotation.** Every deploy attempt (start, success, failure, rollback) and every secret rotation (write of a new version) MUST emit an audit-log entry with the seven mandatory fields (see Data Model — Audit Event Schema).
10. **BR-10 — Image promotion only.** E1's deploy workflow MUST NOT build images. It only pulls from GHCR by sha. Image build remains the responsibility of `docker-build.yml` (Phase 1A) and `admin-docker-build.yml` (Phase 1C-Infra).
11. **BR-11 — Resource tagging.** Every Azure resource MUST carry the four tags `environment` (`staging` | `production`), `market_codes` (`sa,eg`), `cost_center` (`dental-platform`), and `owner` (team email or AAD group object id). Unmistakeable at the IaC level via Bicep parameter defaults.
12. **BR-12 — Production deploy requires manual approval.** The Production deploy workflow MUST use a GitHub Environments approval gate; the absence of approval blocks the workflow before any Azure call.
13. **BR-13 — Drift detection daily.** A scheduled job MUST run `bicep what-if` against both environments daily and emit an audit + alert on any drift. Auto-remediation is forbidden at v1.
14. **BR-14 — Hard-delete forbidden on Key Vaults.** Both vaults MUST have soft-delete and purge-protection enabled with a minimum 90-day retention.
15. **BR-15 — RBAC, not access policies.** Both vaults MUST use Azure RBAC for authorization, not legacy access policies.

---

## User Flow

### Flow 1 — Initial Staging provisioning (one-time per environment)

```
Platform Engineer
  → opens infra/azure/main.bicep
  → runs `az deployment sub create --location ksacentral --template-file main.bicep --parameters env=staging`
  → IaC apply provisions: rg, vnet, cae, postgres-flex, meilisearch (self-hosted on ACA), kv-stg, log-analytics, app-insights
  → IaC apply assigns RBAC: ACA managed identity → kv-stg:Secrets User
  → IaC apply emits audit event `infra.iac.applied`
  → Platform Engineer manually populates ADR-007/008/009 secret slots with placeholder sentinel values (real values arrive in 025/026/027)
  → Platform Engineer triggers a smoke run of `deploy-staging.yml` against the most recent backend image to confirm wiring
  → Smoke succeeds → environment is ready for downstream specs
```

### Flow 2 — Standard Staging deploy (every `main` merge)

```
Engineer merges PR to main
  → docker-build.yml builds backend image, pushes ghcr.io/<org>/backend-api:<sha>
  → admin-docker-build.yml builds admin image, pushes ghcr.io/<org>/admin-web:<sha>
  → deploy-staging.yml triggers on main push event
  → state: pending
  → workflow obtains GitHub OIDC token
  → workflow exchanges OIDC token for Azure access token via federated credential
  → state: in_progress
  → workflow pulls both images by sha
  → workflow runs migrations job (one-shot ACA job) — waits for Succeeded
  → workflow activates new backend_api revision
  → workflow activates new admin_web revision
  → workflow runs `seed --mode=apply` (Staging only, idempotent)
  → state: smoke_validating
  → workflow runs five smoke probes (health, dry-run seed, meili query, admin index, customer Flutter-web index)
  → all pass → state: succeeded → audit event `deploy.completed_succeeded`
  → any fail → state: failed → audit event `deploy.completed_failed` → alert fires
```

### Flow 3 — Production deploy (manual gate)

```
Engineer requests Production promotion of <sha>
  → deploy-production.yml triggers via workflow_dispatch with image_tag=<sha>
  → GitHub Environments approval gate → waits for approver from ProductionDeployers team
  → approver approves
  → state: pending → in_progress
  → workflow runs migrations job
  → workflow activates revisions
  → workflow runs `seed --mode=dry-run` (Production NEVER --mode=apply)
  → state: smoke_validating → smoke probes
  → all pass → state: succeeded
  → audit event `deploy.completed_succeeded` with environment=production
```

### Flow 4 — Rollback

```
On-Call triggers deploy-staging.yml with image_tag=<previous_sha>, skip_migrations=true
  → workflow obtains OIDC token
  → workflow pulls previous_sha image
  → workflow skips migrations job (rollback is image-only at v1; downward migrations are out of scope)
  → workflow activates previous revision
  → smoke probes run
  → state: succeeded
  → audit event `deploy.rollback` with from_sha + to_sha
```

### Flow 5 — Secret rotation

```
Operator (PIM-elevated to Key Vault Secrets Officer) writes new version of secret <name>
  → Key Vault diagnostic log records SecretVersionAdded
  → Log Analytics ingests event
  → backend_api configuration cache (refresh window: 5 minutes default) picks up new version on next read
  → no container restart
  → audit event `secret.rotated` with secret_name, new_version_id, actor_oid
```

### Flow 6 — Drift detection (daily)

```
Scheduled GitHub Actions workflow runs at 02:00 KSA
  → bicep what-if against rg-dental-stg-ksa
  → bicep what-if against rg-dental-prd-ksa
  → drift detected → audit event `infra.drift.detected` with resource ids → alert fires
  → no auto-remediation
```

---

## Operator Workflow States

E1 is a backend/infrastructure spec — there is no end-user UI. The corresponding "states" are the deploy lifecycle (Principle 24) and the operator-facing GitHub Actions UI states.

**Deploy state machine** (per workflow run, persisted in audit-log):

```
pending → in_progress → smoke_validating → succeeded
   |          |                |
   |          |                +→ failed → rolling_back → rolled_back
   |          +→ failed → rolling_back → rolled_back
   +→ failed (auth or image-fetch failure; no rollback needed)
```

**Valid transitions**:

| From | To | Trigger | Actor |
|---|---|---|---|
| (none) | `pending` | workflow run starts | GitHub Actions |
| `pending` | `in_progress` | OIDC token obtained, Azure auth succeeded | GitHub Actions |
| `pending` | `failed` | OIDC failure or GHCR pull failure | GitHub Actions |
| `in_progress` | `smoke_validating` | migrations + revision activation succeeded | GitHub Actions |
| `in_progress` | `failed` | migrations or activation failed | GitHub Actions |
| `smoke_validating` | `succeeded` | all 5 smoke probes pass | GitHub Actions |
| `smoke_validating` | `failed` | any smoke probe fails | GitHub Actions |
| `failed` | `rolling_back` | on-call triggers rollback workflow | On-Call |
| `rolling_back` | `rolled_back` | rollback smoke succeeds | GitHub Actions |
| `rolling_back` | `failed` | rollback smoke fails (rare; manual escalation) | GitHub Actions |

**Failure handling**: A `failed` state is terminal until an operator triggers `deploy-staging.yml` again (forward-fix) or a rollback workflow (backward-recover).

---

## Data Model

E1 introduces **no application-data tables**. It introduces:

1. An **Azure resource inventory** (the "data" of the IaC).
2. A **secret naming taxonomy** in Key Vault.
3. A **deploy/secret audit-event schema**, written into the existing `audit_log_entries` table from spec 003 (no new table — additive event types only).

### Resource Inventory (per environment, Staging shown; Production identical with `prd` suffix)

| # | Resource Type | Name pattern | Notes |
|---|---|---|---|
| 1 | Resource Group | `rg-dental-stg-ksa` | KSA Central. |
| 2 | Virtual Network | `vnet-dental-stg-ksa` | Single VNet, 2 subnets: `snet-cae` (delegated to ACA), `snet-pg-pe` (private endpoint for Postgres). |
| 3 | Container Apps Environment | `cae-dental-stg-ksa` | Workload profile `Consumption + Dedicated D4`. Bound to Log Analytics workspace. |
| 4 | Container App | `ca-backend-api-stg` | Pulls `ghcr.io/<org>/backend-api:<sha>`. Min 2 / max 10 replicas. Managed identity attached. |
| 5 | Container App | `ca-admin-web-stg` | Pulls `ghcr.io/<org>/admin-web:<sha>`. Min 1 / max 5 replicas. Managed identity attached. |
| 6 | Container Apps Job | `caj-ef-migrate-stg` | One-shot job. Image is the same `backend-api:<sha>` invoked with `dotnet ef database update`. |
| 7 | Postgres Flexible Server | `pg-dental-stg-ksa` | SKU per Stage 7 sizing (default `Standard_D2s_v3`, 256 GB). Public access disabled. Private endpoint in `snet-pg-pe`. |
| 8 | Postgres Database | `dental` | Created on the flex server. |
| 9 | Meilisearch (self-hosted on ACA, locked in clarify) | `ca-meili-stg` | Self-hosted Meilisearch container app in KSA Central with an Azure File volume for index persistence. Master key stored in `kv-dental-stg` under `meili/master-key`. Single replica at v1 with documented index-rebuild procedure on rotation/restore. |
| 10 | Key Vault | `kv-dental-stg` | RBAC enabled. Soft-delete + purge-protection enabled (90-day minimum). |
| 11 | Key Vault | `kv-dental-prd` | Provisioned at the same time but ACL-isolated from Staging identity. |
| 12 | Log Analytics Workspace | `log-dental-stg-ksa` | Receives ACA logs, Postgres logs, Key Vault diagnostics. |
| 13 | Application Insights | `appi-dental-stg-ksa` | Workspace-based, bound to the Log Analytics workspace. |
| 14 | User-Assigned Managed Identity | `id-aca-stg` | Attached to both Staging container apps. RBAC `Key Vault Secrets User` on `kv-dental-stg`. |
| 15 | Azure Static Web App (locked in clarify, Standard tier) | `swa-customer-flutter-stg` | Hosts the Flutter customer web build (static bundle). Migration path to ACA container (`ca-customer-flutter-stg`) documented in runbook for future SSR needs. |
| 16 | Action Group | `ag-oncall-stg` | Routes alerts to on-call channel + email distribution. |
| 17 | Alert Rules | (4 rules) | deploy-failure, health-probe, high-5xx, kv-access-anomaly. Each linked to `ag-oncall-stg`. |

Production environment mirrors all 17 with `-prd-` and gets its own managed identity.

### Secret Naming Taxonomy

All ADR-007 / 008 / 009 secrets follow the path:

```
<domain>/<market>/<provider>/<key-name>
```

Where:
- `<domain>` ∈ { `payments`, `shipping`, `notifications-email`, `notifications-sms`, `notifications-push` }
- `<market>` ∈ { `sa`, `eg`, `multi` } (`multi` reserved for cross-market aggregators)
- `<provider>` is the lowercase provider slug (e.g., `paymob`, `bosta`, `unifonic`) — populated when 025/026/027 select providers
- `<key-name>` ∈ { `api-key`, `api-secret`, `webhook-signing-key`, `client-id`, `client-secret`, `account-sid`, `auth-token`, `service-account-json` }

**Reserved E1 placeholders** (created at provisioning time with sentinel value `__placeholder_set_by_E1__` and a Vault tag `set_by_spec=E1; expected_real_value_in=025|026|027`). Placeholder slugs use the regex-friendly form `tbd-by-NNN` so the validation regex (V-3) matches both placeholder and post-selection paths uniformly:

```
payments/sa/tbd-by-027/api-key
payments/sa/tbd-by-027/api-secret
payments/sa/tbd-by-027/webhook-signing-key
payments/eg/tbd-by-027/api-key
payments/eg/tbd-by-027/api-secret
payments/eg/tbd-by-027/webhook-signing-key
shipping/sa/tbd-by-026/api-key
shipping/eg/tbd-by-026/api-key
notifications-email/multi/tbd-by-025/api-key
notifications-sms/sa/tbd-by-025/api-key
notifications-sms/eg/tbd-by-025/api-key
notifications-push/multi/tbd-by-025/service-account-json
```

When 025/026/027 select a provider, the `tbd-by-NNN` segment is replaced by the provider slug (e.g., `paymob`); the placeholder secret is deleted and a fresh secret created at the new path, with a `secret.placeholder_replaced` audit event emitted per transition. The path skeleton is locked at E1; the domain set is closed.

**Storage-encoding note.** Azure Key Vault secret names accept only `^[a-zA-Z0-9-]+$`. The on-disk secret name flattens slashes to `--` (e.g., logical `meili/multi/self-hosted/master-key` is stored as `meili--multi--self-hosted--master-key`). Validation regexes apply to the logical path form. See `data-model.md` §2.

**Non-ADR-7/8/9 secrets** also held in the vaults (provisioned by E1):
- `db/connection-string`
- `meili/master-key`
- `app/jwt-signing-key`
- `app/data-protection-key`

### Audit Event Schema (additive event types on `audit_log_entries` from spec 003)

| Event type | Payload (JSONB) — required keys |
|---|---|
| `infra.iac.applied` | `bicep_template_sha`, `actor_oid`, `resource_changes_count`, `environment` |
| `infra.drift.detected` | `resource_ids[]`, `expected_sha`, `actual_sha`, `environment` |
| `deploy.attempted` | `github_run_id`, `commit_sha`, `images[]`, `environment`, `actor_login`, `correlation_id` |
| `deploy.completed_succeeded` | `github_run_id`, `commit_sha`, `azure_managed_identity_oid`, `images_deployed[]`, `migrations_applied_count`, `smoke_results[]`, `environment`, `correlation_id` |
| `deploy.completed_failed` | same fields as `_succeeded` plus `failure_state`, `failure_reason` |
| `deploy.rollback` | `github_run_id`, `from_sha`, `to_sha`, `actor_login`, `environment`, `correlation_id` |
| `secret.rotated` | `vault_name`, `secret_name`, `old_version_id`, `new_version_id`, `actor_oid` |
| `secret.placeholder_replaced` | `vault_name`, `secret_name`, `replaced_by_spec` (one of `025`, `026`, `027`) |

Retention: deploy + drift + rotation events retained ≥ 365 days. Lower-level diagnostic logs in Log Analytics retain ≥ 90 days.

---

## Validation Rules

### V-1 — IaC validation (CI on every PR touching `infra/azure/**`)
- `bicep build` MUST succeed.
- `bicep lint` MUST report zero errors and zero warnings (warnings treated as errors at v1).
- `az deployment sub validate --what-if` MUST run against a sandbox subscription and pass.
- Tag-completeness check: every `Microsoft.*` resource MUST carry the four mandatory tags (BR-11). CI fails if any resource lacks any tag.

### V-2 — Workflow validation
- `deploy-staging.yml` and `deploy-production.yml` MUST `actionlint` clean.
- Both workflows MUST contain `permissions: id-token: write` (OIDC) and MUST NOT reference any GitHub secret matching `*CLIENT_SECRET*` (CI grep guard).
- `concurrency: deploy-staging-${{ github.ref }}` (or production-equivalent) MUST be present.
- Production workflow MUST reference `environment: production` (GitHub Environments approval gate).

### V-3 — Secret-naming validation
- A CI grep guard MUST scan `infra/azure/keyvault-bootstrap.bicep` and any seed scripts to ensure every secret path matches the taxonomy regex `^(payments|shipping|notifications-email|notifications-sms|notifications-push|db|meili|app)/.+`.

### V-4 — Configuration validation (inherits from spec 003)
- `appsettings.json` and `appsettings.<env>.json` MUST NOT contain any value matching the secret-pattern guards from spec 003. CI rejects PRs that introduce one.
- The backend MUST refuse to start if `AddLayeredConfiguration()` cannot reach the configured Key Vault — fail-closed.

### V-5 — Smoke-probe validation
- Each of the five smoke probes MUST have a documented timeout (default 30s) and exit-code contract.
- A failing probe MUST set the deploy state to `failed` and emit the alert.

### V-6 — Audit completeness
- A weekly job MUST verify that for every GitHub Actions workflow run on `deploy-*.yml` in the past week, an audit-log entry exists. Zero coverage gap allowed.

### V-7 — Tag enforcement at runtime
- A daily Azure Policy compliance scan MUST flag any resource missing tags. Compliance < 100% triggers a P3 alert.

---

## API / Service Requirements

E1 exposes no new application APIs. It does expose **operator surfaces**:

### S-1 — `deploy-staging.yml` workflow inputs/outputs

**Inputs** (workflow_dispatch):
- `image_tag` (string, optional) — defaults to the sha of the current `main` HEAD. When provided, deploys that specific tag (used for rollback).
- `skip_migrations` (boolean, optional) — defaults to `false`. When `true`, the migrations job is skipped (rollback path).
- `skip_seed_apply` (boolean, optional) — defaults to `false`. When `true`, the `seed --mode=apply` step is skipped.

**Outputs**:
- `deploy_state` ∈ `{ succeeded, failed, rolled_back }`
- `smoke_results` (JSON)
- `audit_event_id` (the id of the `deploy.completed_*` audit-log row)

### S-2 — `deploy-production.yml` workflow

Same shape as S-1 but:
- Requires GitHub Environments `production` approval gate.
- `--mode=apply` is hard-coded OFF — `--mode=dry-run` only.
- Default trigger is `workflow_dispatch` only (NOT push). No auto-deploy.

### S-3 — Drift-detection workflow (`infra-drift.yml`)

**Trigger**: scheduled cron `0 23 * * *` UTC (≈ 02:00 KSA).
**Inputs**: none (runs on both environments).
**Outputs**: drift report artifact + audit event + alert (if drift detected).

### S-4 — Key Vault access policies (RBAC, not access policies)

| Role assignment | Scope | Principal |
|---|---|---|
| `Key Vault Secrets User` | `kv-dental-stg` | `id-aca-stg` (managed identity) |
| `Key Vault Secrets User` | `kv-dental-stg` | `gha-deploy-stg` (federated identity for GitHub Actions Staging) |
| `Key Vault Secrets Officer` | `kv-dental-stg` | `aad-group-platform-engineers` (PIM-elevated only) |
| `Key Vault Administrator` | `kv-dental-stg` | `aad-group-platform-engineers` (PIM-elevated only, break-glass) |
| Same four lines | `kv-dental-prd` | Production-scoped principals |

No principal MAY be assigned a permanent (non-PIM) `Officer` or `Administrator` role at v1.

### S-5 — Configuration loader contract (consumed, not authored, by E1)

E1 does not modify `AddLayeredConfiguration()` (spec 003). It does require:
- The vault URI MUST be provided to the loader via the environment variable `KEY_VAULT_URI` set on each container app revision.
- The managed identity MUST be the only authentication path; no client secret.
- Secret cache refresh window MUST be ≤ 5 minutes (default) so that rotations propagate without restart.

---

## Edge Cases

(See also "User Scenarios → Edge Cases" above. The following are additional infrastructure-specific cases.)

- **Subscription quota exhaustion** during IaC apply (e.g., regional vCPU quota). The IaC MUST fail fast with a clear error; the runbook MUST list quota-increase procedures.
- **Bicep template version skew** between Staging and Production (e.g., a Bicep template is updated but only Staging is re-applied). Drift detection (BR-13) catches this within 24 hours and alerts.
- **GHCR organization rename**. The image references in deploy workflows MUST live in a single environment-variable indirection (`GHCR_ORG_NAME`) so a rename requires one PR, not 17.
- **Azure regional outage in KSA Central**. Out of scope at v1. ADR-010 commits to single-region. Recovery procedure: wait for Azure restoration; restore from Postgres geo-backup if data corruption is confirmed (geo-redundant backup MUST be enabled on Postgres flex server even though active workloads are single-region).
- **Bicep `what-if` false-positive on managed-identity tag updates**. The drift workflow MUST exclude noise-prone fields documented in the runbook.
- **Container app cold-start during k6 ramp** in spec 029. Min replicas MUST be ≥ 2 in Staging at v1 to absorb the ramp without warm-up latency skewing the test.
- **Flutter-web index page renders but its bundle fetches 404 a vendored asset**. The smoke probe MUST verify a 200 on at least one of the page's vendored assets (e.g., `main.dart.js`), not just the index HTML.
- **Customer Flutter-web is hosted on Static Web Apps but a future feature requires SSR**. SWA does not support SSR. The runbook MUST document the migration path to ACA container hosting (image already exists; only DNS + CDN swap required).
- **Meilisearch master key leakage**. Key MUST be in the Vault and rotated on incident; the runbook MUST document index re-creation if a rotation forces re-index.
- **Postgres flex server major-version upgrade**. Out of scope at v1. The runbook MUST document the two-step path (logical replica + cutover) for when v17 → v18 arrives.

---

## Acceptance Criteria

Every criterion below MUST pass before E1 is considered at exit and 025/026/027 unblocked.

### AC — Provisioning

- **AC-1**: A clean Azure subscription, given `az deployment sub create --location ksacentral --template-file infra/azure/main.bicep --parameters env=staging`, completes successfully within 25 minutes and exits zero.
- **AC-2**: All 17 resources in the inventory exist for Staging, all carrying the four mandatory tags. Verified by `az resource list -g rg-dental-stg-ksa --query "[].{name:name,tags:tags}"` matching the expected manifest.
- **AC-3**: Postgres Flexible Server has `publicNetworkAccess=Disabled` and zero firewall rules. Verified by `az postgres flexible-server show`.
- **AC-4**: Both Key Vaults have soft-delete + purge-protection enabled with retention ≥ 90 days, and use Azure RBAC (`enableRbacAuthorization=true`, no access policies).
- **AC-5**: All twelve ADR-007/008/009 placeholder secret slots exist in `kv-dental-stg` and `kv-dental-prd` with sentinel value and tag `set_by_spec=E1`.

### AC — Deploy workflow

- **AC-6**: Pushing a commit to `main` triggers `deploy-staging.yml` automatically. The workflow obtains an OIDC token; no `AZURE_CLIENT_SECRET`-style value is referenced anywhere in the workflow file or the repo. CI grep guard passes.
- **AC-7**: The deploy workflow runs the EF Core migrations job *before* activating the new backend revision. Verified by inspecting the workflow run and the ACA revision activation timestamp ordering.
- **AC-8**: `seed --mode=apply` runs in Staging only; `seed --mode=dry-run` runs in Production only. Verified by reading both workflow YAMLs.
- **AC-9**: After a successful deploy, all five smoke probes pass: `/health` 200, `seed --mode=dry-run` exits 0 with zero new `seed_applied` rows, one Meilisearch query returns ≥ 1 result, the admin index page renders 200, and the Flutter-web index renders 200 with `main.dart.js` reachable.
- **AC-10**: Rollback by image tag works: triggering `deploy-staging.yml` with `image_tag=<previous_sha>` and `skip_migrations=true` activates the prior revision; `/health` reports the prior version within 10 minutes of trigger.

### AC — Identity & isolation

- **AC-11**: The Staging managed identity (`id-aca-stg`) can read secrets from `kv-dental-stg` and is denied (403) when reading from `kv-dental-prd`. Verified by a runtime probe.
- **AC-12**: No principal has a permanent (non-PIM) `Key Vault Secrets Officer` or `Key Vault Administrator` role assignment at v1.

### AC — Audit & alerting

- **AC-13**: Every deploy attempt (start, success, failure, rollback) writes an audit-log entry with the seven mandatory fields. Verified by a query over the past 14 days returning row count == workflow run count.
- **AC-14**: Each of the four alert conditions fires within its SLA when the synthetic failure for it is injected: deploy-failure < 2 min, health-probe failure < 90s, high-5xx < 5 min, KV access anomaly < 5 min.
- **AC-15**: The Key Vault diagnostic logs stream to Log Analytics; a `SecretVersionAdded` event is observable within 60 seconds of a rotation.
- **AC-16**: Drift detection runs daily and fires the drift alert on a synthetic drift (e.g., manual tag change on a resource).

### AC — Configuration & secret hygiene

- **AC-17**: The backend container app refuses to start if the configured Key Vault URI is unreachable. Verified by setting `KEY_VAULT_URI` to an invalid URI in a one-shot probe revision and observing fail-closed.
- **AC-18**: Rotating a Key Vault secret value propagates to the running backend within the documented refresh window (≤ 5 minutes default) without container restart. Verified with a sentinel-value probe.
- **AC-19**: A CI guard rejects any PR adding a secret-shaped value to `appsettings.json` or `appsettings.*.json`.

### AC — Runbook & operability

- **AC-20**: A runbook exists at `infra/azure/RUNBOOK.md` covering: (a) secret rotation procedure with cadence, (b) re-running migrations, (c) rollback by image tag, (d) seed-dataset refresh cadence, (e) cross-region failover deferral note pointing at ADR-010, (f) Postgres major-version upgrade procedure (two-step), (g) Meilisearch master-key rotation + re-index path.
- **AC-21**: A representative on-call engineer, given only the runbook, completes a dry-run rotation and a dry-run rollback within 30 minutes total.

### AC — Production parity

- **AC-22**: Both environments are provisioned from the same Bicep template with only the `env` parameter differing. Diff between the two `what-if` outputs (modulo names and sizes) shows zero structural differences.
- **AC-23**: The Production deploy workflow blocks at the GitHub Environments approval gate when no approver is configured; verified by attempting a `workflow_dispatch` without approver setup.

### AC — Hosting decisions made

- **AC-24**: Flutter-web is hosted on Azure Static Web Apps (Standard tier) in both environments. The decision and rationale are recorded in `infra/azure/DECISIONS.md`. The runbook documents the future ACA-container migration path for SSR needs.
- **AC-25**: Meilisearch is self-hosted on ACA in KSA Central with an Azure File volume for index persistence in both environments. The decision and rationale are recorded in `infra/azure/DECISIONS.md`. The runbook documents the master-key rotation procedure and the index-rebuild path on rotation/restore.

---

## Success Criteria

### Measurable Outcomes

- **SC-1**: From a clean subscription, an engineer can stand up a fully working Staging environment (IaC apply + first deploy + smoke pass) in under 60 minutes of wall-clock time, from `git clone` to a green `/health`.
- **SC-2**: 95% of `main` merges reach a green Staging deploy within 15 minutes of the merge commit timestamp.
- **SC-3**: 100% of deploy attempts (success or failure) produce a complete audit-log entry within 60 seconds of the workflow's terminal state.
- **SC-4**: Mean time to rollback (MTTR-rollback) is under 10 minutes from the moment the on-call engineer triggers `deploy-staging.yml` with a previous image tag.
- **SC-5**: Zero secrets are stored in `appsettings*.json` across the entire repository as of E1's exit. Verified by the CI guard returning zero matches.
- **SC-6**: Zero alerts fire from a healthy Staging environment in any 24-hour window once the alert thresholds are tuned (false-positive rate ≤ 1%).
- **SC-7**: Drift detection achieves 100% coverage of both environments daily; any human-driven change outside Bicep is detected within 24 hours.
- **SC-8**: An auditor can, in under 15 minutes, produce a 90-day deploy + secret-rotation report by running the documented query.
- **SC-9**: All four guardrails from CLAUDE.md (lint/format, contract diff, fingerprint, code-owner approval) pass on every PR touching `infra/azure/**` or `.github/workflows/deploy-*.yml`.
- **SC-10**: At E1 exit, all twelve ADR-007/008/009 placeholder secret slots are present and ready to be populated by 025/026/027 with zero further IaC changes.

---

## Phase Assignment

**Phase 1E — Integrations · Milestone 8 (E1 cross-cutting workstream)**.
E1 is non-numeric (no spec ID like `025`); it is referred to as `E1` throughout the implementation plan. E1 MUST exit before specs 025, 026, and 027 can begin (each declares `depends-on: E1`).

---

## Dependencies

### Hard dependencies (must be at DoD before E1 starts)

- **A1 — layered config + seed framework** (Phase 1A docker-build / config / seed scaffolding). E1 consumes `AddLayeredConfiguration()` and the `seed --mode=apply|dry-run` CLI verbatim.
- **Spec 003 — shared-foundations**: `audit_log_entries` table; environment guards on the seed CLI; configuration precedence rules.
- **Phase 1A `docker-build.yml`**: produces `ghcr.io/<org>/backend-api:<sha>` on every `main` merge.
- **Phase 1C-Infra `admin-docker-build.yml`**: produces `ghcr.io/<org>/admin-web:<sha>` on every `main` merge.
- **Customer Flutter-web build pipeline** (Phase 1C, `apps/customer_flutter`): produces a static web bundle artifact that E1 hosts.

### Hard scope-confirmation dependencies (do NOT need to be at DoD; only need to have produced confirmed scope)

- 1A / 1B / 1C / 1D scope confirmed so that Postgres SKU sizing, ACA replica counts, and Meilisearch index sizes are defensible. (Per implementation-plan line 575.)

### Downstream consumers (blocked until E1 exits)

- **025 — notifications**: needs `notifications-email/multi/<provider>/api-key` + `notifications-sms/<market>/<provider>/api-key` + `notifications-push/multi/<provider>/service-account-json` slots.
- **026 — shipping**: needs `shipping/<market>/<provider>/api-key` slots.
- **027 — payments-integration**: needs `payments/<market>/<provider>/{api-key,api-secret,webhook-signing-key}` slots.
- **029 — qa-and-hardening**: requires Staging stack for k6 load tests at 5× RPS and Production stack for `seed --mode=dry-run` smoke + `/health` probes.

### Cross-spec contracts

- E1 establishes the secret-naming taxonomy. 025/026/027 register their providers under it; they do NOT introduce new top-level domains in the path. Adding a domain requires an amendment to this spec.
- E1 establishes the deploy-event audit schema. 025/026/027 emit *integration-specific* audit events (e.g., `payments.webhook.received`) — those are owned by their specs; E1 owns only the deploy/secret/IaC events.

---

## Assumptions

The following defaults were chosen because the implementation plan and ADRs did not nail them down at the line level. Any of these may be overridden by `/speckit-clarify`.

- **Postgres SKU**: `Standard_D2s_v3` (2 vCPU / 8 GiB) at v1 with 256 GB storage; resized in Stage 7 if k6 reveals a bottleneck.
- **ACA replica defaults**: `backend_api` min 2 / max 10; `admin_web` min 1 / max 5.
- **Secret cache refresh window**: 5 minutes default in `AddLayeredConfiguration()`.
- **Postgres backup**: geo-redundant backup enabled, 30-day retention. Single-region active workloads per ADR-010; geo-backup is recovery-only.
- **Log Analytics retention**: 90 days for diagnostic logs, 365+ days for audit-log table (Postgres-backed, retained by application policy).
- **Static Web Apps tier (Flutter-web)**: Standard tier (sized for headroom over the assumed < 250 MB compressed bundle).
- **Drift detection cadence**: daily at 02:00 KSA.
- **Rotation cadence**: 90 days for ADR-007/008/009 provider secrets; 30 days for administrative-only secrets; 365 days for `app/jwt-signing-key` (with 30-day overlap window).
- **Action group fan-out**: on-call email + Microsoft Teams webhook (extensible to PagerDuty or Opsgenie if introduced later).
- **Production approval gate**: 2-of-N approvers from the `ProductionDeployers` GitHub team.

---

## Open Items

All eight items originally surfaced for `/speckit-clarify` are now resolved. See the "Clarifications → Session 2026-05-10" section above for the decision log. Five items received explicit recommended-default decisions; three were deferred to their spec defaults under the orchestrator's "stop after 5 questions" cap.

No open items remain blocking `/speckit-plan`.
