# Data Model: E1 — Infrastructure Integration

**Phase**: 1 (Design)
**Date**: 2026-05-10
**Spec**: [spec.md](./spec.md)

E1 introduces zero new application-data tables. It defines three model surfaces:

1. **Azure Resource Inventory** — the "data" of the IaC.
2. **Key Vault Secret Naming Taxonomy** — the contract 025/026/027 will populate.
3. **Audit Event Schema** — additive event types written into the existing `audit_log_entries` table from spec 003.

This document is the canonical, model-focused statement of those three surfaces. Narrative explanation lives in spec.md and plan.md.

---

## §1 — Azure Resource Inventory

### Common-tag schema (every resource)

| Tag key | Type | Allowed values | Source |
|---|---|---|---|
| `environment` | string | `staging` \| `production` | Bicep parameter `env` |
| `market_codes` | string (csv) | `sa,eg` | Bicep parameter `marketCodes` (default `sa,eg`) |
| `cost_center` | string | `dental-platform` (fixed v1) | Bicep parameter `costCenter` |
| `owner` | string (email or AAD group OID) | team email or `aad-group-platform-engineers` OID | Bicep parameter `ownerAadGroupOid` |

Tag enforcement: every `Microsoft.*` resource MUST carry all four. CI guard `tag-completeness-check` rejects PRs that violate this.

### Resource list (per environment)

`<env>` is `stg` or `prd` throughout.

| # | Bicep module | Azure resource type | Name | Properties |
|---|---|---|---|---|
| 1 | (root) | `Microsoft.Resources/resourceGroups` | `rg-dental-<env>-ksa` | location=ksacentral |
| 2 | network.bicep | `Microsoft.Network/virtualNetworks` | `vnet-dental-<env>-ksa` | addressSpace=10.0.0.0/16 |
| 3 | network.bicep | `Microsoft.Network/virtualNetworks/subnets` | `snet-cae` | 10.0.1.0/24, delegated to `Microsoft.App/environments` |
| 4 | network.bicep | `Microsoft.Network/virtualNetworks/subnets` | `snet-pg-pe` | 10.0.2.0/24, no delegation, hosts private endpoint |
| 5 | aca-environment.bicep | `Microsoft.App/managedEnvironments` | `cae-dental-<env>-ksa` | workloadProfiles=Consumption + D4, vnetConfiguration→snet-cae, daprAIInstrumentationKey=appi |
| 6 | aca-app-backend.bicep | `Microsoft.App/containerApps` | `ca-backend-api-<env>` | image=ghcr.io/<org>/backend-api:<sha>, minReplicas=2, maxReplicas=10, identity=`id-aca-<env>` |
| 7 | aca-app-admin.bicep | `Microsoft.App/containerApps` | `ca-admin-web-<env>` | image=ghcr.io/<org>/admin-web:<sha>, minReplicas=1, maxReplicas=5, identity=`id-aca-<env>` |
| 8 | aca-job-migrate.bicep | `Microsoft.App/jobs` | `caj-ef-migrate-<env>` | trigger=Manual, replicaTimeout=900s, replicaRetryLimit=0, image=backend-api |
| 9 | postgres.bicep | `Microsoft.DBforPostgreSQL/flexibleServers` | `pg-dental-<env>-ksa` | version=16, sku=Standard_D2s_v3, storage=256GB, geoRedundantBackup=Enabled, backupRetentionDays=30, publicNetworkAccess=Disabled, privateEndpoint→snet-pg-pe |
| 10 | postgres.bicep | `Microsoft.DBforPostgreSQL/flexibleServers/databases` | `dental` | charset=UTF8, collation=en_US.UTF-8 |
| 11 | meili.bicep | `Microsoft.App/containerApps` | `ca-meili-<env>` | image=getmeili/meilisearch:vX.Y, minReplicas=1, maxReplicas=1, volume=Azure File→`vol-meili-<env>`, env=MEILI_MASTER_KEY (from KV secret) |
| 12 | meili.bicep | `Microsoft.Storage/storageAccounts/fileServices/shares` | `vol-meili-<env>` | quota=100GB, snapshot=daily |
| 13 | keyvault.bicep | `Microsoft.KeyVault/vaults` | `kv-dental-<env>` | enableRbacAuthorization=true, enableSoftDelete=true, softDeleteRetentionInDays=90, enablePurgeProtection=true |
| 14 | log-analytics.bicep | `Microsoft.OperationalInsights/workspaces` | `log-dental-<env>-ksa` | retention=90d, sku=PerGB2018 |
| 15 | app-insights.bicep | `Microsoft.Insights/components` | `appi-dental-<env>-ksa` | workspace-based, linked to log-analytics |
| 16 | managed-identity.bicep | `Microsoft.ManagedIdentity/userAssignedIdentities` | `id-aca-<env>` | assigned to ca-backend-api + ca-admin-web |
| 17 | static-web-app.bicep | `Microsoft.Web/staticSites` | `swa-customer-flutter-<env>` | sku=Standard, location=westeurope (SWA regional restriction; static content served globally via CDN) |
| 18 | alerts.bicep | `Microsoft.Insights/actionGroups` | `ag-oncall-<env>` | email + Microsoft Teams webhook |
| 19 | alerts.bicep | `Microsoft.Insights/metricAlerts` | `alert-deploy-failure-<env>` | source=workflow run, threshold=any failure |
| 20 | alerts.bicep | `Microsoft.Insights/metricAlerts` | `alert-health-probe-<env>` | source=ca-backend-api `/health`, threshold=3 consecutive non-200 in 90s |
| 21 | alerts.bicep | `Microsoft.Insights/metricAlerts` | `alert-high-5xx-<env>` | source=app insights, threshold=5xx > 1% over 5 min |
| 22 | alerts.bicep | `Microsoft.Insights/metricAlerts` | `alert-kv-anomaly-<env>` | source=kv diagnostic logs, threshold=any read by principal ≠ id-aca |
| 23 | role-assignments.bicep | `Microsoft.Authorization/roleAssignments` | (varies, see RBAC matrix) | scope and role per matrix |

