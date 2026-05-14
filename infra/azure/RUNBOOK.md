# Operational Runbook — Phase 1E Infrastructure

**Spec**: [`../../specs/phase-1E/E1-infrastructure-integration/spec.md`](../../specs/phase-1E/E1-infrastructure-integration/spec.md)
**Decisions log**: [`./DECISIONS.md`](./DECISIONS.md)
**Audience**: on-call platform engineer
**Time budget**: any procedure here MUST be completable in ≤ 30 minutes by a rotating on-call shadow with only this runbook open (AC-21).

This runbook is the single source of truth for routine operational tasks on the Phase 1E
infrastructure. Sections a–i map to AC-20's required topics. Every procedure ends with a
"**verified by**" line listing the AC ids it satisfies.

> Before running ANY destructive step, capture the current state in `${HOME}/.runbook-cache/`
> (a procedure-local sandbox) so you can roll back if something goes sideways.

---

## §a — Secret rotation procedure

Rotation cadences (`rotation_cadence_days` tag on every secret):

| Cadence | Secrets |
|---|---|
| 30 days  | Database connection string (`db/multi/postgres-flex/connection-string`) |
| 90 days  | ADR-007/008/009 provider secrets (payments, shipping, notifications), Meilisearch master key |
| 365 days | JWT signing key, ASP.NET Data Protection key |

### Step-by-step

1. **PIM elevation** — Open the Azure Portal → Privileged Identity Management → My Roles →
   `aad-group-on-call`. Activate the **Key Vault Secrets Officer** role on the target vault
   (`kv-dental-stg` or `kv-dental-prd`). Maximum activation duration: 4 hours. Justification:
   "scheduled rotation of `<secret-name>` per runbook §a".

2. **Capture current state** — Read the existing secret version so you can roll back if the
   new value is rejected by the runtime:
   ```bash
   az keyvault secret show \
     --vault-name kv-dental-<env> \
     --name <flattened-secret-name> \
     --query "{id:id, version:properties.version}" -o json \
     > ~/.runbook-cache/rotation-$(date +%s).json
   ```

3. **Set the new value** — Use `az keyvault secret set` with `--tags` carrying the updated
   `rotated_at` timestamp. The deploy workflow's KV permissions DO NOT include `secret set`;
   only the PIM-elevated rotation role does.
   ```bash
   az keyvault secret set \
     --vault-name kv-dental-<env> \
     --name <flattened-secret-name> \
     --value "<new-value>" \
     --tags rotated_at=$(date -u +%FT%TZ) set_by_spec=E1
   ```

4. **Watch for propagation** — The backend caches secret values for 5 minutes
   (DECISIONS.md DD-2). Wait 5 minutes, then validate via the diagnostic endpoint:
   ```bash
   curl -fsS https://${BACKEND_FQDN}/diagnostics/config/cache-stamp | jq .
   ```
   The `cache_age_seconds` should reset to < 60.

5. **Audit-emit** — The KV diagnostic-log → audit-event pipeline (Phase 4) will write a
   `secret.rotated` row automatically within 60 seconds. Confirm by querying the audit log:
   ```sql
   SELECT * FROM audit_log_entries
   WHERE action = 'secret.rotated'
     AND payload->>'secret_name' = '<flattened-secret-name>'
   ORDER BY occurred_at DESC LIMIT 1;
   ```

6. **PIM deactivation** — Return to PIM and deactivate the elevated role explicitly (do not
   wait for the 4-hour timeout).

**Verified by**: AC-15 (KV diagnostic logs stream to Log Analytics), AC-18 (rotation
propagates without restart), AC-20 (procedure exists).

---

## §b — Re-running migrations manually

When the EF migration job fails mid-run (rare — usually only on accidental schema conflicts),
re-run it manually with the image-pin from the last good deploy:

```bash
# Fetch the last image tag from the running revision.
last_image=$(az containerapp revision list \
  --name ca-backend-api-stg \
  --resource-group rg-dental-stg-ksa \
  --query "[?properties.active==\`true\`] | [0].properties.template.containers[0].image" -o tsv)

bash scripts/azure/run-migrations-job.sh staging "${last_image##*:}"
```

If the job fails again, capture the EF migration log:
```bash
az containerapp job execution show \
  --name caj-ef-migrate-stg \
  --resource-group rg-dental-stg-ksa \
  --job-execution-name <execution-id> \
  --query "properties" -o json > ~/.runbook-cache/migrate-fail-$(date +%s).json
```

