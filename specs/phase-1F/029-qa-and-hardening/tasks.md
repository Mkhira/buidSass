# Tasks: 029 — QA & Launch Hardening

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/evidence-bundle-layout.md](./contracts/evidence-bundle-layout.md)
**Phase**: 1F — Launch Hardening · Milestone 9
**Created**: 2026-05-10

Legend: `[P]` = can run in parallel with siblings; `[B]` = blocking — must complete before next phase. Each task lists the user-story (`US-N`) and acceptance-criterion / success-criterion it satisfies.

---

## Phase 0 — Setup, Risk-11 verification, tooling acquisition, feature pointer

- [ ] **T001 [B]** Verify Risk 11 resolved in `docs/risks/risk-register.md` (Arabic editorial reviewer named). If unresolved, **STOP** and escalate to Product Lead. Source: spec.md §Edge Cases + FR-021. (Gate for US-2 / SC-2.)
- [ ] **T002 [P]** Bootstrap Evidence Bundle: `mkdir -p evidence/{regression,localization,rtl,security,reliability,performance,production-smoke,containers,dod-audit,impeccable,launch-readiness}`; copy `contracts/evidence-bundle-layout.md` to `evidence/README.md`; commit `chore: bootstrap launch-readiness evidence bundle`. Satisfies FR-001.
- [ ] **T003 [P]** Advance SpecKit pointer: write `.specify/feature.json` with `{"feature_directory":"specs/phase-1F/029-qa-and-hardening"}`; commit `chore(specify): advance feature pointer to phase-1F/029`. Satisfies FR-022 / SC-13.
- [ ] **T004 [P]** Vendor OWASP ASVS L1 control sheet at `docs/security/asvs-l1-controls.md` (download from OWASP, pin version, commit). Satisfies tooling dependency for US-4.
- [ ] **T005 [P]** Install + verify k6 CLI on the QA workstation; configure k6 dashboard backend (Grafana k6 Cloud or self-hosted). Satisfies tooling dependency for US-6.
- [ ] **T006 [P]** Install + verify `gitleaks` CLI. Satisfies tooling dependency for US-4 (secret scan).
- [ ] **T007 [P]** Confirm provider sandbox accounts active for: HyperPay, Tap, Paymob, Kashier, Tabby, Tamara, Valu, Aramex, SMSA, Mylerz, SES, Unifonic, FCM. Capture results in `evidence/reliability/provider-sandbox-matrix.md`. Satisfies tooling dependency for US-5.
- [ ] **T008 [P]** Configure Percy (or chosen visual-regression tool) for Flutter mobile + Flutter web + Next.js admin. Satisfies tooling dependency for US-3.
- [ ] **T009 [P]** Author + commit chaos drill harnesses under `tests/chaos/{payment,shipping,notification}/` per plan.md §Project Structure. Required scripts (each invoked by exactly one Phase 5 task — see traceability below): `payment/inject-hyperpay-5xx.sh` + `payment/verify-dead-letter.sh` (T031), `shipping/inject-aramex-webhook-outage.sh` + `shipping/verify-timeout-reconciler.sh` (T032), `notification/inject-ses-bounce.sh` + `notification/verify-delivery-log.sh` (T033). Optional secondary harnesses (`payment/inject-paymob-webhook-outage.sh`, etc.) MAY be authored for re-runs / EG-market parity but are NOT invoked by the gating drills. Plus `tests/chaos/README.md`. Satisfies tooling dependency for US-5.
- [ ] **T010 [P]** Author + commit QA scripts under `scripts/qa/`: `run-regression.sh`, `rtl-visual-sweep.sh`, `run-k6-stepped.sh`, `run-dod-audit.sh`, `sample-container-health.sh`, `rehearse-rollback.sh`, `verify-section-13-checklist.sh`, `build-localization-inventory.sh`, `auth-fuzz-runbook.md`, `idor-runbook.md`. Satisfies tooling dependency for US-1, US-3, US-4, US-6, US-8.

## Phase 1 — DoD audit (BR-1 / FR-018 / SC-11)