**Note on resource 17 (Static Web App):** Azure Static Web Apps has a regional limitation — it provisions in a small set of regions (westeurope is the closest GA option to KSA). Content is served globally via Azure CDN, so latency to KSA + EG is acceptable. The KSA Central residency commitment under ADR-010 applies to **personal data processing**, not to static asset hosting; the SWA artifact contains only the compiled Flutter web bundle (no PII at rest). The decision and rationale are recorded in `infra/azure/DECISIONS.md`.

### Identity → role-assignment matrix

(See `contracts/infrastructure-contract.md` §RBAC matrix for the full normalized form.)

| Principal | Scope | Role |
|---|---|---|
| `id-aca-<env>` | `kv-dental-<env>` | Key Vault Secrets User |
| `gha-deploy-<env>` (federated) | `rg-dental-<env>-ksa` | Container Apps Contributor |
| `gha-deploy-<env>` (federated) | `kv-dental-<env>` | Key Vault Secrets User |
| `gha-deploy-<env>` (federated) | `pg-dental-<env>-ksa` | Reader (for `az containerapp exec` audit-emit path) |
| `gha-drift-<env>` (federated) | `rg-dental-<env>-ksa` | Reader |
| `aad-group-platform-engineers` (PIM) | `rg-dental-<env>-ksa` | Owner (break-glass only) |
| `aad-group-platform-engineers` (PIM) | `kv-dental-<env>` | Key Vault Administrator (break-glass only) |
| `aad-group-on-call` (PIM, time-boxed) | `rg-dental-<env>-ksa` | Container Apps Contributor (rollback path) |
| `aad-group-on-call` (PIM, time-boxed) | `kv-dental-<env>` | Key Vault Secrets Officer (rotation path) |
| `aad-group-auditors` | `rg-dental-<env>-ksa` | Reader |
| `aad-group-auditors` | `log-dental-<env>-ksa` | Log Analytics Reader |

**Cross-environment isolation**: `gha-deploy-stg` MUST NOT have any role assignment on `rg-dental-prd-ksa` or `kv-dental-prd`. Conversely for `gha-deploy-prd`. Verified by AC-11.

---

## §2 — Key Vault Secret Naming Taxonomy

### Path schema

```
<domain>/<market>/<provider>/<key-name>
```

### Domain set (closed; adding a domain requires a spec amendment)

| Domain | Notes |
|---|---|
| `payments` | ADR-007 secrets, populated by spec 027 |
| `shipping` | ADR-008 secrets, populated by spec 026 |
| `notifications-email` | ADR-009 email secrets, populated by spec 025 |
| `notifications-sms` | ADR-009 SMS secrets, populated by spec 025 |
| `notifications-push` | ADR-009 push secrets (FCM service-account JSON, etc.), populated by spec 025 |
| `db` | Postgres connection string, populated by E1 itself |
| `meili` | Meilisearch master key, populated by E1 itself |
| `app` | Backend application secrets (jwt-signing-key, data-protection-key), populated by E1 itself |

### Market set

