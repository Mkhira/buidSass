# Research: 029 — QA & Launch Hardening

**Phase**: 0
**Date**: 2026-05-10

## §1 — Why Staging-primary + Production read-only smoke (Q1)

**Decision**: Run every active QA pillar on Staging ACA. Production receives only read-only probes: `/health`, `/version`, and `seed --mode=dry-run` (which by definition writes nothing).
**Rationale**: Production at Milestone 9 is at customer-zero. Any write-path activity (seeded test order, test promotion, test review) pollutes `audit_log_entries`, payment-provider sandbox-vs-production bookkeeping, and notification logs in ways that are hard to undo. Staging carries the full ACA stack at parity per E1, with the same Postgres + Meilisearch + provider-sandbox topology.
**Alternatives**: (a) Production write-path smoke — rejected, audit-pollution + provider-bookkeeping risk; (b) Staging-only with no Production touch — rejected, production-readiness cannot be validated without at least the read-only floor; (c) ephemeral preview environment — rejected, doesn't carry KV, ACA, or provider sandbox parity at the time we need this.

## §2 — Why stepped 1× → 3× → 5× ramp shape for k6 (Q2)

**Decision**: Each k6 scenario ramps 1× → 3× → 5× of expected launch RPS over 60 minutes total, holds at 5× for 15 minutes, asserts p95 budgets at every step. Per-step rampUp = 15 min; per-step hold = 5 min before next ramp.
**Rationale**: Implementation-plan task #6 specifies "5× expected launch RPS" but does not specify shape. Stepped ramps surface threshold failures (e.g., connection-pool exhaustion at 3× when capacity-planning predicted 5× headroom) that flat-load tests miss because they don't isolate the failure point. A 15-minute hold at 5× is long enough for slow-warm caches (e.g., Meilisearch index segments, EF Core query plans) to hit steady state.
**Alternatives**: (a) Flat 5× hold for 30 min — rejected, hides intermediate threshold failures; (b) Spike-and-hold (0× → 5× instant) — rejected, unrealistic and tests autoscaling more than steady-state SLO; (c) Soak test (1× hold for 24 h) — rejected, not what task #6 asked for; reserve for Phase 1.5 capacity planning.
**Budgets** (per Stage 7 SLO targets):
- Catalog browse: p95 < 400 ms.
- Search: p95 < 600 ms (Meilisearch hot path).
- Checkout: p95 < 1500 ms (covers 010 + 012 + 027 sandbox call).

## §3 — Why live-sandbox chaos vs. stub-only (Q3)

**Decision**: Live sandbox provider failure injection by default, with stub fallback for any provider that refuses sandbox failure cooperation.
**Rationale**: Specs 025 / 026 / 027 explicitly designed dead-letter queues + reconciliation workers + retry policies — none of those wiring details are exercised by stub-only tests. Live sandbox chaos catches: webhook signature drift after a provider SDK update, retry-window edge cases that don't manifest in unit tests, dead-letter routing misconfiguration in production wiring (Hangfire queue names, KV slot name typos, etc.).
**Provider-cooperation matrix** (drafted at planning time; fixed before execution):
- HyperPay (KSA cards): supports `force_5xx` test header. Live chaos.
- Tap (KSA cards backup): supports forced-decline test cards. Live chaos.
- Paymob (EG cards): supports webhook-suppression test mode. Live chaos.
- Kashier (EG backup): no documented chaos hook — **stub fallback** (Risk Register entry).
- Tabby (KSA BNPL): supports decline + redirect-timeout test customers. Live chaos.
- Tamara (KSA BNPL): supports decline test customers. Live chaos for decline; stub fallback for webhook-outage scenarios.
- Valu (EG BNPL): no documented chaos hook — **stub fallback** (Risk Register entry).
- Aramex / SMSA / Mylerz (shipping): contact provider integration team for sandbox failure-injection capabilities.
- SES / Unifonic / FCM (notification): SES bounce simulator + Unifonic sandbox failures + FCM invalid-token simulation all available.

**Alternatives**: (a) Stub-only — rejected, misses production-wiring bugs; (b) Production chaos — rejected, customer-impacting; (c) Wait for full chaos-engineering platform (Litmus / Chaos Mesh) — rejected, scope creep beyond Phase 1F.

