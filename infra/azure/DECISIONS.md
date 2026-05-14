# Infrastructure Decisions — E1 (Phase 1E)

**Spec**: [`../../specs/phase-1E/E1-infrastructure-integration/spec.md`](../../specs/phase-1E/E1-infrastructure-integration/spec.md)
**Plan**: [`../../specs/phase-1E/E1-infrastructure-integration/plan.md`](../../specs/phase-1E/E1-infrastructure-integration/plan.md)
**Last reviewed**: 2026-05-14

This is the canonical log of decisions that govern the Azure runtime for spec 028 (E1) and its
downstream consumers (specs 025 / 026 / 027 / 029). Each entry records the decision, its source,
the rationale, the alternatives considered, and (where applicable) the reversibility cost.

Two categories live here:

1. **Clarify-locked decisions** (five) — settled in `clarifications.md` during the spec phase. These
   are stable; revisiting requires a spec amendment per Principle 32.
2. **Deferred-default decisions** (three) — sensible defaults chosen by E1 in the absence of an
   explicit clarification, with explicit re-evaluation triggers documented.

A separate residency-clearance attestation block (per BR-1 carve-out) closes the document.

---

## §1 — Clarify-locked decisions

### D-1 — Meilisearch self-hosted on Azure Container Apps

| Field | Value |
|---|---|
| **Decision** | Run Meilisearch as a self-hosted container app (`ca-meili-<env>`) inside the same ACA managed environment as the backend. Persist the index via an Azure File share volume mount (`vol-meili-<env>`); master key sourced from Key Vault. |
| **Source** | `specs/phase-1E/E1-infrastructure-integration/clarifications.md` Q-MEILI-1 (clarify-locked) |
| **Rationale** | ADR-005 picked Meilisearch for Arabic normalization + typo tolerance + simple ops. There is no first-party managed Meilisearch on Azure. Self-hosting on ACA gives us: (a) same VNet as backend (no internet hop), (b) reuse of the ACA platform we already pay for, (c) volume-snapshot disaster-recovery story, (d) bounded blast radius via the per-env managed identity. |
| **Alternatives** | Azure AI Search (closed-source, different feature shape, would require rewriting all spec 006 search code), Meilisearch Cloud (no KSA region, breaks ADR-010), self-hosted on AKS (introduces a second compute platform — wasteful for one workload). |
| **Reversibility cost** | Migrating to Azure AI Search would be a full search-stack rewrite (spec 006 surface). Moving to Meilisearch Cloud once a KSA region exists is mechanical (point `MEILI_HOST` env var; rebuild indexes). |

### D-2 — Flutter customer web on Azure Static Web Apps

| Field | Value |
|---|---|
| **Decision** | Host the compiled Flutter customer web bundle on Azure Static Web Apps (`swa-customer-flutter-<env>`, SKU Standard, location `westeurope`). |
| **Source** | `clarifications.md` Q-WEB-HOST-1 (clarify-locked) |
| **Rationale** | The Flutter web bundle is a fully-static artifact (no SSR). SWA provides: built-in TLS, global CDN, route fallback for SPA, free SSL, deeper GitHub Actions integration than ACA. Operating cost is ~ 1/10th of an always-warm ACA replica. |
| **Alternatives** | ACA container (overkill for static), Azure Storage static website + Front Door (cheaper but more glue), Cloudflare Pages (off-Azure, fragments ADR-010 attestation surface). |
| **Reversibility cost** | Low. Migrating to ACA container hosting (e.g., nginx static) is mechanical: build the bundle into a tiny image, repoint the SWA DNS CNAME. Documented as the long-term option once SWA hits a KSA region OR if SWA pricing changes materially. |
| **Region** | `westeurope` (SWA is GA in only a small set of regions; `westeurope` is the closest GA region to KSA / Egypt). Latency to KSA is acceptable because static content is served via the Azure CDN edge nodes. KSA residency does NOT apply to non-personal compiled static assets — see §3 (residency-clearance attestation). |

### D-3 — Postgres compute size — `Standard_D2s_v3`