| Market | Notes |
|---|---|
| `sa` | Saudi Arabia — Principle 5 first market |
| `eg` | Egypt — Principle 5 second market |
| `multi` | Cross-market aggregator providers (e.g., FCM, Checkout.com) |

### Key-name set

| Key name | Notes |
|---|---|
| `api-key` | Generic API key |
| `api-secret` | Generic API secret (paired with api-key) |
| `webhook-signing-key` | HMAC signing key for inbound webhooks (e.g., payment provider webhooks) |
| `client-id` | OAuth/OIDC client id |
| `client-secret` | OAuth/OIDC client secret |
| `account-sid` | Provider-specific account identifier (e.g., Twilio) |
| `auth-token` | Provider auth token (e.g., Twilio) |
| `service-account-json` | Service-account JSON document (e.g., FCM) |
| `connection-string` | Database connection string (`db` domain only) |
| `master-key` | Search master key (`meili` domain only) |
| `jwt-signing-key` | JWT HS256 / RS256 signing key (`app` domain only) |
| `data-protection-key` | ASP.NET Core Data Protection key (`app` domain only) |

### Validation regex

A CI guard validates every secret path matches:

```
^(payments|shipping|notifications-email|notifications-sms|notifications-push|db|meili|app)/(sa|eg|multi)/[a-z0-9-]+/(api-key|api-secret|webhook-signing-key|client-id|client-secret|account-sid|auth-token|service-account-json|connection-string|master-key|jwt-signing-key|data-protection-key)$
```

Single-domain secrets (`db`, `meili`, `app`) by convention use `multi` as the market segment and a fixed provider slug:
- `db/multi/postgres-flex/connection-string`
- `meili/multi/self-hosted/master-key`
- `app/multi/backend-api/jwt-signing-key`
- `app/multi/backend-api/data-protection-key`

### E1-provisioned placeholder secrets (12 ADR-007/008/009 slots + 4 own)

Created at provisioning time with sentinel value `__placeholder_set_by_E1__` and Vault tag `set_by_spec=E1; expected_real_value_in=025|026|027`. Placeholder slugs use the regex-friendly form `tbd-by-NNN` (no angle brackets) so the secret-naming validation regex matches both placeholder and post-selection paths uniformly:

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

When 025/026/027 select providers, the provider slug (e.g., `paymob`, `bosta`, `unifonic`) replaces the `tbd-by-NNN` segment via the deletion of the placeholder secret and creation of a fresh secret at the new path; the placeholder rename is handled in the 025/026/027 implementation, which emits a `secret.placeholder_replaced` audit event for each transition.

**Storage-encoding note.** Azure Key Vault secret names accept only `^[a-zA-Z0-9-]+$` (no slashes). The taxonomy paths above are *logical* paths used in documentation, audit events, and CI guards. The actual on-disk secret names flatten the slash separator to a double-hyphen `--`. For example, the logical path `meili/multi/self-hosted/master-key` is stored as the Azure KV secret name `meili--multi--self-hosted--master-key`. Conversion is mechanical (`s|/|--|g`); both the deploy scripts and the audit-emit wrapper do this transparently. Validation regexes apply to the **logical path form**.

E1's own secrets, populated at provisioning with real values:

```
db/multi/postgres-flex/connection-string
meili/multi/self-hosted/master-key
app/multi/backend-api/jwt-signing-key
app/multi/backend-api/data-protection-key
```

### Vault metadata schema (per secret)

| Field | Type | Required | Notes |
|---|---|---|---|
| `value` | string | Yes | The secret value itself |
| Tag `set_by_spec` | string | Yes | The spec ID that wrote this secret (`E1`, `025`, `026`, `027`) |
| Tag `expected_real_value_in` | string | Only for placeholders | Spec ID that will overwrite the placeholder |
| Tag `rotated_at` | ISO-8601 | Set on rotation | When the most recent version was written |
| Tag `rotation_cadence_days` | integer | Yes | 90 for ADR-007/008/009 secrets, 30 for admin-only, 365 for jwt-signing-key |
| Version expiration | datetime | No | Optional; if set, alert fires at expiration - 14 days |

---

## §3 — Audit Event Schema

E1 introduces eight new event types into the existing `audit_log_entries` table from spec 003. No table changes required — `audit_log_entries.event_type` is a free string column already.

### Event types (additive)

