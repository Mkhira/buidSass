# Implementation Plan: 029 — QA & Launch Hardening

**Branch**: `phase-1F-launch-hardening` | **Date**: 2026-05-10 | **Spec**: [spec.md](./spec.md)

## Summary

Spec 029 is **not a code-shipping spec.** It is a verification + sign-off + CI-promotion spec. Output is the Launch-Readiness Evidence Bundle and the merge-blocking promotion of the `impeccable-scan` workflow on `apps/admin_web`. The plan below sequences the QA pillars (regression → localization → RTL → security → reliability → performance → production smoke → container health → checklist sign-off → impeccable promotion) and identifies the small surface of infrastructure / config / scripting changes that DO ship in this spec.

## Technical Context

**Language/Version**: No new application code. Tooling-only:
- k6 (load tests, OSS Grafana k6).
- `gitleaks` (secret scan, OSS).
- Polly + WebApplicationFactory + Testcontainers Postgres (existing test stack — used for chaos test harnesses).
- Playwright (admin E2E) — existing CI dependency.
- `flutter integration_test` (mobile + web E2E) — existing CI dependency.
- Bash + jq + GitHub CLI (workflow orchestration).

**Primary Dependencies (new tooling sourced)**:
- Visual-regression tool: Percy (recommended default) — provider-cooperative for Flutter + Next.js.
- OWASP ASVS L1 control sheet — vendored under `docs/security/asvs-l1-controls.md` for offline reference.
- Provider sandbox failure-injection: per-provider documented (HyperPay's `force_5xx` test header, etc.). Sandbox-cooperation matrix lives in `evidence/reliability/provider-sandbox-matrix.md`.

**Storage**: No new tables. No migrations. **Zero schema changes.**

**Testing**: This spec IS the testing. The tests it executes are already shipped by spec 002 (testing strategy) + spec 015 (admin E2E) + per-module specs. New artifact: chaos test harnesses under `tests/chaos/` (Bash-orchestrated; no production deploy surface).

**Target Platform**: Staging ACA (default) for all pillars except the read-only Production smoke (User Story 7), which targets Production ACA.

**Project Type**: QA verification spec. No vertical slice. No new modules.

**Performance Goals**:
- Catalog browse p95 < 400 ms at 5× RPS.
- Search p95 < 600 ms at 5× RPS.
- Checkout p95 < 1500 ms at 5× RPS.
- Container rollback < 5 minutes per container.
- Post-rollback health < 30 s.

**Constraints**:
- BR-7 (residency): Production smoke MUST target only `Saudi Arabia Central`.
- BR-11 (no new features): Plan rejects any work item that introduces user-facing functionality.
- BR-14 (Production read-only): No write-path operations against Production from this spec.
- Risk 11: Arabic editorial reviewer must be named before User Story 2 begins.

**Scale/Scope**:
- 12 user stories (one per QA pillar + impeccable promotion).
- ≈ 9 evidence artifact families (regression / localization / RTL / security / reliability / performance / production-smoke / containers / launch-readiness / impeccable / dod-audit).
- Per-pillar runtime estimate: regression 8 h; localization 5 d (gated on reviewer availability); RTL 1 d; security 3 d; reliability 1 d (3 drills); performance 2 d; production-smoke 30 min; containers 4 h; checklist + impeccable 1 d. Total elapsed: ~10 working days assuming reviewer responsiveness.

## Constitution Check

| Principle | Posture | Status |
|---|---|---|
| 4 | Arabic editorial-grade enforced via named-reviewer per-surface sign-off (BR-2). | PASS |
| 7 | Brand palette enforced post-launch via merge-blocking impeccable-scan (BR-10). | PASS |
| 17 | Order / payment / fulfillment / refund state separation re-verified by regression (BR-1). | PASS |
| 24 | Every state machine across 1A–1E re-verified by regression + chaos drills (BR-5). | PASS |
| 25 | Every QA finding + sign-off + waiver captured in Evidence Bundle or `audit_log_entries` (BR-12). | PASS |
| 28 | Implementation-ready: every pillar enumerates owner, tooling, exit criterion, evidence. | PASS |
| 29 | All twelve required spec sections present. | PASS |
| 30 | Phase scoped 1F. No scope creep into 1.5. | PASS |
| 31 | Constitution supremacy: any QA finding conflicting with constitution opens a remediation issue and blocks 029 exit. | PASS |
| ADR-010 | Production smoke targets `Saudi Arabia Central` only (BR-7). | PASS |
| Guardrail #1 | Lint + format bar already green on `main`; 029 does not introduce new code requiring re-baselining. | PASS |
| Guardrail #2 | Contract diff already green; 029 does not change contracts. | PASS |
| Guardrail #3 | Standard fingerprint + constitution + ADR injection in this session. | PASS |
| Guardrail #4 | CODEOWNERS gains a line for `.github/workflows/impeccable-scan.yml` per User Story 10; no other CODEOWNERS edits. | PASS |

No violations.

## Project Structure

```
specs/phase-1F/029-qa-and-hardening/
├── spec.md
├── plan.md (this file)
├── tasks.md
├── research.md
├── data-model.md       (intentionally minimal — no new schema)
├── quickstart.md
├── contracts/
│   └── evidence-bundle-layout.md   (the only "contract" 029 has — the Bundle's directory shape)
└── checklists/
    └── requirements.md

evidence/                                                (NEW — top-level repo path)
├── README.md
├── regression/
│   └── regression-run-<DATE>.junit.xml + summary.md
├── localization/
│   └── <surface>-signoff-<DATE>.md  (× per-surface count)
├── rtl/
│   └── rtl-sweep-<platform>-<DATE>.html + screenshots-<platform>-<DATE>.zip
├── security/
│   ├── asvs-l1-<DATE>.md
│   ├── depscan-<DATE>.json
│   ├── gitleaks-<DATE>.json
│   ├── auth-fuzz-<DATE>.md
│   └── idor-sweep-<DATE>.md
├── reliability/
│   ├── chaos-payment-<DATE>.md
│   ├── chaos-shipping-<DATE>.md
│   ├── chaos-notification-<DATE>.md
│   └── provider-sandbox-matrix.md
├── performance/
│   ├── k6-catalog-<DATE>.json
│   ├── k6-search-<DATE>.json
│   └── k6-checkout-<DATE>.json + grafana-snapshot.png
├── production-smoke/
│   └── smoke-<DATE>.md
├── containers/
│   ├── health-backend_api-<DATE>.md
│   ├── health-admin_web-<DATE>.md
│   └── health-flutter_web-<DATE>.md
├── dod-audit/
│   └── dod-audit-<DATE>.md
├── impeccable/
│   └── promotion-<DATE>.md
└── launch-readiness/
    └── launch-readiness-<DATE>.md   ← signed authorization document

tests/chaos/                                              (NEW)
├── README.md
├── payment/
│   ├── inject-hyperpay-5xx.sh
│   ├── inject-paymob-webhook-outage.sh
│   └── verify-dead-letter.sh
├── shipping/
│   ├── inject-aramex-webhook-outage.sh
│   └── verify-timeout-reconciler.sh
└── notification/
    ├── inject-ses-bounce.sh
    └── verify-delivery-log.sh

docs/security/
└── asvs-l1-controls.md                                   (NEW — vendored OWASP ASVS L1 control sheet)

scripts/qa/                                               (NEW)
├── rtl-visual-sweep.sh
├── run-regression.sh
├── run-k6-stepped.sh
└── verify-section-13-checklist.sh

.github/workflows/
└── impeccable-scan.yml                                   (MODIFIED — advisory exit removed; threshold check added)

.impeccable/
└── thresholds.json                                       (MODIFIED — mode: advisory → enforced)

CODEOWNERS                                                (MODIFIED — add line for impeccable-scan.yml)

.specify/
└── feature.json                                          (MODIFIED — phase-1D pointer → phase-1F/029)
```

**No changes** under `services/backend_api/`, `apps/admin_web/`, or `apps/customer_flutter/` — except as required to remediate findings surfaced by the QA pillars (those remediations are tracked as separate issues, NOT inside this spec's commit history).

## Pillar Sequencing

The 10 user stories execute in a partially-parallel order:

```
Day 1:  US-1 (regression, kicked off)        + US-7 (production smoke, parallelizable)
Day 1:  Risk 11 verification (gate for US-2)
Day 2:  US-3 (RTL sweep) + US-4 (security pass starts)
Day 3:  US-5 (chaos drills) + US-6 (k6 starts)
Day 4:  US-6 (k6 continues) + US-2 (localization, rolling)
Day 5:  US-8 (container health + rollback rehearsal)
Day 6:  US-10 (impeccable rehearsal PR opened)
Day 7:  US-10 (impeccable threshold-flip merged after rehearsal)
Day 8:  US-2 finalization (consolidated localization sign-off)
Day 9:  US-9 (Section 13 walk + Evidence Bundle finalization)
Day 10: Launch authorization signing
```

Parallelization is intentional. Localization (US-2) is the long-pole because it depends on a human reviewer; that's why it kicks off mid-window and finalizes on day 8.

## Risk Register Touchpoints

| Risk # | Mitigation in this spec |
|---|---|
| 1 (AI-agent drift) | Lint + format bar green is checked as part of regression sweep entry criteria. |
| 11 (Arabic editorial reviewer) | Treated as external prerequisite (Q4 + BR-2). 029 cannot start US-2 work until reviewer is in the Risk Register. |
| 7 (residency breach at cutover) | Production smoke (US-7) verifies `/health` from KSA-Central host only; ADR-010 region check repeated in the Section 13 Compliance walk. |
| 8 (vendor-drift) | DoD audit (FR-018) re-verifies multi-vendor-readiness checkbox on every 1A–1E module. |
| 3 (payment race conditions) | Chaos drill on payment (US-5) re-exercises idempotency + dead-letter + reconciliation. |
| 9 (solo-dev burnout) | 029 timeline (~10 working days) is park-safe — every pillar produces an independently-mergeable evidence artifact, so partial progress always moves the launch checklist forward. |

## Phasing inside 029

029 has internal phases. Tasks.md uses these phase headings:

- **Phase 0 — Setup + Risk-11 verification + tooling acquisition**
- **Phase 1 — DoD audit (BR-1)**
- **Phase 2 — Functional regression (US-1)**
- **Phase 3 — Localization + RTL (US-2 + US-3)**
- **Phase 4 — Security pass (US-4)**
- **Phase 5 — Reliability chaos + reconciliation rerun (US-5)**
- **Phase 6 — Performance k6 (US-6)**
- **Phase 7 — Production smoke + container health + rollback rehearsal (US-7 + US-8)**
- **Phase 8 — `impeccable-scan` promotion (US-10)**
- **Phase 9 — Section 13 walk + Evidence Bundle finalization + launch authorization (US-9)**

## What 029 Does NOT Do

- Does not introduce new product features (BR-11).
- Does not change `services/backend_api/`, `apps/admin_web/`, or `apps/customer_flutter/` source code (except as needed to remediate findings — and those land in separate fix PRs, not in this spec's commit).
- Does not modify any database schema. Zero new migrations.
- Does not run penetration testing (out of scope; Phase 1.5).
- Does not run cross-region BCDR rehearsal (out of scope; ADR-010 single-region posture).
- Does not run write-path Production smoke (BR-14 — read-only only).
- Does not author marketing-site or blog Arabic content (out of platform scope).