- [ ] **T011 [B]** Run `./scripts/qa/run-dod-audit.sh` against every Phase 1A–1E spec (22 specs × 18 checkboxes). Output: `evidence/dod-audit/dod-audit-<DATE>.md`. Satisfies SC-11.
- [ ] **T012 [B]** For each `fail` cell in T011's matrix, open a `phase-1f-blocker` issue tagged with the failing module. **029 cannot proceed past Phase 1 while any `phase-1f-blocker` is open.** Satisfies BR-1 / SC-12.
- [ ] **T013** Once all `phase-1f-blocker`s closed, mark DoD-audit artifact `status: pass` in frontmatter; sign off (Engineering Lead). Updates SC-11.

## Phase 2 — Functional regression (US-1 / SC-1)

- [ ] **T014 [B]** Run `./scripts/qa/run-regression.sh staging`; output `evidence/regression/regression-run-<DATE>.junit.xml` + `regression-run-<DATE>.summary.md`. Satisfies US-1 AC-1 / AC-2.
- [ ] **T015 [B]** Triage P0 / P1 failures; open `phase-1f-blocker` per failure with linked test + reproduction steps. Satisfies US-1 AC-3.
- [ ] **T016 [B]** Re-run regression after remediation; replace prior artifact with green run; sign off (QA Lead). Satisfies US-1 AC-4 / SC-1.
- [ ] **T017** Verify regression-run summary captures customer + admin + B2B coverage explicitly (per Section 8 user-story matrix). Satisfies FR-002.

## Phase 3 — Localization (US-2) + RTL (US-3)

### Localization (US-2 / SC-2 / BR-2 / BR-15)

- [ ] **T018 [B]** Run `./scripts/qa/build-localization-inventory.sh > evidence/localization/inventory-<DATE>.md`; hand to named Arabic editorial reviewer. Satisfies US-2 AC-1.
- [ ] **T019 [P]** For each surface in {`mobile-app`, `web-storefront`, `admin-dashboard`, `transactional-email`, `push-notification`, `sms-template`, `pdf-invoice`, `pdf-tax-receipt`, `cms-legal-pages`, `cms-faq`, `cms-blog`}: reviewer flags issues; Engineering opens `loc-ar-blocker` per flagged issue; remediates; reviewer re-reviews; per-surface sign-off captured at `evidence/localization/<surface>-signoff-<DATE>.md`. (11 sub-tasks, runnable in parallel.) Satisfies US-2 AC-2 / AC-3 / FR-003.
- [ ] **T020 [B]** Once all 11 per-surface sign-offs captured, author consolidated sign-off at `evidence/localization/consolidated-signoff-<DATE>.md` with reviewer's final approval line. Satisfies US-2 AC-4 / SC-2 / BR-15.

### RTL visual regression (US-3 / SC-3 / BR-3)

- [ ] **T021 [P]** Run `./scripts/qa/rtl-visual-sweep.sh staging`; output `evidence/rtl/rtl-sweep-{mobile,web,admin}-<DATE>.html` + screenshot ZIPs. Sweep covers `ar-SA` + `ar-EG`. Satisfies US-3 AC-1 / FR-004.
- [ ] **T022 [B]** Triage P0 / P1 visual regressions; open `rtl-blocker` per flagged issue; remediate; re-sweep. Satisfies US-3 AC-2.
- [ ] **T023 [B]** Replace prior sweep artifact with green run; sign off (QA Lead). Satisfies US-3 AC-3 / SC-3.

## Phase 4 — Security pass (US-4 / SC-4 / BR-4)