| Event type | Emitted by | Payload (JSONB) — required keys | Retention |
|---|---|---|---|
| `infra.iac.applied` | Bicep apply (manual or automated) | `bicep_template_sha`, `actor_oid`, `resource_changes_count`, `environment` | 365+ days |
| `infra.drift.detected` | `infra-drift.yml` workflow | `resource_ids[]`, `expected_sha`, `actual_sha`, `environment`, `parsed_changes_json_url` | 365+ days |
| `deploy.attempted` | `deploy-staging.yml` / `deploy-production.yml` (start of run) | `github_run_id`, `commit_sha`, `images[]`, `environment`, `actor_login`, `correlation_id` | 365+ days |
| `deploy.completed_succeeded` | deploy workflow (terminal success) | `github_run_id`, `commit_sha`, `azure_managed_identity_oid`, `images_deployed[]`, `migrations_applied_count`, `smoke_results[]`, `environment`, `correlation_id` | 365+ days |
| `deploy.completed_failed` | deploy workflow (terminal failure) | same as `_succeeded` plus `failure_state`, `failure_reason` | 365+ days |
| `deploy.rollback` | rollback path | `github_run_id`, `from_sha`, `to_sha`, `actor_login`, `environment`, `correlation_id` | 365+ days |
| `secret.rotated` | KV diagnostic log → audit-emit | `vault_name`, `secret_name`, `old_version_id`, `new_version_id`, `actor_oid` | 365+ days |
| `secret.placeholder_replaced` | KV diagnostic log → audit-emit (when sentinel value is overwritten) | `vault_name`, `secret_name`, `replaced_by_spec` ∈ {`025`,`026`,`027`} | 365+ days |

### Common columns (all events, inherited from `audit_log_entries`)

| Column | Source | Notes |
|---|---|---|
| `id` | DB-generated UUID | Primary key |
| `event_type` | enum-like string | Per table above |
| `event_timestamp` | UTC `now()` | Set by audit writer |
| `actor_kind` | enum {`user`, `system`, `github-actions`, `azure-managed-identity`} | E1 events use `github-actions` or `azure-managed-identity` |
| `actor_id` | string | GitHub run id or AAD object id |
| `payload` | JSONB | Event-specific keys per table above |
| `correlation_id` | string | UUID linking related events (e.g., `attempted` + `completed_*` share a correlation_id) |

### Event lifecycle invariants

1. Every `deploy.attempted` MUST be followed (within 30 minutes) by exactly one of `deploy.completed_succeeded` or `deploy.completed_failed` carrying the same `correlation_id`.
2. Every `deploy.rollback` references a `from_sha` and `to_sha` that both have prior `deploy.completed_succeeded` events.
3. Every `secret.rotated` references a `vault_name` + `secret_name` that exists in the resource inventory (V-3 regex).
4. The weekly audit-completeness job verifies invariant 1 over the past 7 days; gap = P1 incident.

### Correlation strategy

The deploy workflow generates a single UUID at start, passes it as `correlation_id` to both `deploy.attempted` and `deploy.completed_*`, and persists it to `/tmp/correlation_id` for inclusion in `smoke_results[]` payloads. This makes the entire deploy run queryable as a single causal chain.

---

## §4 — IaC drift event payload schema

Sub-shape of `infra.drift.detected.payload`:

```json
{
  "environment": "staging" | "production",
  "resource_ids": ["/subscriptions/.../resourceGroups/.../providers/Microsoft.../<name>", ...],
  "expected_sha": "<bicep-template-sha-at-last-apply>",
  "actual_sha": "<inferred-from-az-resource-show>",
  "parsed_changes": [
    { "resource_id": "...", "change_kind": "Modify"|"Delete"|"Create", "fields_changed": ["tags.cost_center", ...] }
  ],
  "drift_run_id": "<github-actions-run-id>",
  "drift_run_url": "https://github.com/.../actions/runs/..."
}
```

`change_kind=Create` for unexpected manual resources. `change_kind=Delete` for expected-but-missing resources (likely manual deletion).

---

## §5 — Cross-references

| Source | Consumed by |
|---|---|
| Spec 003 — `audit_log_entries` table | All eight E1 event types |
| Spec 003 — `AddLayeredConfiguration()` | Backend reads `KEY_VAULT_URI` to bootstrap |
| ADR-010 | Region + residency posture for §1 |
| ADR-007/008/009 | Secret slots in §2 |
| Phase 1A `docker-build.yml` | Provides backend image (consumed by §1 row 6) |
| Phase 1C-Infra `admin-docker-build.yml` | Provides admin image (consumed by §1 row 7) |
| Phase 1C `apps/customer_flutter/web` build | Provides Flutter bundle (consumed by §1 row 17) |

This data model is the contract that `tasks.md` will translate into work items.
