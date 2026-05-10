# Contract: E1 — Infrastructure Integration (operator-facing)

**Phase**: 1 (Design — Contracts)
**Date**: 2026-05-10
**Spec**: [../spec.md](../spec.md) · **Plan**: [../plan.md](../plan.md) · **Data model**: [../data-model.md](../data-model.md)

E1 introduces zero **application API** contracts. This document is the **operator-facing** contract: the inputs, outputs, naming taxonomies, schemas, and access-control matrix that downstream specs (025/026/027/029) and operators (deploy / rollback / rotate / audit) rely on.

Treat this contract as the **stable surface**. Breaking changes require a spec amendment.

---

## §1 — `deploy-staging.yml` workflow contract

### Trigger

| Trigger | Condition |
|---|---|
| `push` | branches=[`main`] (auto-deploy on merge) |
| `workflow_dispatch` | manual override (rollback, ad-hoc deploy of a specific sha) |

### Inputs

| Input | Type | Default | Notes |
|---|---|---|---|
| `image_tag` | string | `${{ github.sha }}` (auto-computed for push) | When provided via dispatch, deploys that specific tag (used for rollback). MUST resolve to existing tags in `ghcr.io/<org>/backend-api` AND `ghcr.io/<org>/admin-web`. |
| `skip_migrations` | boolean | `false` | When `true`, the migrations job is skipped (rollback path). |
| `skip_seed_apply` | boolean | `false` | When `true`, the `seed --mode=apply` step is skipped. |
| `skip_audit_emit` | boolean | `false` | Bootstrap-only. Used **only** before the very first successful deploy when no backend container exists yet. Documented in runbook. |

### Outputs (workflow run summary)

| Output | Type | Notes |
|---|---|---|
| `deploy_state` | enum {`succeeded`, `failed`, `rolled_back`} | Terminal state per the deploy state machine. |
| `smoke_results` | JSON array of `{probe, status, duration_ms, message}` | Five entries (one per probe). |
| `audit_event_id` | UUID | Id of the `deploy.completed_*` audit-log row. |
| `correlation_id` | UUID | Links `deploy.attempted` and `deploy.completed_*`. |

### Permissions block (verbatim required)

```yaml
permissions:
  id-token: write     # OIDC
  contents: read
  packages: read      # GHCR pull
```

Any deviation (e.g., `id-token: read`) is a CI-blocking violation.

### Concurrency (verbatim required)

```yaml
concurrency:
  group: deploy-staging-${{ github.ref }}
  cancel-in-progress: false
```

### State transitions emitted as audit events

| Transition | Audit event |
|---|---|
| (none) → `pending` | `deploy.attempted` |
| `pending` → `in_progress` | (none — internal) |
| `in_progress` → `smoke_validating` | (none — internal) |
| `smoke_validating` → `succeeded` | `deploy.completed_succeeded` |
| any → `failed` | `deploy.completed_failed` |
| (rollback path) | `deploy.rollback` |

---

## §2 — `deploy-production.yml` workflow contract

Identical to §1 with the following overrides:

| Field | Override |
|---|---|
| Trigger | `workflow_dispatch` only (no `push`) |
| `environment` | `production` (GitHub Environments approval gate, **2-of-N** from `ProductionDeployers`) |
| `skip_seed_apply` | not exposed; seed step hard-coded to `--mode=dry-run` |
| `concurrency.group` | `deploy-production-${{ github.ref }}` |

The seed CLI MUST refuse `--mode=apply` in `ASPNETCORE_ENVIRONMENT=Production` per spec 003. The workflow's hard-coding is belt-and-suspenders.

---

## §3 — `infra-drift.yml` workflow contract

| Field | Value |
|---|---|
| Trigger | `schedule: '0 23 * * *'` UTC (≈ 02:00 KSA) + `workflow_dispatch` |
| Permissions | `id-token: write`, `contents: read` |
| Outputs | Drift JSON artifact per environment, audit event `infra.drift.detected` if drift detected, alert via action group |
| Auto-remediation | **Forbidden at v1** (clarify-locked) |

Drift JSON shape: see `data-model.md` §4.

---

## §4 — Secret Naming Taxonomy contract

### Path schema

```
<domain>/<market>/<provider>/<key-name>
```

### Validation regex (CI-enforced)

```
^(payments|shipping|notifications-email|notifications-sms|notifications-push|db|meili|app)/(sa|eg|multi)/[a-z0-9-]+/(api-key|api-secret|webhook-signing-key|client-id|client-secret|account-sid|auth-token|service-account-json|connection-string|master-key|jwt-signing-key|data-protection-key)$
```

### Closed sets (extending requires spec amendment)