- [ ] **T024 [P]** Dependency scan: `dotnet list services/backend_api package --vulnerable --include-transitive > evidence/security/depscan-dotnet-<DATE>.txt`; `pnpm --filter ./apps/admin_web audit --audit-level=moderate --json > evidence/security/depscan-admin-<DATE>.json`; `flutter pub outdated --mode=null-safety -C apps/customer_flutter > evidence/security/depscan-flutter-<DATE>.txt`. Satisfies FR-006.
- [ ] **T025 [P]** Secret scan: `gitleaks detect --redact --no-git -s . --report-path evidence/security/gitleaks-<DATE>.json`. Satisfies FR-007.
- [ ] **T026 [P]** OWASP ASVS L1 walk: copy `docs/security/asvs-l1-controls.md` to `evidence/security/asvs-l1-<DATE>.md`; manually annotate every control with `implemented` or `N/A with documented rationale`. Satisfies US-4 AC-4 / FR-005.
- [ ] **T027 [P]** Auth fuzzing per `scripts/qa/auth-fuzz-runbook.md`: replay, signature stripping, JWT alg-confusion, refresh-token reuse, MFA bypass against spec 004's endpoints. Output `evidence/security/auth-fuzz-<DATE>.md`. Satisfies US-4 AC-2 / FR-008.
- [ ] **T028 [P]** IDOR sweep per `scripts/qa/idor-runbook.md` against every per-tenant / per-company / per-user endpoint across Phase 1A–1E. Output coverage matrix at `evidence/security/idor-sweep-<DATE>.md`. Coverage ≥ 95 %; zero successful unauthorized access. Satisfies US-4 AC-3 / FR-009.
- [ ] **T029 [B]** Triage findings: any High/Critical CVE / secret / auth bypass / IDOR access opens `security-blocker`; remediate; re-run the affected scan. Satisfies US-4 AC-1 / SC-4.
- [ ] **T030 [B]** Sign off on security pass (Security Lead) once zero blockers remain. Satisfies BR-4.

## Phase 5 — Reliability chaos + reconciliation rerun (US-5 / SC-5 / BR-5)

- [ ] **T031 [B]** Payment chaos: run `tests/chaos/payment/inject-hyperpay-5xx.sh staging` + `verify-dead-letter.sh`; capture in `evidence/reliability/chaos-payment-<DATE>.md`. Verify retry exhaustion → dead-letter. Satisfies US-5 AC-1 / FR-010.
- [ ] **T032 [B]** Shipping chaos: run `tests/chaos/shipping/inject-aramex-webhook-outage.sh staging` + `verify-timeout-reconciler.sh`; capture in `evidence/reliability/chaos-shipping-<DATE>.md`. Verify timeout reconciler ages out orphaned rows. Satisfies US-5 AC-2.
- [ ] **T033 [B]** Notification chaos: run `tests/chaos/notification/inject-ses-bounce.sh staging` + `verify-delivery-log.sh`; capture in `evidence/reliability/chaos-notification-<DATE>.md`. Verify dead-letter capture + delivery-log update. Satisfies US-5 AC-3.
- [ ] **T034 [B]** Reconciliation rerun: trigger 027 daily reconciliation, 026 timeout reconciliation, 025 delivery-log audit; capture verification at `evidence/reliability/reconciliation-rerun-<DATE>.md`. Confirm clean operator-queue attribution. Additionally `SELECT count(*), action_kind FROM audit_log_entries WHERE created_at > '<chaos-window-start>' GROUP BY action_kind` and attach the result to the artifact to verify Principle 25 audit-trail emission across the chaos window. Satisfies US-5 AC-4 / FR-011 / FR-019.
- [ ] **T035 [B]** If any drill reveals a wiring defect (dead-letter not draining, reconciliation skipping rows, etc.), open a `phase-1f-blocker`; remediate; re-run the drill. Satisfies US-5 AC-5 / SC-5.
- [ ] **T036 [B]** Sign off on reliability pass (Engineering Lead + Operations Lead).

## Phase 6 — Performance k6 (US-6 / SC-6 / BR-6)

- [ ] **T037 [B]** Validate RPS baseline assumptions with Operations Lead; capture at `evidence/performance/rps-baseline-<DATE>.md`. Satisfies research §9.
- [ ] **T038 [P]** Catalog scenario: `./scripts/qa/run-k6-stepped.sh tests/load/catalog.js > evidence/performance/k6-catalog-<DATE>.json`. Stepped 1× → 3× → 5× over 60 min; 15-min hold at 5×; assert p95 < 400 ms at every step. Satisfies US-6 AC-1.
- [ ] **T039 [P]** Search scenario: `./scripts/qa/run-k6-stepped.sh tests/load/search.js > evidence/performance/k6-search-<DATE>.json`. Same ramp; assert p95 < 600 ms. Satisfies US-6 AC-2.
- [ ] **T040 [P]** Checkout scenario: `./scripts/qa/run-k6-stepped.sh tests/load/checkout.js > evidence/performance/k6-checkout-<DATE>.json`. Same ramp; assert p95 < 1500 ms; provider-sandbox success ≥ 99.5 %; zero idempotency-key collisions; zero double-charge incidents. Satisfies US-6 AC-3.
- [ ] **T041 [B]** If any p95 budget breach: Engineering tunes (cache, index, replica count); re-run the breached scenario. Satisfies US-6 AC-4.
- [ ] **T042 [B]** Capture Grafana snapshots for each scenario at `evidence/performance/grafana-snapshot-<scenario>-<DATE>.png`. Sign off (Engineering Lead + QA Lead). Satisfies SC-6 / FR-012.

