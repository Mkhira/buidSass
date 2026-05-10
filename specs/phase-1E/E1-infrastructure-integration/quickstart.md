# Quickstart: E1 — Infrastructure Integration

**Phase**: 1 (Design)
**Date**: 2026-05-10
**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Target audience**: a platform engineer setting up Staging from scratch.
**Verifies**: SC-1 — "From a clean subscription, an engineer can stand up a fully working Staging environment (IaC apply + first deploy + smoke pass) in under 60 minutes of wall-clock time, from `git clone` to a green `/health`."

---

## Prerequisites

| Requirement | How to verify |
|---|---|
| Azure subscription with Owner role on the target subscription | `az account show --query 'roleAssignments'` lists Owner. |
| Azure tenant id, subscription id, AAD group object id (`aad-group-platform-engineers`) | Documented in your platform team's onboarding doc. |
| GitHub repository admin role | Required to add federated credentials and Environments. |
| Local tools: `az` ≥ 2.60, `bicep` ≥ 0.27, `gh` ≥ 2.40, `jq`, `psql`, `git`. | `az version`, `bicep --version`, `gh --version` |
| GHCR access: backend + admin images already published by Phase 1A and Phase 1C-Infra workflows | `gh api /orgs/<org>/packages/container/backend-api` returns 200. |
| Phase 1A and Phase 1C at DoD | Confirm via `docs/implementation-plan.md` exit criteria. |

If any prerequisite is unmet, **stop**. E1 cannot proceed without them.

---

## Step 1 — Clone and switch to the E1 branch (≤ 2 min)

```bash
git clone https://github.com/<org>/<repo>.git
cd <repo>
git checkout phase-1E
```

Verify spec presence:

```bash
ls specs/phase-1E/E1-infrastructure-integration/
# expect: spec.md plan.md research.md data-model.md quickstart.md contracts/ checklists/ tasks.md
```

---

## Step 2 — Bootstrap federated credentials (≤ 8 min)

This is a **one-time-per-tenant** step. If it has already been run (check by listing federated credentials on the user-assigned managed identity), skip.

```bash
# Set the seven required env vars
export AZURE_TENANT_ID=<tenant-id>
export AZURE_SUBSCRIPTION_ID=<subscription-id>
export GITHUB_ORG=<org>
export GITHUB_REPO=<repo>
export AAD_PLATFORM_GROUP_OID=<aad-group-platform-engineers-OID>
export AAD_ON_CALL_GROUP_OID=<aad-group-on-call-OID>
export AAD_AUDITORS_GROUP_OID=<aad-group-auditors-OID>

az login --tenant "$AZURE_TENANT_ID"
az account set --subscription "$AZURE_SUBSCRIPTION_ID"

# Idempotent script: creates 4 federated credentials (gha-deploy-stg, gha-deploy-prd, gha-drift-stg, gha-drift-prd)
# on user-assigned managed identities, with subject claims pinned per research.md §2.
./scripts/azure/setup-federated-credentials.sh
```

Verify:

```bash
az identity federated-credential list --identity-name id-aca-stg --resource-group rg-dental-stg-ksa
# expect: gha-deploy-stg, gha-drift-stg
```

---

## Step 3 — Apply Bicep IaC for Staging (≤ 25 min)

```bash
az deployment sub create \
  --location ksacentral \
  --template-file infra/azure/main.bicep \
  --parameters infra/azure/parameters/staging.bicepparam \
  --parameters ownerAadGroupOid="$AAD_PLATFORM_GROUP_OID"
```

Expected wall-clock: 18–22 minutes. Postgres flex provisioning is the long pole (~12 min); ACA environment ~5 min; everything else parallelizes.

Verify all 17 + 6 (alerts/role-assignments) resources are present:

```bash
az resource list -g rg-dental-stg-ksa --query 'length([?tags.environment==`staging`])'
# expect: ≥ 17 (the precise count includes role-assignment objects)
```

Verify Postgres has zero firewall rules and public access disabled:

```bash
az postgres flexible-server show -n pg-dental-stg-ksa -g rg-dental-stg-ksa \
  --query '{public: publicNetworkAccess, fwRules: length(@)}'
# expect: { "public": "Disabled", "fwRules": 0 }
```

Verify Key Vaults are RBAC-enabled with purge-protection:

