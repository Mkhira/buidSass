# Research: E1 — Infrastructure Integration

**Phase**: 0 (Outline & Research)
**Date**: 2026-05-10
**Spec**: [spec.md](./spec.md)
**Plan**: [plan.md](./plan.md)

This document records the five research areas that closed open questions for the planning step. Every clarify-stage decision (Meilisearch self-hosted on ACA, Flutter web on Azure Static Web Apps, Postgres `Standard_D2s_v3`, manual drift remediation, 2-of-N production approvers) is treated as **decided input**, not subject to further research. The five sections below resolve **planning-stage** unknowns.

---

## §1 — Bicep module decomposition

**Decision**: Decompose into **16 modules** under `infra/azure/modules/`, plus `main.bicep` (subscription-level entry) and `keyvault-bootstrap.bicep` (run separately to populate placeholder secrets).

**Rationale**:

- **One module per resource type, not per logical group.** Keeps each module under 150 LOC, maximizes reuse between Staging and Production (same module, different parameters), and makes RBAC review of the module surface tractable.
- **Subscription-level entry point** is required because Resource Group creation must happen at subscription scope. `main.bicep` is `targetScope = 'subscription'`; modules are RG-scoped via `scope: rg`.
- **Bootstrap is separate**: secrets containing sentinel values are written via `keyvault-bootstrap.bicep` after the vault exists. Combining this with `main.bicep` would require nested deployments and complicate idempotency on re-apply.
- **Module-per-resource boundary** also matches the tag-completeness CI check (`tag-completeness-check`) — it parses Bicep modules and checks every `Microsoft.*` resource for the four mandatory tags. Larger modules would weaken the check by mixing tag-applied and tag-skipped resources.

**Alternatives considered**:

- **Three modules grouped by logical concern (network / data / compute).** Rejected because each module would exceed 400 LOC, mix RBAC scopes, and obscure the per-resource tag check. Code review would also become noisier per resource change.
- **Bicep registry module imports** (e.g., from `br/public:avm/`). Deferred to v1.5. AVM modules are excellent but add an indirection layer that requires version pinning + drift discipline; for E1 we want all source under our own CODEOWNERS-protected paths until the team has muscle memory with the full stack.

**References**: Microsoft Bicep best-practices guide (subscription-scope deployments), AVM (Azure Verified Modules) public registry pattern.

---

## §2 — OIDC federated credential subject-claim shape

**Decision**: Use **environment-scoped** subject claims for the two deploy identities (`gha-deploy-stg`, `gha-deploy-prd`), and **branch-scoped** subject claims for the two drift identities (`gha-drift-stg`, `gha-drift-prd`).

**Subject claims locked**:

```
gha-deploy-stg:  repo:<org>/<repo>:environment:staging
gha-deploy-prd:  repo:<org>/<repo>:environment:production
gha-drift-stg:   repo:<org>/<repo>:ref:refs/heads/main
gha-drift-prd:   repo:<org>/<repo>:ref:refs/heads/main
```

The drift workflow further pins the job to `main` branch only via the workflow file's `if: github.ref == 'refs/heads/main'` guard. Drift identities cannot deploy because they lack `Container Apps Contributor` RBAC.

**Rationale**:

- **Environment scoping for deploys** binds the federated credential to the GitHub Environments approval gate. A workflow trying to deploy to `production` MUST go through the 2-of-N approval gate before it can even obtain the OIDC token; the token-exchange itself fails if the workflow does not reference `environment: production`.
- **Branch scoping for drift** is sufficient because drift is read-only (`bicep what-if` does not mutate resources); narrowing to `main` prevents feature-branch experiments from polluting drift reports.
- **Why not actor-scoped?** Actor scoping (`actor:<github-handle>`) would force a federated-credential rotation every time a team member changes; not operationally sustainable.
- **Why not workflow-scoped?** Workflow scoping (`workflow:<name>`) is brittle to workflow renames and does not gate on the GitHub Environments approval list.

**Alternatives considered**:

- **Single federated identity covering all four cases.** Rejected because it would force the drift identity to carry deploy RBAC (over-privilege).
- **Long-lived service principal client secret in GitHub Actions secrets.** Forbidden by spec.md BR-3.

**References**: GitHub OIDC documentation (configuring OIDC in cloud providers), Azure AD federated identity credential subject claims.

---

## §3 — Meilisearch persistent storage: Azure File vs Azure Disk

**Decision**: **Azure File** (SMB share) mounted as an ACA volume.

**Rationale**:

- **Cross-AZ flexibility.** Azure File is zone-redundant by default in supported regions; the index survives an AZ outage. Azure Disk would pin Meilisearch to a single AZ.
- **Snapshot simplicity.** Azure File supports point-in-time snapshots without dismounting; Azure Disk snapshots require detach + re-attach, complicating live backup.
- **ACA volume-mount native support.** ACA officially supports Azure File volumes; Azure Disk requires Premium SSD v2 + workaround patterns at v1.
- **Performance is sufficient.** Meilisearch's working set fits in memory at launch catalog size (< 50K SKUs); index file IO is bursty during reindex, not steady-state. Azure File's IOPS are adequate for that workload.

**Alternatives considered**:

- **Azure Disk (Premium SSD v2).** Higher per-IOPS performance; not justified for launch catalog size; more complex backup story.
- **Ephemeral storage + warm-up from authoritative source on each container start.** Rejected because warm-up time exceeds the ACA cold-start budget; would also re-trigger every replica restart.

**References**: Azure Container Apps storage guide, Meilisearch persistence documentation.