## Phase 7 — Production smoke (US-7) + container health (US-8)

### Production smoke (US-7 / SC-7 / BR-14)

- [ ] **T043 [B]** Production seed dry-run: `ASPNETCORE_ENVIRONMENT=Production dotnet run --project services/backend_api -- seed --mode=dry-run > evidence/production-smoke/seed-dryrun-<DATE>.log`. Verify exit 0 + `SELECT count(*) FROM seed_applied;` unchanged. Satisfies US-7 AC-1.
- [ ] **T044 [B]** Production `/health`: `curl -i https://<production-aca-host>/health > evidence/production-smoke/health-<DATE>.log`. Verify HTTP 200, every dependency probe `ok`, response < 500 ms. Satisfies US-7 AC-2.
- [ ] **T045 [B]** Production `/version`: `curl -i https://<production-aca-host>/version > evidence/production-smoke/version-<DATE>.log`. Verify SHA matches deployed image tag. Satisfies US-7 AC-3.
- [ ] **T046 [B]** Author `evidence/production-smoke/smoke-<DATE>.md` summary with frontmatter; sign off (Engineering Lead). Satisfies US-7 AC-4 / FR-013.

### Container health + rollback rehearsal (US-8 / SC-8 / BR-8)

- [ ] **T047 [P]** `backend_api` health: `./scripts/qa/sample-container-health.sh staging backend_api 60 > evidence/containers/health-backend_api-<DATE>.md`. Verify 60-s liveness + readiness all 200 within 1 s. Satisfies US-8 AC-1.
- [ ] **T048 [P]** `admin_web` health: same protocol. Satisfies US-8 AC-2.
- [ ] **T049 [P]** Flutter-web health: same protocol; additionally verify CDN cache-hit rate ≥ 90 % on warm assets. Satisfies US-8 AC-3.
- [ ] **T050 [B]** Rollback rehearsal `backend_api`: `./scripts/qa/rehearse-rollback.sh staging backend_api <previous-tag>`. Verify rollback < 5 min; post-rollback health < 30 s. Append to T047 artifact. Satisfies US-8 AC-4.
- [ ] **T051 [B]** Rollback rehearsal `admin_web`: same protocol. Append to T048 artifact. Satisfies US-8 AC-5.
- [ ] **T052 [B]** Rollback rehearsal Flutter-web: same protocol. Append to T049 artifact. Satisfies US-8 AC-5 / FR-014 / FR-015.

## Phase 8 — `impeccable-scan` promotion (US-10 / SC-10 / BR-10 / BR-16)