**Verified by**: AC-7 (migrations before activation), AC-20.

---

## §c — Rollback by image tag

The deploy-staging.yml workflow supports manual rollback via `workflow_dispatch`. Procedure:

1. **Identify the target tag** — Find the last known-good commit sha on `main`:
   ```bash
   gh run list --workflow=deploy-staging.yml --status=success --limit=10
   ```

2. **Trigger rollback** — Skip migrations (the prior tag's schema is already in place):
   ```bash
   gh workflow run deploy-staging.yml \
     -f image_tag=<sha> \
     -f skip_migrations=true \
     -f skip_seed_apply=true
   ```

3. **Watch the run** — Open the GitHub Actions UI; expect Completed within 10 minutes.

4. **Validate** — The smoke probes at the end of the workflow assert health + admin index +
   meili + flutter-web. If all five pass, the rollback is complete.

5. **Audit** — A `deploy.rollback` row is emitted automatically with `from_sha` (current) and
   `to_sha` (target).

**Verified by**: AC-10 (rollback by image tag works), AC-20.

---

## §d — Seed-dataset refresh cadence

| Environment | Cadence | Mode |
|---|---|---|
| Staging     | Monthly (1st of the month) | `--mode=apply` |
| Production  | NEVER via `apply`           | `--mode=dry-run` only |

The Staging monthly refresh is triggered by `deploy-staging.yml`'s seed step. Production
is hard-blocked by spec 003's `SeedGuard` in `Program.cs` (`return 1` before any DI runs)
AND by `scripts/azure/run-seed-job.sh` (which refuses the apply+production combination).

If a one-off seed refresh is needed off-cycle (Staging only):
```bash
gh workflow run deploy-staging.yml \
  -f skip_migrations=true \
  -f skip_seed_apply=false   # explicit
```

**Verified by**: AC-8 (apply Staging-only / dry-run Production-only), AC-20.

---

## §e — Cross-region failover deferral

Phase 1E is **single-region** (`saudiarabiacentral`) per ADR-010. Cross-region failover to
a secondary KSA region (or to UAE North) is **not in v1 scope** because:

- ADR-010 fixes the residency posture to `ksacentral` — multi-region within KSA would
  require an additional region pair to materialize in Azure (not currently GA).
- Cross-region within KSA is currently constrained to availability zones inside one
  region; the Postgres Flexible Server's `geoRedundantBackup=Enabled` provides the
  DR boundary (geo-redundant storage replicates to the paired region for backups, not
  for online failover).

**If a regional outage occurs**:

1. Wait for Azure to communicate ETA via the status page (incidents are typically resolved
   within 4 hours; full-region failures are rare).
2. If wait time exceeds 24 hours, escalate to product leadership for a one-off ADR
   amendment authorizing a temporary cross-region restore from geo-backup. This is an
   intentional pause on automation: the human decision protects against accidental
   data residency violations.

A geo-redundant restore drill is run quarterly (see §h).

**Verified by**: AC-20.

---

## §f — Postgres major-version upgrade

Postgres major version is **16** at launch (DECISIONS.md DD-3). Upgrade procedure when 17
stabilizes (target: 6 months post-17 GA):

1. **Stand up a logical replica** at the new version using `pg_basebackup`-style migration
   (Azure's Migration Service handles this for Flexible Server).
2. **Run application against the replica** in Staging for 2 weeks; watch for index
   regressions, plan changes, and incompatibility warnings in EF Core 9.
3. **Cutover** during a low-traffic window:
   - Briefly pause writes (~ 60s) via the deploy workflow's `pause_writes` knob (added
     in Phase 1F).
   - Repoint the KV `db/multi/postgres-flex/connection-string` secret to the new server.
   - Wait 5 minutes for the backend's secret cache to refresh.
   - Unpause writes.
4. **Decommission the old server** after 7 days of stable operation.

**Verified by**: AC-20.

---

## §g — Meilisearch master-key rotation

Meilisearch master-key rotation is special because all index data is encrypted with the
key — rotating the key requires reindexing.

1. **Generate a new master key** (256-bit random):
   ```bash
   new_key=$(openssl rand -base64 32)
   ```

2. **PIM-elevate to Key Vault Secrets Officer** on `kv-dental-<env>`.

3. **Update the KV secret**:
   ```bash
   az keyvault secret set \
     --vault-name kv-dental-<env> \
     --name "meili--multi--self-hosted--master-key" \
     --value "$new_key" \
     --tags rotated_at=$(date -u +%FT%TZ) set_by_spec=E1
   ```

4. **Restart the Meilisearch container app** so it picks up the new key (the env var is
   sourced via `secretRef`, but Meilisearch reads the key only at startup):
   ```bash
   az containerapp revision restart \
     --name ca-meili-<env> \
     --resource-group rg-dental-<env>-ksa \
     --revision $(az containerapp revision list \
                   --name ca-meili-<env> \
                   --resource-group rg-dental-<env>-ksa \
                   --query "[?properties.active==\`true\`] | [0].name" -o tsv)
   ```

5. **Reindex** — All previously-encrypted index documents are now unreadable. Trigger a
   full re-index from the backend:
   ```bash
   az containerapp exec \
     --name ca-backend-api-<env> \
     --resource-group rg-dental-<env>-ksa \
     --command "/bin/sh -lc 'dotnet /app/backend_api.dll search-reindex --all'"
   ```

6. **Re-run smoke probe 03** to confirm the index is healthy:
   ```bash
   bash scripts/azure/smoke/03-meili-query.sh <env>
   ```

**Verified by**: AC-25 (Meilisearch self-hosted on ACA with documented rotation), AC-20.

---

## §h — Disaster recovery (geo-redundant backup restore)

Quarterly cadence (last Friday of each quarter — non-business hours preferred).

1. **Trigger a geo-restore** of the Postgres flexible server into a side-by-side
   `rg-dental-stg-dr-ksa` resource group:
   ```bash
   az postgres flexible-server geo-restore \
     --resource-group rg-dental-stg-dr-ksa \
     --name pg-dental-stg-dr-ksa \
     --source-server pg-dental-stg-ksa \
     --location saudiarabiacentral \
     --restore-time "$(date -u --date='15 minutes ago' +%FT%TZ)"
   ```

2. **Restore the meili volume** from snapshot (Azure File daily-snapshot policy from
   Phase 1):
   - Locate the most recent snapshot of `vol-meili-stg`.
   - Restore into a side-by-side share `vol-meili-stg-dr`.
   - Stand up a temporary Meilisearch ACA container pointed at the restored share.

3. **Validate**: run the smoke probes (`run-all.sh staging-dr`) against the DR stack.
   All five should pass.

4. **Tear down** the DR stack after the validation log is captured.

5. **Record** the drill in the exercise log (§i).

**Verified by**: AC-20.

---

## §i — Exercise log

| Date | Procedure | Actor | Wall-clock | Outcome | Notes |
|---|---|---|---|---|---|
| _(none yet — first drill scheduled post-Phase-1F)_ |  |  |  |  |  |

Each row is added **only after** the procedure has been completed end-to-end. If a procedure
ran but exceeded the 30-minute budget, file an issue with the `runbook-improvement` label
and re-test after the issue is closed.

---

## §Production deploy gate (T067 documentation)

The Production environment in GitHub is configured per clarify-locked decision D-5
(2-of-N approvers). Configuration steps for the GitHub Environments UI:

1. **Settings → Environments → New environment** → name `production`.
2. **Required reviewers** → set to **2** and add the `ProductionDeployers` team.
3. **Prevent self-review** → enable (`prevent_self_review = true`). The actor who
   triggered `deploy-production.yml` cannot count toward their own approvals.
4. **Deployment branch policy** → "Selected branches" → add `main` (protected branches
   only).

The "2 required reviewers" setting is set via the **UI only** — the `gh api PUT
/repos/.../environments/production` endpoint accepts the team-id reviewer list but not the
minimum-approver count as of GitHub's REST API surface today. The other settings (team
reviewers, branch policy, prevent_self_review) CAN be applied via `gh api`:

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
```

**Compliance audit query** — validate the current setting weekly:

```bash
gh api /repos/$GITHUB_ORG/$GITHUB_REPO/environments/production --jq '.protection_rules'
```

The output must include a `required_reviewers` rule with `reviewers.length >= 1` and the
team id matching `ProductionDeployers`.

**Verified by**: AC-23.

---

## §Dashboard query

Daily compliance dashboard (T072) — saved Log Analytics query (run via the workbook in
the Azure Portal). Source query lives in [`compliance-dashboard.kql`](./compliance-dashboard.kql).
Pinned to the on-call team's home dashboard.

---

## §Change log

| Date | Author | Section | Change |
|---|---|---|---|
| 2026-05-14 | @Mkhira | All | Initial runbook drafted as part of E1 Phase 6 (T063). |