---

## §4 — Audit-emit transport: CLI verb vs HTTP endpoint

**Decision**: **CLI verb in the existing admin/seed tool**, invoked from deploy workflows via a wrapper script that runs the CLI inside an existing backend container (`az containerapp exec`).

**Rationale**:

1. **Reuses spec 003's audit writer.** The CLI re-uses the same `IAuditLogWriter` and EF context that the production code paths use; zero duplicate code, zero schema drift risk.
2. **Survives backend HTTP outage.** The CLI talks to Postgres directly. If `/health` is failing, audit emission still works — exactly when we need audit most (the `deploy.completed_failed` event).
3. **No new public surface.** No new managed-identity-bound bearer-token issuance, no rate limiter, no new OpenAPI route to drift, no contract-diff coverage gap.
4. **Self-attribution.** The CLI calls `az account show` inside the executing container, captures the managed identity object id, and writes it into the audit row — satisfying Principle 25's actor-identity requirement directly.
5. **Testable.** Unit tests already exist for `IAuditLogWriter`; the CLI verb adds a thin CLI parser + DI wiring on top, ≤ 50 LOC.

**Alternatives considered**:

- **Managed-identity-bound HTTP endpoint** (`POST /admin/audit/emit`). Rejected because:
  - Adds a public-shaped surface that must be authenticated, rate-limited, contract-diffed, and load-tested.
  - Fails to emit audit when the backend itself is failing, which is the exact moment we need it.
  - Requires bearer-token plumbing in the deploy workflow on top of OIDC + managed identity (three layers of trust).
- **Direct SQL INSERT from the workflow** via `psql` over private endpoint. Rejected because it bypasses the audit-writer's schema invariants (e.g., field validation, side-effects on retention triggers); future schema migrations would orphan the workflow's INSERT.
- **GitHub Actions log as the audit-of-record.** Rejected because GitHub Actions log retention is GitHub-side, not under our 365-day retention contract; not queryable by SQL; not unified with application audit events.

**Implementation note**: The wrapper `scripts/azure/audit-emit.sh` runs:

```bash
az containerapp exec \
  --name ca-backend-api-<env> \
  --resource-group rg-dental-<env>-ksa \
  --command "dotnet AdminTool.dll audit-emit --event-type $TYPE --payload $PAYLOAD_JSON"
```

`az containerapp exec` requires the calling identity to have `Container Apps Contributor`, which the deploy federated identity already has. The CLI runs inside an already-warm container, so cold-start latency is not a factor.

**Bootstrap edge case**: Before the very first successful deploy, no backend container exists. The bootstrap deploy uses `--skip-audit-emit`; after first successful deploy, the flag is dropped. This is documented in the runbook and the bootstrap script's banner output.

**References**: Spec 003 audit_log_entries schema, Azure CLI `containerapp exec` documentation.

---

## §5 — Postgres backup strategy

**Decision**: **Geo-redundant 30-day backup**, point-in-time recovery (PITR) enabled, no cross-region active workloads.

**Rationale**:

- **Residency posture (ADR-010).** Active workloads remain in KSA Central; geo-backup writes to the paired region (Saudi Arabia North, currently in preview at the time of writing — fall back to UAE North only if SAN is not GA at provisioning time, with explicit documentation in the decisions log). Geo-backup is recovery-only; no read traffic ever goes to the paired region. KSA-resident personal data therefore is not actively processed outside KSA.
- **30-day retention** aligns with Azure Postgres Flexible Server's max default and gives a comfortable rollback window for data-corruption scenarios that are detected late.
- **PITR** allows arbitrary-second restoration within retention, which is the standard Azure Postgres flex affordance and costs nothing extra.
- **Quarterly restore drill** is documented in the runbook (§Disaster Recovery): restore the most recent backup to a sandbox flex server and run a smoke query. Drill outcomes logged in `infra/azure/RUNBOOK.md`'s exercise log.

**Alternatives considered**:

- **Locally redundant backup only.** Rejected because a regional outage would lose the entire backup chain.
- **Active-passive replica in paired region.** Out of scope at v1 per ADR-010 (single-region commitment). To be revisited only if a market or regulator imposes a multi-region active workload requirement.
- **Logical pg_dump shipped to Storage.** Operationally heavier; PITR + geo-redundant flex backup obviates it for v1.

**Residency review note**: At E1 acceptance, the platform engineer signs a one-line attestation: "Geo-redundant backup target region is `<region>` and is approved for KSA + EG personal-data residue per ADR-010." That attestation lives in `infra/azure/DECISIONS.md`.

**References**: Azure Database for PostgreSQL Flexible Server backup overview, ADR-010 in `CLAUDE.md`.

---

## Summary of resolutions

| # | Question | Decision | File reference |
|---|---|---|---|
| 1 | Bicep module structure | 16 modules + main + bootstrap | plan.md §"Bicep module structure" |
| 2 | OIDC subject claim shape | env-scoped for deploys, branch-scoped for drift | plan.md §"Federated identity setup" |
| 3 | Meilisearch persistence | Azure File | plan.md §"Bicep module structure" → meili.bicep |
| 4 | Audit-emit transport | CLI verb in admin/seed tool | plan.md §"Audit-event emission path" |
| 5 | Postgres backup | Geo-redundant 30-day + PITR | spec.md §Assumptions; runbook §DR |

All five resolutions are **decided** (no NEEDS CLARIFICATION remaining). Plan proceeds to Phase 1 (data-model + contracts + quickstart).