## §4 — Why rolling per-surface localization sign-off (Q4)

**Decision**: Arabic editorial reviewer signs off per-surface (mobile / web / admin / email / push / SMS / PDF-invoice / PDF-tax / CMS) on a rolling basis, with a final consolidated sign-off line gating exit.
**Rationale**: Risk 11 calls the reviewer out as the launch-blocking long-pole. Single-batch sign-off models would serialize the bottleneck at the end of Milestone 9; rolling sign-off lets early surfaces (e.g., transactional emails, which tend to have tighter copy) finalize while later, copy-heavy surfaces (e.g., CMS legal pages) work through revisions in parallel. The final consolidated line preserves auditability and gives a clear "all surfaces done" gate.
**Alternatives**: (a) Single batch — rejected, serial bottleneck; (b) Per-string sign-off — rejected, too granular, would create thousands of micro-approvals; (c) Sample-based sign-off (10 % of strings) — rejected, fails Principle 4's editorial-grade bar.

## §5 — Why throwaway-PR rehearsal for impeccable-scan promotion (Q5)

**Decision**: Open `chore/impeccable-enforcement-rehearsal` as a draft PR that intentionally breaches a P1 budget. Verify red check + waiver-label override + label-removal re-lock cycle. Only after that rehearsal passes does the threshold-flip PR (`.impeccable/thresholds.json` + workflow YAML edit) merge to `main`.
**Rationale**: Implementation-plan task #10 explicitly says "Dry-run on a throwaway PR." Direct-to-`main` flips have caused merge freezes in prior phases (1C scaffold work); the rehearsal PR is cheap insurance. The rehearsal PR's check history also serves as evidence that the waiver flow works — if a real PR later needs a waiver, we have a known-good runbook.
**Alternatives**: (a) Direct merge to `main`, unwind via revert if needed — rejected, freeze risk; (b) Feature-flag the enforcement (env var) — rejected, adds permanent complexity for one-time operation.

## §6 — Why a top-level `evidence/` directory (vs. wiki / external doc repo)

**Decision**: Default to a top-level `evidence/` directory in this repository. Operations Lead may override to a dedicated docs repo if asset size becomes prohibitive (e.g., RTL screenshot ZIPs > 100 MB).
**Rationale**: Evidence Bundle is repo-adjacent so commit SHA → evidence link traceability is one-step. Git history of the Bundle itself is auditable. Image-heavy artifacts (RTL screenshots, k6 Grafana snapshots) can be linked rather than committed if size is a concern, with the link source committed.
**Alternatives**: (a) GitHub Wiki — rejected, no commit-SHA traceability; (b) S3 + linked URLs — rejected, residency posture (would need to be Azure Blob in KSA-Central per ADR-010); (c) Dedicated docs repo — viable, deferred to Operations Lead choice at execution time.

## §7 — Section 13 checklist line → owner mapping

Pre-computed mapping (lives in `evidence/launch-readiness/launch-readiness-template.md` as a starter; Launch Captain may adjust):