| Set | Members |
|---|---|
| Domain | `payments`, `shipping`, `notifications-email`, `notifications-sms`, `notifications-push`, `db`, `meili`, `app` |
| Market | `sa`, `eg`, `multi` |
| Key-name | `api-key`, `api-secret`, `webhook-signing-key`, `client-id`, `client-secret`, `account-sid`, `auth-token`, `service-account-json`, `connection-string`, `master-key`, `jwt-signing-key`, `data-protection-key` |

### E1-provisioned placeholder slots (12 ADR-007/008/009 + 4 E1-owned)

Listed in `data-model.md` §2. 025/026/027 OVERWRITE the placeholders by replacing the `tbd-by-NNN` slug in the path with the selected provider slug (the placeholder secret is deleted and a fresh secret created at the new path). Placeholder slugs use the regex-friendly form `tbd-by-NNN` so they pass the V-3 validation regex (CI guard would otherwise reject the placeholders themselves).

**Storage encoding**: Azure Key Vault secret names accept only `^[a-zA-Z0-9-]+$`. The on-disk secret name flattens slashes to `--` (e.g., logical `meili/multi/self-hosted/master-key` is stored as `meili--multi--self-hosted--master-key`). Validation regexes apply to the logical path form. Conversion is mechanical (`s|/|--|g`).

### Per-secret metadata schema

| Tag | Type | Required | Notes |
|---|---|---|---|
| `set_by_spec` | string | Yes | `E1`, `025`, `026`, `027` |
| `expected_real_value_in` | string | Placeholders only | `025` \| `026` \| `027` |
| `rotated_at` | ISO-8601 | Yes (set on rotation) | When most recent version was written |
| `rotation_cadence_days` | integer | Yes | 90, 30, or 365 per `data-model.md` §2 |

### Vault diagnostic-log → audit-event flow

When a `Microsoft.KeyVault.SecretVersionAdded` event lands in Log Analytics:

1. The `kv-rotation-collector` Azure Function (or scheduled Logic App — see runbook) reads the event.
2. It checks the secret's tags:
   - If `set_by_spec=E1` and the new version's value is **not** the sentinel: emit `secret.placeholder_replaced` with `replaced_by_spec` derived from the secret path's `<provider>` segment matching 025/026/027 ownership.
   - Otherwise: emit `secret.rotated`.
3. Also updates the secret's `rotated_at` tag to `now()`.

---

## §5 — Audit Event Schema contract

(Full schema in `data-model.md` §3.)

### Mandatory fields per event type

| Event type | Mandatory `payload` keys |
|---|---|
| `infra.iac.applied` | `bicep_template_sha`, `actor_oid`, `resource_changes_count`, `environment` |
| `infra.drift.detected` | `resource_ids[]`, `expected_sha`, `actual_sha`, `environment`, `parsed_changes_json_url`, `drift_run_id`, `drift_run_url` |
| `deploy.attempted` | `github_run_id`, `commit_sha`, `images[]`, `environment`, `actor_login`, `correlation_id` |
| `deploy.completed_succeeded` | `github_run_id`, `commit_sha`, `azure_managed_identity_oid`, `images_deployed[]`, `migrations_applied_count`, `smoke_results[]`, `environment`, `correlation_id` |
| `deploy.completed_failed` | as `_succeeded` plus `failure_state`, `failure_reason` |
| `deploy.rollback` | `github_run_id`, `from_sha`, `to_sha`, `actor_login`, `environment`, `correlation_id` |
| `secret.rotated` | `vault_name`, `secret_name`, `old_version_id`, `new_version_id`, `actor_oid` |
| `secret.placeholder_replaced` | `vault_name`, `secret_name`, `replaced_by_spec` ∈ {`025`,`026`,`027`}, `actor_oid` |

### Causal invariants

1. Every `deploy.attempted` MUST be paired with exactly one `deploy.completed_*` carrying the same `correlation_id`, within 30 minutes.
2. `deploy.rollback`'s `from_sha` and `to_sha` both MUST reference prior `deploy.completed_succeeded` events (verified at audit-emit time; if either is unverifiable, audit-emit fails closed and the deploy workflow exits non-zero).
3. The weekly audit-completeness job verifies invariant 1 over the past 7 days; gap = P1 incident.

---

## §6 — Key Vault RBAC matrix contract

| Principal | Scope | Role | Notes |
|---|---|---|---|
| `id-aca-stg` (managed identity) | `kv-dental-stg` | Key Vault Secrets User | Runtime read by backend + admin |
| `id-aca-prd` (managed identity) | `kv-dental-prd` | Key Vault Secrets User | Runtime read by backend + admin |
| `gha-deploy-stg` (federated) | `kv-dental-stg` | Key Vault Secrets User | Reads `db/multi/postgres-flex/connection-string` for migrations job |
| `gha-deploy-prd` (federated) | `kv-dental-prd` | Key Vault Secrets User | Same |
| `aad-group-platform-engineers` (PIM) | both vaults | Key Vault Administrator | Break-glass; PIM-elevated only |
| `aad-group-on-call` (PIM, time-boxed) | both vaults | Key Vault Secrets Officer | Rotation path |
| `aad-group-auditors` | both vaults | Reader (data plane: none) | Reads metadata only via control plane |