```bash
for v in kv-dental-stg kv-dental-prd; do
  az keyvault show -n $v --query '{rbac: properties.enableRbacAuthorization, soft: properties.enableSoftDelete, purge: properties.enablePurgeProtection}'
done
# expect both: { "rbac": true, "soft": true, "purge": true }
```

---

## Step 4 — Bootstrap placeholder secrets (≤ 3 min)

```bash
az deployment group create \
  --resource-group rg-dental-stg-ksa \
  --template-file infra/azure/keyvault-bootstrap.bicep \
  --parameters env=staging
```

Verify all 12 ADR-007/008/009 placeholder slots exist plus the 4 E1-owned secrets:

```bash
az keyvault secret list --vault-name kv-dental-stg --query 'length([?contains(tags.set_by_spec, `E1`)])'
# expect: 16
```

---

## Step 5 — Configure GitHub Environments and Variables (≤ 5 min)

```bash
gh api -X PUT /repos/$GITHUB_ORG/$GITHUB_REPO/environments/staging --silent
gh api -X PUT /repos/$GITHUB_ORG/$GITHUB_REPO/environments/production --silent

# Set variables (NOT secrets — these are non-sensitive) at repo level
gh variable set AZURE_TENANT_ID --body "$AZURE_TENANT_ID"
gh variable set AZURE_SUBSCRIPTION_ID --body "$AZURE_SUBSCRIPTION_ID"
gh variable set AZURE_DEPLOY_STG_CLIENT_ID --body "$(az identity show -n id-aca-stg -g rg-dental-stg-ksa --query clientId -o tsv)"
gh variable set AZURE_DEPLOY_PRD_CLIENT_ID --body "$(az identity show -n id-aca-prd -g rg-dental-prd-ksa --query clientId -o tsv)"

# Production environment: 2-of-N approval gate (clarify-locked)
gh api -X PUT /repos/$GITHUB_ORG/$GITHUB_REPO/environments/production \
  --input - <<EOF
{ "wait_timer": 0, "reviewers": [], "deployment_branch_policy": { "protected_branches": true, "custom_branch_policies": false } }
EOF
# Then in the GitHub UI, manually add the 'ProductionDeployers' team as required reviewers with min approvers = 2.
```

Verify in the GitHub UI: Settings → Environments → production → Required reviewers → "ProductionDeployers" with **minimum approvers = 2**.

---

## Step 6 — Run the first Staging deploy (≤ 12 min)

Trigger a manual `workflow_dispatch` of `deploy-staging.yml` against the latest images on `main`:

```bash
gh workflow run deploy-staging.yml --ref main
gh run watch
```

Expected sequence (each step prints elapsed time):

| Step | Duration | Notes |
|---|---|---|
| OIDC token exchange | 5–10 s | `azure/login@v2` |
| `deploy.attempted` audit emit | 5–15 s | First-ever run uses `--skip-audit-emit` (no backend container yet); subsequent runs emit normally. |
| EF migrations job | 60–180 s | Runs `dotnet ef database update`; waits for `Succeeded`. |
| Activate backend revision | 30–60 s | New revision becomes traffic-receiving. |
| Activate admin revision | 30–60 s | |
| Seed apply (Staging only) | 30–120 s | Idempotent. First run writes seed_applied rows; subsequent runs write zero. |
| Smoke probes (5) | ≤ 90 s | Aggregate timeout. |
| `deploy.completed_succeeded` audit emit | 5–15 s | |

Total wall-clock: 4–9 minutes for a typical run.

---

## Step 7 — Verify the smoke results (≤ 3 min)

```bash
# 1. /health on the backend
curl -fsS "https://ca-backend-api-stg.<env-suffix>.azurecontainerapps.io/health"
# expect: 200 OK with version + sha + uptime

# 2. seed --mode=dry-run inside the backend container
az containerapp exec -n ca-backend-api-stg -g rg-dental-stg-ksa \
  --command "dotnet AdminTool.dll seed --mode=dry-run"
# expect: exit 0, "0 new seeders applied"

# 3. Meilisearch query (smoke index has at least one doc seeded by step 6)
MEILI_URL="https://ca-meili-stg.<env-suffix>.azurecontainerapps.io"
MEILI_KEY=$(az keyvault secret show --vault-name kv-dental-stg --name meili--multi--self-hosted--master-key --query value -o tsv)
curl -fsS "$MEILI_URL/indexes/products/search" -H "Authorization: Bearer $MEILI_KEY" -d '{"q":"test"}' | jq '.hits | length'
# expect: ≥ 1

# 4. admin index page
curl -fsS -o /dev/null -w "%{http_code}\n" "https://ca-admin-web-stg.<env-suffix>.azurecontainerapps.io/"
# expect: 200

# 5. Flutter web index + main.dart.js asset
SWA_URL="https://swa-customer-flutter-stg.<env-suffix>.azurestaticapps.net"
curl -fsS -o /dev/null -w "%{http_code}\n" "$SWA_URL/"
curl -fsS -o /dev/null -w "%{http_code}\n" "$SWA_URL/main.dart.js"
# expect: both 200
```