| Field | Value |
|---|---|
| **Decision** | Postgres Flexible Server SKU `Standard_D2s_v3` (2 vCPU, 8 GB RAM) at launch for BOTH Staging and Production. |
| **Source** | `clarifications.md` Q-PG-SIZE-1 (clarify-locked) |
| **Rationale** | The largest table at launch (`audit_log_entries`) is write-mostly and append-heavy. 2 vCPU + 8 GB is sized for ~ 500 concurrent connections and ~ 1k tps on a write-mostly workload — well above the expected launch traffic. Vertical scaling to `D4s_v3` is online and reversible. |
| **Alternatives** | `B1ms` Burstable (rejected — Production traffic risk), `D4s_v3` (rejected — premature; cost 2x for headroom we don't need yet). |
| **Reversibility cost** | Online compute scale; no downtime, no data migration. Re-evaluate quarterly using Azure Monitor CPU + connection-pool metrics. |

### D-4 — Manual drift remediation (auto-remediation forbidden at v1)

| Field | Value |
|---|---|
| **Decision** | `infra-drift.yml` detects drift, emits `infra.drift.detected` audit event, fires `alert-drift-<env>` alert, and **stops**. Manual remediation by an on-call engineer (PIM-elevated). No auto-revert. |
| **Source** | `clarifications.md` Q-DRIFT-1 (clarify-locked) |
| **Rationale** | Auto-remediation at v1 risks reverting a legitimate manual hotfix made during an incident. The cost of P1 caused by reverting a legit hotfix > the cost of a 24-h human-in-the-loop remediation cycle. Re-evaluate post-launch once drift events are characterized. |
| **Alternatives** | Auto-revert via `az deployment sub create` on detection (rejected — see above), Terraform Cloud-style policy enforcement (rejected — not in stack). |
| **Reversibility cost** | Trivial. Adding an `auto-remediate` job to `infra-drift.yml` is a 30-line change once we have data. |

### D-5 — Production deploys require 2-of-N approvers (`ProductionDeployers` team)

| Field | Value |
|---|---|
| **Decision** | GitHub Environment `production` is gated on the `ProductionDeployers` team with `required_approvers = 2` and `prevent_self_review = true`. `deploy-production.yml` is `workflow_dispatch`-only (never auto on `push`). |
| **Source** | `clarifications.md` Q-PROD-APPROVAL-1 (clarify-locked) |
| **Rationale** | Production is the only environment that handles real PII (Principle 5 markets). A single approver is insufficient against a compromised account or a coerced engineer. 2-of-N + self-review prevention is the industry-standard two-person-rule for prod releases. |
| **Alternatives** | 1-of-N (rejected — see above), 3-of-N (rejected — operational drag during incidents; revisit at 5+ team members). |
| **Reversibility cost** | Trivial. Edit Environment settings + RUNBOOK.md. |

---

## §2 — Deferred-default decisions

### DD-1 — Alert channels: email + Microsoft Teams webhook

| Field | Value |
|---|---|
| **Decision** | Action group `ag-oncall-<env>` delivers all four alert paths via (a) the platform-eng distribution list and (b) a Microsoft Teams webhook into `#dental-platform-alerts`. No SMS / PagerDuty at v1. |
| **Source** | E1 default in the absence of an explicit clarification (`research.md` §6) |
| **Re-evaluation trigger** | Any P1 incident where the alert was acknowledged > 15 minutes late, OR team grows beyond 6 on-call engineers. |
| **Cost to switch** | Adding PagerDuty / Opsgenie webhook = one action-group receiver added (≤ 5 lines in `alerts.bicep`). |

### DD-2 — Backend secret-cache TTL: 5 minutes

| Field | Value |
|---|---|
| **Decision** | `AddLayeredConfiguration()` caches Key Vault secret values for 5 minutes (spec 003 default). Rotation propagates within 5 minutes without restart (verified by AC-18). |
| **Source** | E1 default; spec 003 owns the implementation. |
| **Re-evaluation trigger** | Any rotation incident where the 5-min window was operationally painful, OR provider webhook signing keys with sub-5-min rotation cadence are introduced. |
| **Cost to switch** | Configuration knob in `appsettings.json` and `AddLayeredConfiguration()`. |

### DD-3 — Postgres major version: 16

| Field | Value |
|---|---|
| **Decision** | Postgres Flexible Server `version = 16`. Upgrade procedure documented in RUNBOOK §"Postgres major-version upgrade" (logical-replica + cutover). |
| **Source** | E1 default; latest GA when this spec was written, aligned with EF Core 9 + Npgsql 9 support matrix. |
| **Re-evaluation trigger** | Postgres 17 GA stabilizes (typically 6 months post-release). |
| **Cost to switch** | 4–6 hours wall-clock for a logical-replica cutover with negligible downtime. |

---

## §3 — Residency-clearance attestation (BR-1 carve-out)

The Flutter customer web bundle hosted on `swa-customer-flutter-<env>` is non-personal compiled
static content; KSA PDPL and Egypt Law 151/2020 localization clauses do not apply to this artifact
at rest. Approved by `@Mkhira` on `2026-05-14`.

The attestation covers only the static-asset hosting surface. ALL personal-data processing
(database, application logs containing PII, search indexes containing customer-identifying
information) remains in `ksacentral` per ADR-010. The Flutter web bundle ITSELF contains no PII
(it is JavaScript / WASM compiled from Dart source); customer data is fetched at runtime from the
backend API hosted in `ksacentral` and is never persisted to SWA storage.

---

## §4 — Change log

| Date | Author | Change |
|---|---|---|
| 2026-05-14 | @Mkhira | Initial DECISIONS.md — five clarify-locked + three deferred-default + SWA residency attestation. |