| Block | Line | Default Owner |
|---|---|---|
| Product | All Phase 1 scope functional | Product Lead |
| Product | Market rules reviewed | Product Lead + Compliance |
| Product | Restricted-product logic verified | QA Lead |
| Product | Returns/refunds operational | Operations Lead |
| Product | B2B quote → order tested | QA Lead |
| Engineering | Staging stable | Engineering Lead |
| Engineering | Migrations repeatable on clean DB | Engineering Lead |
| Engineering | Backup + restore verified in-region | Operations Lead |
| Engineering | Secrets managed (no in-repo) | Security Lead |
| Engineering | Rate limits configured | Security Lead |
| Engineering | ADRs 001–006 + 010 Accepted | Engineering Lead |
| Engineering | Lint + format green on `main` | Engineering Lead |
| Engineering | Contract tests green on `main` | Engineering Lead |
| Engineering | CODEOWNERS enforcement verified | Security Lead |
| Integrations | Payment providers live-ready | Engineering Lead |
| Integrations | Shipping providers tested | Engineering Lead |
| Integrations | OTP/SMS delivered to test numbers | Operations Lead |
| Integrations | Email delivers Arabic correctly | QA Lead |
| Integrations | Push verified Android + iOS | QA Lead |
| Integrations | PDF invoices correct in Ar + En | QA Lead |
| Operations | Catalog loaded | Operations Lead |
| Operations | Support team trained | Operations Lead |
| Operations | Admin roles assigned | Security Lead |
| Operations | Verification SOP ready | Operations Lead |
| Operations | Refund SOP ready | Operations Lead |
| Operations | Order-ops SOP ready | Operations Lead |
| QA | Full regression passed | QA Lead |
| QA | Arabic editorial QA passed | Arabic Editorial Reviewer |
| QA | Web + mobile smoke tests | QA Lead |
| QA | Admin permissions matrix tested | Security Lead |
| Compliance | KSA PDPL passed | Compliance Lead |
| Compliance | Egypt Law 151/2020 passed | Compliance Lead |
| Compliance | Egypt VAT format verified by accountant | Compliance Lead |
| Compliance | Legal pages in Ar + En reviewed | Compliance Lead |
| Compliance | KSA-Central confirmed; no out-of-region | Engineering Lead + Compliance |
| Monitoring | Uptime monitor live | Operations Lead |
| Monitoring | Error tracking active | Engineering Lead |
| Monitoring | Structured logs accessible | Engineering Lead |
| Monitoring | Payment-failure alerts firing | Operations Lead |

## §8 — DoD audit method (FR-018, BR-1)

**Method**: For each Phase 1A–1E spec, walk Section 11's 18 checkboxes. For each checkbox, record `pass | fail | N/A with rationale`. Failures open as `phase-1f-blocker`. Audit captured in `evidence/dod-audit/dod-audit-<DATE>.md` as a single matrix (specs × checkboxes).

**Specs in scope**:
- Phase 1A: 001 (governance), 002 (architecture), 003 (shared-foundations).
- Phase 1B: 004 (identity), 005 (catalog), 006 (search), 007-a (pricing-engine), 010 (cart-checkout), 011 (order), 012 (tax-invoice), 013 (return-refund).
- Phase 1C: 015 (admin shell + per-module surfaces), 014 (customer-storefront), 016 (customer-mobile).
- Phase 1D: 020 (verification), 021 (B2B), 007-b (promotions UX), 022 (reviews), 023 (support), 024 (CMS).
- Phase 1E: E1 (infrastructure), 025 (notifications), 026 (shipping), 027 (payments).

**Approximate count**: 22 specs × 18 checkboxes = 396 cells. Most cells are `pass`; failures are the work item.

## §9 — Performance baseline numbers (where do RPS targets come from?)

Expected launch RPS targets (assumption — Operations Lead validates at execution time):
- Catalog browse: 50 RPS (steady) + 200 RPS peak hour. **5× peak = 1000 RPS** load test target.
- Search: 30 RPS (steady) + 100 RPS peak. **5× = 500 RPS**.
- Checkout: 5 RPS (steady) + 20 RPS peak. **5× = 100 RPS**.

These are **assumptions** based on a Day-1 launch in two markets (KSA + EG) with limited paid acquisition. Operations Lead validates against the marketing launch plan at execution time and adjusts if actual targets differ. Targets are recorded in `evidence/performance/rps-baseline-<DATE>.md` before the k6 runs begin.

## §10 — Tooling acquisition checklist

Sourced during Phase 0 setup tasks, verified before Phase 2 starts:

- [ ] k6 CLI installed on QA workstation.
- [ ] k6 dashboard backend configured (Grafana k6 Cloud or self-hosted Grafana).
- [ ] `gitleaks` CLI installed.
- [ ] OWASP ASVS L1 control sheet vendored at `docs/security/asvs-l1-controls.md`.
- [ ] Provider sandbox accounts confirmed live for: HyperPay, Tap, Paymob, Kashier, Tabby, Tamara, Valu, Aramex, SMSA, Mylerz, SES, Unifonic, FCM.
- [ ] Percy account configured for Flutter + Next.js visual regression.
- [ ] `flutter integration_test` runner working on macOS + Linux CI.
- [ ] Playwright admin E2E suite green on Staging.
- [ ] Postman backend-API collection green on Staging.