**Cross-environment isolation invariant**: no Staging principal MAY read from `kv-dental-prd`; no Production principal MAY read from `kv-dental-stg`. Verified by AC-11 at runtime.

**No permanent (non-PIM) `Officer` or `Administrator` role assignments allowed at v1.** Verified by AC-12.

---

## §7 — Smoke probe contract

Each smoke probe is an independent bash script under `scripts/azure/smoke/`. Contract:

| Field | Value |
|---|---|
| Argument | positional `<environment>` (`staging` or `production`) |
| Timeout | 30 s default (override via `SMOKE_TIMEOUT_SECONDS`) |
| Exit codes | 0 = pass, non-zero = fail |
| stdout | structured JSON line: `{"probe":"<name>","status":"pass"\|"fail","duration_ms":<int>,"message":"<text>"}` |
| stderr | human-readable diagnostic on failure |

`scripts/azure/smoke/run-all.sh` orchestrates: runs all five probes, aggregates JSON to `/tmp/smoke-results.json`, exits 0 only if all five pass.

| Probe | Script | Verifies |
|---|---|---|
| 1 | `01-health.sh` | `GET /health` on `ca-backend-api-<env>` returns 200 |
| 2 | `02-seed-dryrun.sh` | `seed --mode=dry-run` exits 0 with zero `seed_applied` rows written |
| 3 | `03-meili-query.sh` | one Meilisearch query against the `products` index returns ≥ 1 hit |
| 4 | `04-admin-index.sh` | `GET /` on `ca-admin-web-<env>` returns 200 |
| 5 | `05-flutter-web-index.sh` | `GET /` on `swa-customer-flutter-<env>` returns 200 AND `GET /main.dart.js` returns 200 |

---

## §8 — Synthetic-failure injection contract

Used quarterly (and after any alert-rule change) to verify AC-14.

| Script | Injects | Verifies | Recovery |
|---|---|---|---|
| `inject-deploy-failure.sh` | Forces `deploy-staging.yml` to fail at the migrations step (e.g., temporarily pointing at a bad migration sha). | `alert-deploy-failure-<env>` fires within 2 minutes with run id + failing state. | Re-run with correct sha. |
| `inject-health-fail.sh` | Sets `ca-backend-api-<env>` revision to a sha known to 5xx on `/health` for 90 seconds. | `alert-health-probe-<env>` fires within 90 seconds. | Re-deploy good sha. |
| `inject-5xx-spike.sh` | Generates synthetic load that triggers > 1% 5xx for 5 minutes. | `alert-high-5xx-<env>` fires within 5 minutes. | Stop the load generator. |
| `inject-kv-anomaly.sh` | Reads a secret using a non-managed-identity principal (e.g., a PIM-elevated test account). | `alert-kv-anomaly-<env>` fires within 5 minutes. | None — anomaly is the test. |

Each script logs its actor identity to `audit_log_entries` with `event_type=synthetic.injection`, payload `{ "purpose": "<probe-name>", "expected_alert": "<alert-id>" }`.

---

## §9 — Versioning policy

This contract is **stable**. Breaking-change rules:

- Adding a new `domain` to the secret naming taxonomy: **spec amendment required** (also amends the validation regex and the closed-set table in `data-model.md` §2).
- Adding a new `event_type` to the audit schema: **non-breaking** (additive). The backend's audit writer accepts arbitrary event_type strings.
- Removing a mandatory payload key: **breaking**, requires spec amendment.
- Adding a mandatory payload key: **breaking** for downstream emitters; requires coordination with the emitter (typically the deploy workflow).

A semver-style version banner lives at the top of this file; bumps follow the rules above.

**Contract version**: 1.0.0 (E1 ratified).

---

## §10 — Cross-references

| Source | Section |
|---|---|
| Spec — Acceptance Criteria | spec.md AC-1 to AC-25 |
| Spec — User Roles | spec.md "User Roles" |
| Spec — Business Rules | spec.md "Business Rules" |
| Plan — Workflow shapes | plan.md "GitHub Actions workflow shapes" |
| Data model — Resource inventory | data-model.md §1 |
| Data model — Secret taxonomy | data-model.md §2 |
| Data model — Audit schema | data-model.md §3 |
| Research — OIDC subject claims | research.md §2 |
| Research — Audit-emit transport | research.md §4 |
| Quickstart — Bootstrap walkthrough | quickstart.md |