If any of the five fails, the deploy workflow already emitted `deploy.completed_failed` — see the audit-log row for diagnosis.

---

## Step 8 — Verify the audit-log entries (≤ 2 min)

```bash
PG_CONN=$(az keyvault secret show --vault-name kv-dental-stg --name db--multi--postgres-flex--connection-string --query value -o tsv)
psql "$PG_CONN" -c "
  SELECT event_type, event_timestamp, actor_kind, actor_id, payload->>'environment' AS env
  FROM audit_log_entries
  WHERE event_type LIKE 'deploy.%'
  AND event_timestamp > now() - interval '15 minutes'
  ORDER BY event_timestamp;
"
# expect: at least 2 rows: deploy.attempted + deploy.completed_succeeded, sharing a correlation_id
```

---

## Step 9 — Total wall-clock check (SC-1 verification)

Stop the timer.

| Step | Allotted | Cumulative |
|---|---|---|
| 1 — Clone + branch | 2 min | 2 |
| 2 — Federated credentials | 8 min | 10 |
| 3 — Bicep IaC apply | 25 min | 35 |
| 4 — KV bootstrap | 3 min | 38 |
| 5 — GitHub Environments | 5 min | 43 |
| 6 — First deploy | 12 min | 55 |
| 7 — Smoke probes | 3 min | 58 |
| 8 — Audit-log check | 2 min | 60 |
| **Total** | **60 min** | |

If your total exceeds 60 minutes, the bottleneck is most likely Postgres provisioning (consider pre-warming a sandbox flex server) or the federated-credential bootstrap (one-time per tenant — should be amortized to zero on subsequent runs).

---

## Quickstart troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `az login --federated-token` fails with "subject does not match" | Federated credential subject claim typo | Re-run `setup-federated-credentials.sh --dry-run` to print expected vs actual; correct in script. |
| Bicep apply hangs at Postgres provisioning > 30 min | Regional capacity issue or quota | Check `az vm list-skus -l ksacentral`; file quota request if needed. |
| EF migrations job times out | DB unreachable from the migrations job (private endpoint not connected) | Verify the migrations job's container app uses the same VNet as Postgres (`snet-pg-pe`). |
| Smoke probe 4 (admin index) returns 404 | Admin image pulled but Next.js standalone build produced no index | Phase 1C-Infra image issue, not E1. Check `admin-docker-build.yml`. |
| Smoke probe 5 (Flutter `main.dart.js`) returns 404 | SWA deploy artifact path mismatch | Check the SWA deployment workflow input — Flutter web bundle root is `apps/customer_flutter/build/web`. |
| Audit-log query returns 0 rows | First-ever deploy used `--skip-audit-emit` | Run a second deploy; the second deploy emits audit normally. |

---

## Production setup (separate run)

After Staging is green and stable, run the same nine steps for Production:

- Step 3 uses `infra/azure/parameters/production.bicepparam`.
- Step 4 uses `kv-dental-prd`.
- Step 6 uses `gh workflow run deploy-production.yml` and **requires 2-of-N approval** (clarify-locked).
- Step 6 hard-codes `seed --mode=dry-run` (Production never `--mode=apply`).

Production wall-clock is similar (~60 min for the first-ever run; ~10 min for routine deploys).

---

## What's next

After E1 acceptance:

- **Spec 025 (notifications)** unblocks: provisions provider slugs into the `notifications-{email,sms,push}/...` slots.
- **Spec 026 (shipping)** unblocks: provisions provider slugs into `shipping/...` slots.
- **Spec 027 (payments-integration)** unblocks: provisions provider slugs into `payments/...` slots.
- **Spec 029 (qa-and-hardening)** can begin k6 load tests against the Staging stack.

The runbook (`infra/azure/RUNBOOK.md`) is the day-2 operations reference. Bookmark it.