- [ ] **T053 [B]** Open rehearsal PR `chore/impeccable-enforcement-rehearsal` off `main`. Intentionally introduce a P1-budget breach in `apps/admin_web`. Push as draft; do NOT merge. Satisfies US-10 setup.
- [ ] **T054 [B]** Verify red check on the rehearsal PR (impeccable-scan reports `failure`). Satisfies US-10 AC-1.
- [ ] **T055 [B]** Apply `impeccable-waiver` label; CODEOWNERS-listed reviewer approves; verify check transitions to passing override. Satisfies US-10 AC-2.
- [ ] **T056 [B]** Remove the label; verify check returns to `failure` (re-lock). Close rehearsal PR (do not merge). Satisfies US-10 AC-3.
- [ ] **T057 [B]** Open threshold-flip PR `chore/impeccable-enforcement-flip` off `main`. Edits: (a) `.impeccable/thresholds.json` `mode: "advisory"` → `"enforced"`; (b) `.github/workflows/impeccable-scan.yml` — remove `Advisory exit` step, add `Threshold check` step that consults `.impeccable-report/report.json` against `.impeccable/thresholds.json` budgets and fails the workflow on breach (unless `impeccable-waiver` label present); (c) `CODEOWNERS` add line `.github/workflows/impeccable-scan.yml @design-team @engineering-leads`. Satisfies US-10 AC-4 / FR-017.
- [ ] **T058 [B]** Merge threshold-flip PR after CODEOWNERS approval. Satisfies BR-10 final state.
- [ ] **T059 [B]** Capture promotion runbook + rehearsal-PR URL + threshold-flip-PR URL at `evidence/impeccable/promotion-<DATE>.md`. Include the rehearsal PR's full check-history transition (red → labeled → green → label-removed → red). Sign off (Engineering Lead). Satisfies US-10 AC-5 / SC-10 / BR-16.

## Phase 9 — Section 13 walk + Evidence Bundle finalization + launch authorization (US-9 / SC-9 / SC-12)

- [ ] **T060 [B]** Copy `contracts/evidence-bundle-layout.md` §3.11 template intent to `evidence/launch-readiness/launch-readiness-<DATE>.md`. Walk every Section 13 line (Product / Engineering / Integrations / Operations / QA / Compliance / Monitoring); for each, record owner + evidence link + timestamp + sign-off. Satisfies US-9 AC-1 / FR-016.
- [ ] **T061 [B]** Triage missing-evidence lines: open `launch-blocker` per missing line; remediate (gather missing evidence; if blocker is genuine, open remediation issue and resolve). Satisfies US-9 AC-2.
- [ ] **T062 [B]** Verify zero open blockers across `phase-1f-blocker`, `loc-ar-blocker`, `rtl-blocker`, `security-blocker`, `launch-blocker` labels via `gh issue list --label phase-1f-blocker,loc-ar-blocker,rtl-blocker,security-blocker,launch-blocker`. Satisfies SC-12.
- [ ] **T063 [B]** Capture multi-actor signatures inline in `evidence/launch-readiness/launch-readiness-<DATE>.md`: Product Lead + Engineering Lead + Operations Lead + Security Lead. Each signature includes name, role, ISO timestamp. Satisfies US-9 AC-3 / SC-9 / FR-023.
- [ ] **T064 [B]** Final commit: stage every Evidence Bundle artifact + the launch authorization document; commit `chore(launch): launch-readiness evidence bundle finalized; launch authorization granted`. Satisfies US-9 AC-4.
- [ ] **T065 [B]** Confirm launch-readiness document SHA is referenced in the next deploy's release notes (release-notes wiring is owned by Engineering Lead's deploy pipeline; this task verifies the wire). Satisfies US-9 AC-4 final state.

---

## Traceability matrix

| Acceptance / Success Criterion | Tasks |
|---|---|
| US-1 AC-1..AC-4 / SC-1 | T014 – T017 |
| US-2 AC-1..AC-4 / SC-2 | T018 – T020 |
| US-3 AC-1..AC-3 / SC-3 | T021 – T023 |
| US-4 AC-1..AC-4 / SC-4 | T024 – T030 |
| US-5 AC-1..AC-5 / SC-5 | T031 – T036 |
| US-6 AC-1..AC-4 / SC-6 | T037 – T042 |
| US-7 AC-1..AC-4 / SC-7 | T043 – T046 |
| US-8 AC-1..AC-5 / SC-8 | T047 – T052 |
| US-9 AC-1..AC-4 / SC-9 / SC-12 | T060 – T065 |
| US-10 AC-1..AC-5 / SC-10 | T053 – T059 |
| BR-1 / FR-018 / SC-11 | T011 – T013 |
| FR-022 / SC-13 | T003 |
| Risk 11 verification (FR-021) | T001 |
| Evidence Bundle bootstrap (FR-001) | T002 |
| Tooling acquisition | T004 – T010 |

Every Acceptance Scenario in spec.md and every Success Criterion (SC-1..SC-13) has at least one corresponding task. No task ships in this spec that introduces a new feature (BR-11).
