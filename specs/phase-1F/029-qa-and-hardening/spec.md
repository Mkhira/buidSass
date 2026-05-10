# Feature Specification: 029 — QA & Launch Hardening

**Feature Branch**: `phase-1F-launch-hardening`
**Spec ID**: 029
**Created**: 2026-05-10
**Status**: Draft
**Phase**: 1F — Launch Hardening · Milestone 9
**Input**: Implementation-plan §Phase 1F spec 029 (lines 633–653) + §Section 11 Definition of Done (lines 1107–1131) + §Section 13 Launch-Readiness Checklist (lines 1154–1209). Intent verbatim: *"no new features. Everything below runs against the complete system."*

---

## Clarifications

### Session 2026-05-10

Five priority clarifications resolved on a recommended-default basis (workflow contract: any clarification not answered by the user within 1 minute falls back to the SpecKit-recommended default). All five used defaults — no live operator was reachable during this authoring window. Each is reversible during the QA execution window (Section 6) without re-spec.

- **Q1**: Final regression pass execution surface — Staging only, or Staging + Production (read-only smoke)?
  **A**: **Staging-primary; Production read-only smoke restricted to `/health`, `/version`, and the seed dry-run defined in task #7.** Source: `default`. Rationale: Production must remain customer-zero through Milestone 9; write-path regression on Production risks polluting `audit_log_entries`, payment-provider sandboxes, and notification logs. Staging carries the full ACA stack at parity per E1.
- **Q2**: Performance load-test target multiplier and shape — flat 5× expected launch RPS, or stepped 1×→3×→5× ramp?
  **A**: **Stepped 1× → 3× → 5× over 60 minutes per scenario (catalog browse, search, checkout). Hold at 5× for 15 minutes. p95 budgets enforced at every step.** Source: `default`. Rationale: implementation-plan task #6 mandates 5× as the ceiling but does not specify ramp shape; stepped ramps surface threshold failures (e.g., connection-pool saturation at 3× before saturation at 5×) that flat tests miss. Matches typical k6 best-practice for pre-launch load testing.
- **Q3**: Chaos-drill scope — provider stub injection only, or live sandbox provider failure injection?
  **A**: **Live sandbox provider failure injection for payment + shipping + notification channels, executed in the Staging environment against the real sandbox endpoints with provider-cooperative test modes.** Source: `default`. Rationale: stub-only chaos misses real-world webhook signature, retry, and dead-letter wiring. Specs 025 / 026 / 027 explicitly designed dead-letter queues + reconciliation workers that must be exercised end-to-end. Falls back to stub injection for any provider that refuses sandbox failure cooperation, with a Risk Register entry recorded.
- **Q4**: Arabic editorial reviewer engagement model — single batch sign-off, or rolling per-surface sign-off as fixes land?
  **A**: **Rolling per-surface sign-off, with a final consolidated sign-off line on the launch-readiness checklist.** Source: `default`. Rationale: the editorial reviewer is the launch-blocking dependency (Risk 11). A single batch model guarantees a serial bottleneck at the end of Milestone 9; rolling sign-off lets fixes from earlier surfaces unblock parallel work. Final consolidated line preserves the auditability requirement.
- **Q5**: `impeccable-scan` enforcement promotion (task #10) — flip enforcement on a single throwaway PR first, or flip directly on `main` and unwind via revert if needed?
  **A**: **Throwaway-PR rehearsal required: the threshold flip lives on a draft PR (`chore/impeccable-enforcement-rehearsal`) that intentionally breaches a P1 threshold to verify (a) red check, (b) `impeccable-waiver` label unblock, (c) CODEOWNERS waiver-approval flow. Only after the rehearsal PR demonstrates all three behaviors does the flip merge to `main`.** Source: `default`. Rationale: matches the "Dry-run on a throwaway PR" wording in implementation-plan task #10. Direct-to-`main` flips have caused merge freezes in prior phases (1C scaffold work); the rehearsal pattern is cheap insurance.

**External prerequisite — NOT a task in this spec**: Risk 11 — Arabic editorial reviewer named and onboarded — MUST be resolved before this spec can exit. Resolution is owned by Product leadership per the Risk Register; this spec depends on the resolution but does not own it. If unresolved at the start of QA execution, this spec MUST be paused and Risk 11 escalated.

---

## ADR & Constitution Traceability

| Source | Title | How 029 satisfies it |
|---|---|---|
| Principle 4 | Arabic editorial-grade | Localization audit pillar (BR-2) requires named human Arabic editorial reviewer sign-off on every customer-facing surface. No machine-translated string ships. |
| Principle 7 | Brand palette | `impeccable-scan` enforcement (BR-10) catches palette drift on `apps/admin_web` at merge time. |
| Principle 24 | State machines | Reliability pillar (BR-5) re-verifies every state machine across phases 1A–1E by replaying webhook fixtures + injecting forced transitions. |
| Principle 25 | Audit | Every QA finding, sign-off, waiver, and promotion is auditable: this spec produces a Launch-Readiness Evidence Bundle (BR-9) that captures actor + timestamp + evidence URL for every checklist line. |
| Principle 28 | AI-build | Spec is implementation-ready: every QA pillar enumerates owner, tooling, exit criterion, and evidence artifact. |
| Principle 29 | Required spec output | All twelve sections present. |
| Principle 30 | Phasing | Spec scoped to Phase 1F. Hardening only — no new features. |
| Principle 31 | Constitution supremacy | If any QA finding conflicts with the constitution, the constitution wins; the conflicting work item is opened as a remediation issue and 029 cannot exit until resolved. |
| Section 11 DoD | Definition of Done | BR-1 re-verifies every DoD checkbox against every Phase 1A–1E module; missing checkboxes block 029 exit. |
| Section 13 | Launch-Readiness Checklist | BR-9 maps every checklist line (Product / Engineering / Integrations / Operations / QA / Compliance / Monitoring) to a verification owner + evidence artifact. |
| Risk 11 | Arabic editorial reviewer | Called out as external prerequisite (above), NOT as a task. |
| ADR-010 | Cloud + residency | Compliance pillar (BR-7) re-verifies no out-of-region data paths; Production smoke (task #7) hits Production ACA in `Saudi Arabia Central` only. |
| Spec 015 | admin_web scaffold | `impeccable-scan` enforcement promotion targets `apps/admin_web`; depends on spec 015's Next.js scaffold being live (it is, per Phase 1C exit). |
| Spec E1 | infrastructure-integration | Staging ACA stack required for performance + chaos; Production ACA required for production smoke (task #7). |
| Spec 003 | shared-foundations | `audit_log_entries` is the destination for every audit trail line written during 029 execution. |
| All 1A–1E specs | — | Hard dependency: every spec at DoD before 029 starts. Verified by BR-1. |

---

## Goal

Take the system from "Phase 1A–1E features merged" to **launch-ready** by executing a structured, auditable, multi-pillar verification pass across the complete platform. **No new features ship in this spec.** The output is:

1. A signed Launch-Readiness Evidence Bundle covering every line of the Section 13 checklist.
2. A clean DoD audit confirming every Phase 1A–1E module satisfies every Section 11 checkbox.
3. A passing functional regression across customer + admin + B2B user stories.
4. A passing localization + RTL audit signed off by the named Arabic editorial reviewer.
5. A passing security pass (OWASP ASVS L1, dependency scan, secret scan, auth fuzzing, IDOR sweep).
6. A passing reliability pass (chaos drills + reconciliation rerun) on payment, shipping, notification providers.
7. A passing performance pass (k6, stepped 1×→3×→5× of expected launch RPS, p95 budgets enforced).
8. A passing Production smoke (`/health` 200; `seed --mode=dry-run` exits 0 with zero `seed_applied` rows).
9. A passing container health verification on Staging for `backend_api`, `admin_web`, and Flutter-web with image-tag rollback rehearsed.
10. The `impeccable-scan` CI job promoted from advisory to merge-blocking on `apps/admin_web`, with the `impeccable-waiver` label flow rehearsed end-to-end.

Exit of 029 = exit of Phase 1F = **launch authorization granted** (formal sign-off captured in the Evidence Bundle).

---

## User Scenarios & Testing *(mandatory)*

This spec's "users" are internal: QA Lead, Engineering Lead, Security Lead, Operations Lead, Arabic Editorial Reviewer, Product Lead, and the Launch Captain. Each scenario is a verification activity that produces evidence; "passing" means the produced evidence satisfies the listed exit criterion and the corresponding line(s) on the Section 13 checklist are checked.

### User Story 1 — QA Lead executes the full functional regression (Priority: P1)

QA Lead runs the full Phase-1-scope regression suite against the Staging ACA stack. Suite covers every customer user story (browse, search, register, login, cart, checkout, COD/card/BNPL payment, order tracking, reorder, return request, review submission), every B2B user story (company account, multi-user roles, quote request, quote approval, bulk order, repeat-order template, PO/reference numbers, invoice billing), and every admin user story (catalog ops, inventory ops including lot/batch/expiry, order management, returns/refunds, verification approval/rejection, quotation management, promotion authoring, CMS publishing, support ticket handling, finance review, role/permission management).

**Why this priority**: Without a passing functional regression, no other QA pillar is meaningful. This is the foundational gate.

**Independent Test**: Execute the regression test suite (Postman + k6 functional + Flutter integration_test + Playwright admin E2E) against the Staging ACA stack. Suite must finish with zero P0 failures, zero P1 failures, and ≤ 5 P2 findings each with a tracked remediation issue. Evidence artifact: `evidence/regression/regression-run-<DATE>.junit.xml` + linked dashboard URL.

**Acceptance Scenarios**:

1. **Given** Staging is at the post-Phase-1E commit, **When** QA Lead triggers the regression workflow, **Then** every user story listed in Section 8 of the implementation plan executes and reports pass/fail per scenario.
2. **Given** a regression run completes, **When** the run report is uploaded to the Evidence Bundle, **Then** the Bundle index lists the run with timestamp, commit SHA, environment, pass-rate, and links to per-suite JUnit artifacts.
3. **Given** a P0 or P1 failure surfaces, **When** the QA Lead triages it, **Then** the failure is opened as a blocking issue tagged `phase-1f-blocker`; 029 cannot exit while any `phase-1f-blocker` is open.
4. **Given** all regression failures are remediated, **When** the regression workflow is re-run, **Then** the green run replaces the prior run in the Evidence Bundle, and the Section 13 "Full regression passed" line is checked with the run URL as evidence.

---

### User Story 2 — Arabic Editorial Reviewer signs off every customer-facing surface (Priority: P1)

The named Arabic editorial reviewer (Risk 11 must be resolved) reviews every Arabic string surface: mobile-app screens, web storefront screens, admin dashboard screens, transactional emails, push notifications, SMS templates, PDF invoices, PDF tax receipts, return-request notices, verification result notices, OTP messages, and CMS-managed content (legal pages, FAQ, blog snippets). Reviewer flags any string that fails editorial-grade Arabic (machine-translation residue, dialect drift, formal-register violations, RTL punctuation breaks, currency-formatting drift, date-formatting drift). Engineering opens fix issues for each flagged string; reviewer re-reviews and signs off per surface.

**Why this priority**: Principle 4 mandates editorial-grade Arabic; Risk 11 calls this out as the launch-blocking long-pole. No launch without sign-off.

**Independent Test**: For each surface kind (mobile, web, admin, email, push, SMS, PDF-invoice, PDF-tax, CMS), the reviewer attaches a signed sign-off entry to the Evidence Bundle: `evidence/localization/<surface>-signoff-<DATE>.md` containing reviewer name, surface inventory, scope notes, list of flagged-and-resolved issues, and a final approval line. The consolidated checklist line "Arabic editorial QA passed by a named human reviewer" (Section 13 QA) is checked only when all per-surface sign-offs are present.

**Acceptance Scenarios**:

1. **Given** Risk 11 is resolved (reviewer named in the Risk Register), **When** the localization audit kicks off, **Then** Engineering produces a per-surface string inventory with screenshots / sample renders for every Arabic-facing surface and hands it to the reviewer.
2. **Given** the reviewer flags an issue on surface X, **When** Engineering opens a fix issue, **Then** the issue is tagged `loc-ar-blocker`, linked from the per-surface sign-off draft, and 029 cannot mark surface X as signed-off until the issue is closed.
3. **Given** all flagged issues on surface X are resolved, **When** the reviewer re-reviews, **Then** the per-surface sign-off entry is finalized in the Evidence Bundle with the reviewer's name and timestamp.
4. **Given** all per-surface sign-offs are finalized, **When** the consolidated checklist line is reviewed, **Then** the Section 13 QA line is checked and the Evidence Bundle is updated.

---

### User Story 3 — RTL visual regression sweep across mobile + web + admin (Priority: P1)

QA executes an RTL visual regression sweep across every screen in the customer mobile app (Flutter), the customer web storefront (Flutter web), and the admin dashboard (Next.js). Sweep captures `ar-SA` and `ar-EG` baseline screenshots and diffs them against the previous baseline. Flagged regressions: text overflow, mirrored-icon misuse (e.g., back-arrow not mirrored), dynamic-text truncation, table-column reflow failure, modal-button order, form-field label/value alignment, currency-symbol position, date-format direction, breadcrumb separator direction.

**Why this priority**: Principle 4 requires every screen support RTL. Visual regressions ship as customer-facing defects with high reputational cost in KSA/EG.

**Independent Test**: Run the RTL visual-diff workflow (`scripts/qa/rtl-visual-sweep.sh`) against Staging. Workflow produces a per-platform diff report: `evidence/rtl/rtl-sweep-<platform>-<DATE>.html` + screenshot ZIP. Sweep must report zero P0 visual regressions, zero P1 visual regressions, and ≤ 10 P2 cosmetic issues each with a tracked remediation issue.

**Acceptance Scenarios**:

1. **Given** the Staging build is current, **When** the RTL sweep runs, **Then** it captures every customer-facing screen + every admin screen in `ar-SA` + `ar-EG` and produces visual-diff artifacts.
2. **Given** a P0/P1 visual regression is flagged, **When** Engineering opens a fix issue, **Then** the issue is tagged `rtl-blocker`; 029 cannot exit while any `rtl-blocker` is open.
3. **Given** all blockers are remediated, **When** the sweep is re-run, **Then** the green sweep report is added to the Evidence Bundle and Section 13 QA "Web + mobile (Android + iOS) smoke tests passed" gains an RTL evidence link.

---

### User Story 4 — Security Lead executes OWASP ASVS L1 + dependency + secret + auth-fuzzing + IDOR sweep (Priority: P1)

Security Lead executes a structured security pass:

- OWASP ASVS L1 control review against the platform (every L1 control mapped to "implemented / N/A with rationale / gap").
- Dependency scan (`dotnet list package --vulnerable --include-transitive` + `pnpm audit --audit-level=moderate` + `flutter pub outdated --mode=null-safety`) with zero High / Critical CVEs.
- Secret scan (`gitleaks detect --redact --no-git -s .`) with zero findings on `main` and on the merge target.
- Auth fuzzing pass against spec 004's `auth.tokens` + `mfa.tokens` + `otp_codes` endpoints (focus: replay, signature stripping, JWT alg-confusion, refresh-token reuse, MFA bypass).
- IDOR sweep against every per-tenant / per-company / per-user resource endpoint across Phase 1A–1E (catalog, orders, quotes, returns, payments, invoices, support tickets, verification, reviews, CMS preview tokens).

**Why this priority**: ADR-007 / ADR-010 / Principle 13 / Principle 25 / KSA PDPL all require security gates pre-launch. Section 13 Compliance block depends on this work product.

**Independent Test**: Each sub-pass produces an evidence artifact: `evidence/security/asvs-l1-<DATE>.md`, `evidence/security/depscan-<DATE>.json`, `evidence/security/gitleaks-<DATE>.json`, `evidence/security/auth-fuzz-<DATE>.md`, `evidence/security/idor-sweep-<DATE>.md`. Each artifact carries a zero-findings line or, where findings exist, links to remediation issues that must be closed before sub-pass can be marked complete.

**Acceptance Scenarios**:

1. **Given** the security pass kicks off, **When** the dep + secret scans run on `main`, **Then** zero High / Critical / secret findings remain open at sub-pass completion.
2. **Given** the auth-fuzzing pass runs, **When** any successful bypass is recorded, **Then** the bypass is opened as a `security-blocker` and 029 cannot exit until it is closed by a Security-Lead-signed verification.
3. **Given** the IDOR sweep covers every resource endpoint, **When** the sweep produces a coverage matrix, **Then** the matrix is attached to the Evidence Bundle and shows ≥ 95 % endpoint coverage with no successful unauthorized access on the covered endpoints.
4. **Given** the OWASP ASVS L1 review completes, **When** the artifact is finalized, **Then** every L1 control row reads `implemented` or `N/A with documented rationale` — no row reads `gap`.

---

### User Story 5 — Reliability pass: chaos drills + reconciliation rerun on payment, shipping, notification (Priority: P1)

Operations Lead + Engineering Lead execute chaos drills against the spec 025 / 026 / 027 provider integrations on Staging:

- **Payment chaos** (027): force a 5xx burst from HyperPay sandbox; verify Polly retry policy fires; verify dead-letter routing engages on retry exhaustion; verify the daily reconciliation job picks up orphaned authorizations on the next cycle.
- **Shipping chaos** (026): force a webhook outage from the primary EG shipping provider; verify the timeout reconciler ages out pending shipments; verify the manual operator queue receives the orphaned rows.
- **Notification chaos** (025): force an SES sandbox bounce; verify the dead-letter queue receives the bounce; verify the per-channel delivery-log records the failure with provider message-id and reason.

After chaos, the team **reruns the reconciliation jobs** (027 daily reconciliation + 026 shipping reconciliation + 025 notification delivery-log audit) and verifies the orphaned rows surface in the operator queues correctly, and that `audit_log_entries` captures every operator action taken during the chaos window.

**Why this priority**: Principle 13 + spec 027 BR-16 require operationally-rehearsed chaos drills before launch. Webhook reliability is the dominant launch risk for paid orders.

**Independent Test**: Each chaos scenario produces a runbook execution log: `evidence/reliability/chaos-payment-<DATE>.md`, `evidence/reliability/chaos-shipping-<DATE>.md`, `evidence/reliability/chaos-notification-<DATE>.md`. Each log records: pre-drill state, injection method, observed system behavior, recovery time, post-drill verification, and any deviation from expected behavior.

**Acceptance Scenarios**:

1. **Given** the payment chaos drill runs, **When** retry exhaustion fires, **Then** the dead-letter queue receives the failed payment with full provider context, and the reconciliation job opens an exception in the operator queue on the next run.
2. **Given** the shipping chaos drill runs, **When** the timeout reconciler fires, **Then** the orphaned shipments age out per their per-provider TTL and surface in the operator queue with a clear remediation prompt.
3. **Given** the notification chaos drill runs, **When** the SES bounce arrives, **Then** the dead-letter queue captures the bounce, the delivery-log row is updated with `failed`, and the per-channel delivery-success metric reflects the failure.
4. **Given** all three drills complete, **When** the reconciliation rerun is executed, **Then** every orphaned row is correctly attributed and no spurious exceptions are opened.
5. **Given** the drills produce findings, **When** Engineering remediates, **Then** the drill is re-run and the green run replaces the prior log in the Evidence Bundle.

---

### User Story 6 — Performance pass: k6 load tests at 5× expected launch RPS (Priority: P1)

QA Lead executes k6 load tests against the **Staging ACA stack** for three scenarios:

- **Catalog browse** (spec 005): list + filter + facet + product detail.
- **Search** (spec 006): autocomplete + faceted search + Arabic-normalized query.
- **Checkout** (specs 010 + 012 + 027): cart → tax calc → invoice draft → payment authorization (sandbox).

For each scenario, the test ramps **1× → 3× → 5×** of expected launch RPS over 60 minutes, holds at 5× for 15 minutes, and asserts p95 latency budgets (catalog browse p95 < 400 ms; search p95 < 600 ms; checkout p95 < 1500 ms — budgets per Stage 7 SLO targets). Failure to hold p95 under budget at any step is a blocker; failure at the 5× hold step is a launch blocker.

**Why this priority**: Implementation-plan task #6 mandates k6 at 5× as a Phase 1F deliverable. Without performance evidence at planned-launch load, on-call coverage cannot justify launch-day staffing.

**Independent Test**: Each scenario produces a k6 summary artifact: `evidence/performance/k6-catalog-<DATE>.json`, `evidence/performance/k6-search-<DATE>.json`, `evidence/performance/k6-checkout-<DATE>.json` plus a Grafana dashboard snapshot. Each artifact must show p95 within budget at every ramp step + the 5× hold.

**Acceptance Scenarios**:

1. **Given** the Staging ACA stack is at parity with Production capacity (≥ 100 % of planned production replica count), **When** the k6 catalog scenario runs, **Then** p95 < 400 ms holds at 1×, 3×, 5×, and 5×-hold.
2. **Given** the search scenario runs, **When** the 5×-hold step completes, **Then** p95 < 600 ms holds; Meilisearch reports zero query timeouts; Postgres connection-pool saturation < 70 %.
3. **Given** the checkout scenario runs, **When** the 5×-hold step completes, **Then** p95 < 1500 ms holds; payment-provider sandbox returns success rate ≥ 99.5 %; zero idempotency-key collisions; zero double-charge incidents in the test window.
4. **Given** any p95 budget is breached, **When** Engineering tunes (cache, index, replica count), **Then** the scenario is re-run and the green run replaces the prior artifact in the Evidence Bundle.

---

### User Story 7 — Production smoke: seed dry-run + `/health` (Priority: P1)

Engineering Lead executes the production smoke from a controlled deploy environment:

- `ASPNETCORE_ENVIRONMENT=Production dotnet run -- seed --mode=dry-run` — must exit 0 and write **zero** rows to `seed_applied`. (The seed framework's dry-run mode evaluates seeders without committing.)
- `curl https://<production-aca-host>/health` — must return HTTP 200 with the standard health payload (db: ok, search: ok, providers: ok where probed).
- `curl https://<production-aca-host>/version` — must return the expected commit SHA matching the deployed image tag.

**Why this priority**: Implementation-plan task #7 mandates this exact smoke. It validates that Production ACA is wired correctly before customer traffic.

**Independent Test**: Engineering Lead captures the three command outputs into `evidence/production-smoke/smoke-<DATE>.md`. Output must show exit code 0, zero `seed_applied` rows, HTTP 200 from `/health`, and matching SHA from `/version`.

**Acceptance Scenarios**:

1. **Given** the Production ACA stack is deployed at the launch candidate image tag, **When** the seed dry-run executes, **Then** the command exits 0 and `SELECT count(*) FROM seed_applied;` returns the same count as before the run.
2. **Given** `/health` is queried, **When** the response is captured, **Then** the response is HTTP 200, all dependency probes report `ok`, and the response time is < 500 ms.
3. **Given** `/version` is queried, **When** the response is captured, **Then** the returned SHA matches the deployed image tag exactly.
4. **Given** any smoke step fails, **When** Engineering remediates, **Then** the smoke is re-run and the green capture replaces the prior artifact in the Evidence Bundle.

---

### User Story 8 — Container health verification + image-tag rollback rehearsal on Staging (Priority: P1)

Engineering Lead executes container health verification against Staging ACA for `backend_api`, `admin_web`, and Flutter-web (per E1's hosting decision: Flutter-web is served from the same ACA tier as `admin_web`'s static assets via Cloudflare-fronted CDN). For each container:

- Liveness probe responds 200 within 1 s for 60 consecutive seconds.
- Readiness probe responds 200 within 1 s for 60 consecutive seconds.
- Image-tag rollback is rehearsed by deploying tag `n-1` over tag `n`, verifying the rollback completes within 5 minutes, and verifying the rolled-back container passes liveness + readiness immediately after rollback.

**Why this priority**: Implementation-plan task #8 mandates this exact verification + rehearsal. Image-tag rollback is the primary launch-day rollback lever.

**Independent Test**: Each container produces a verification log: `evidence/containers/health-<container>-<DATE>.md`. Log records: liveness/readiness sample windows, rollback target tag, rollback duration, post-rollback verification. Rollback rehearsal must complete cleanly on every container.

**Acceptance Scenarios**:

1. **Given** Staging is at the launch candidate image tag, **When** liveness + readiness samples are taken on `backend_api` for 60 s, **Then** every sample returns 200 within 1 s.
2. **Given** `admin_web` is queried, **When** liveness + readiness samples are taken for 60 s, **Then** every sample returns 200 within 1 s.
3. **Given** Flutter-web is queried, **When** liveness + readiness samples are taken for 60 s, **Then** every sample returns 200 within 1 s and the static-asset CDN reports cache-hit rate ≥ 90 % on warm assets.
4. **Given** the image-tag rollback rehearsal runs on `backend_api`, **When** tag `n-1` is deployed over tag `n`, **Then** the rollback completes within 5 minutes and the rolled-back container passes liveness + readiness within 30 s of rollout completion.
5. **Given** the rollback rehearsal repeats on `admin_web` and Flutter-web, **When** each rollback completes, **Then** the same time + health criteria hold.

---

### User Story 9 — Launch Captain executes Section 13 launch-readiness checklist end-to-end (Priority: P1)

The Launch Captain (named role; cross-functional) walks the Section 13 checklist line-by-line. For every line in every block (Product / Engineering / Integrations / Operations / QA / Compliance / Monitoring), the Captain captures: verification owner, evidence artifact link, sign-off timestamp, and sign-off actor. Lines without complete evidence are blockers. The completed checklist + Evidence Bundle index is the launch authorization document.

**Why this priority**: This is the formal launch-authorization gate. Without it, launch cannot be granted.

**Independent Test**: The Captain produces a single document: `evidence/launch-readiness/launch-readiness-<DATE>.md`. Document contains the full Section 13 checklist with every line either checked + evidence-linked + timestamped + signed, or marked as a blocker with a tracked remediation issue. 029 exits when zero blockers remain.

**Acceptance Scenarios**:

1. **Given** every prior User Story 1–8 has produced its evidence, **When** the Captain walks the Section 13 checklist, **Then** every checklist line maps to ≥ 1 evidence artifact in the Bundle.
2. **Given** a checklist line lacks evidence, **When** the Captain marks it blocker, **Then** a remediation issue is opened tagged `launch-blocker` and 029 cannot exit while any `launch-blocker` is open.
3. **Given** all blockers are resolved, **When** the Captain finalizes the document, **Then** the document is signed by Product Lead + Engineering Lead + Operations Lead + Security Lead, and launch authorization is granted.
4. **Given** the document is finalized, **When** the next deploy is cut, **Then** the deploy carries the launch-readiness document SHA in its release notes.

---

### User Story 10 — `impeccable-scan` promoted from advisory to merge-blocking on `apps/admin_web` (Priority: P1)

Engineering Lead executes the four-step promotion of the `impeccable-scan` CI job to merge-blocking status:

1. Flip `.impeccable/thresholds.json` `mode: "advisory"` → `mode: "enforced"`.
2. Remove the `Advisory exit` step from `.github/workflows/impeccable-scan.yml` (the `if: always()` `echo` step at the end). Retain the `continue-on-error: true` on the `Run impeccable detect` step but gate the workflow's overall exit on a new `Threshold check` step that consults `.impeccable-report/report.json` against the budgets in `.impeccable/thresholds.json`.
3. Wire the `impeccable-waiver` PR label as the override path: PRs carrying the label and approved by a CODEOWNERS-listed reviewer pass the check. CODEOWNERS gains a line: `.github/workflows/impeccable-scan.yml @design-team @engineering-leads`.
4. Rehearse the flow on a throwaway PR (`chore/impeccable-enforcement-rehearsal`) that intentionally introduces a P1-budget breach to verify (a) red check on the rehearsal PR, (b) `impeccable-waiver` label + CODEOWNERS approval unblocks merge, (c) removing the label re-locks the merge.

**Why this priority**: Implementation-plan task #10 mandates this promotion as a Phase 1F deliverable. Tied to spec 015's design-system stability commitment.

**Independent Test**: The promotion produces evidence: `evidence/impeccable/promotion-<DATE>.md` + the merged threshold-flip PR + the closed rehearsal PR. The rehearsal PR must show the red → labeled → green → label-removed → red transition in its check history.

**Acceptance Scenarios**:

1. **Given** `.impeccable/thresholds.json` is flipped to `enforced`, **When** a PR introduces a P1-budget breach, **Then** the `impeccable-scan` job reports `failure` and the PR cannot merge.
2. **Given** the same PR receives the `impeccable-waiver` label, **When** a CODEOWNERS-listed reviewer approves the waiver, **Then** the check transitions to a passing override state and the PR is mergeable.
3. **Given** the label is removed, **When** the check re-runs, **Then** the check returns to `failure` and the merge re-locks.
4. **Given** the rehearsal completes, **When** the threshold-flip PR is merged to `main`, **Then** the next PR touching `apps/admin_web` runs the enforced scan and gates on it.
5. **Given** the rehearsal PR is closed, **When** the Evidence Bundle is updated, **Then** the rehearsal PR URL is linked under `evidence/impeccable/promotion-<DATE>.md` for audit traceability.

---

### Edge Cases

- **Risk 11 unresolved at QA execution start**: 029 MUST be paused. Risk 11 escalates to the Risk Register weekly. No User Story 2 work begins until the reviewer is named in the Risk Register.
- **Production smoke `/health` returns degraded** (e.g., search probe `degraded` but db probe `ok`): treat as a blocker; do not proceed to launch. Engineering must restore full `ok` before re-running the smoke.
- **Performance pass exceeds p95 budget at 5× hold but holds at 3×**: treat as a launch-blocker per task #6 wording. Engineering tunes; scenario re-runs. If tuning extends beyond Milestone 9 timeline, escalate to Product for go/no-go.
- **A Phase 1A–1E spec's DoD audit reveals a missed checkbox** (e.g., audit-event emission missing on a state transition): open a `phase-1f-blocker`. The owning module's team remediates. 029 cannot exit while open.
- **Chaos drill reveals a dead-letter queue not consuming** (e.g., 027's payment dead-letter not draining): treat as a launch-blocker. Engineering remediates; drill re-runs.
- **Arabic editorial reviewer flags a non-fixable surface** (e.g., a third-party SDK string we cannot re-translate): document the exception, capture sign-off-with-exception explicitly, and link the exception to a Phase 1.5 follow-up issue. Launch authorization may proceed only if Product Lead signs the exception.
- **`impeccable-scan` rehearsal PR cannot find a CODEOWNERS reviewer to approve the waiver** (e.g., reviewer absence): the Engineering Lead may temporarily designate a backup reviewer in the Evidence Bundle for the rehearsal only. Production CODEOWNERS list remains the gating list for real PRs.
- **Container rollback rehearsal fails** (e.g., rollback exceeds the 5-minute SLA): treat as a launch-blocker. Engineering investigates the platform-side rollback pipeline and re-rehearses.
- **A regression test flakes intermittently on Staging** (passes 8 / 10 runs): treat as a P1 finding; flake must be rooted out (or the test deleted with a tracked replacement issue) before 029 exit. No flaky tests ship to launch monitoring.
- **A dependency-scan finding is fixed via a transitive bump that cannot ship** (e.g., breaking change in a transitive dep): document as a known-CVE-with-mitigation entry; mitigation must be sign-off-able by the Security Lead and tracked in Phase 1.5.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST produce a Launch-Readiness Evidence Bundle stored at `evidence/` (top-level repo path or a dedicated documentation repo, per Operations Lead choice; default: top-level `evidence/` directory in this repo) covering every line of Section 13.
- **FR-002**: The system MUST execute a full functional regression covering every customer + admin + B2B user story listed in Section 8 of the implementation plan; results captured in JUnit format.
- **FR-003**: The system MUST execute an Arabic localization audit by the named editorial reviewer (Risk 11 prerequisite) covering every Arabic string surface; results captured per-surface in the Evidence Bundle.
- **FR-004**: The system MUST execute an RTL visual regression sweep across mobile (Flutter Android + iOS), web storefront (Flutter web), and admin (Next.js) for `ar-SA` + `ar-EG`; results captured as visual-diff reports.
- **FR-005**: The system MUST execute an OWASP ASVS L1 control review with every L1 control mapped to `implemented` or `N/A with documented rationale`.
- **FR-006**: The system MUST execute a dependency vulnerability scan on `services/backend_api` (.NET), `apps/admin_web` (pnpm), and `apps/customer_flutter` (Flutter) with zero High / Critical CVEs at the time of launch authorization.
- **FR-007**: The system MUST execute a secret scan (`gitleaks`) against the merge target with zero findings.
- **FR-008**: The system MUST execute auth fuzzing against spec 004's identity endpoints with zero successful bypass.
- **FR-009**: The system MUST execute an IDOR sweep across every per-tenant / per-company / per-user resource endpoint with ≥ 95 % endpoint coverage and zero successful unauthorized access on covered endpoints.
- **FR-010**: The system MUST execute live-sandbox chaos drills on payment (027) + shipping (026) + notification (025) providers with full webhook + retry + dead-letter + reconciliation verification.
- **FR-011**: The system MUST rerun every reconciliation job (027 daily, 026 timeout, 025 delivery-log audit) post-chaos and confirm clean operator-queue attribution.
- **FR-012**: The system MUST execute k6 load tests against the Staging ACA stack for catalog + search + checkout, ramping 1× → 3× → 5× of expected launch RPS over 60 minutes per scenario, holding at 5× for 15 minutes, with p95 budgets enforced (catalog 400 ms; search 600 ms; checkout 1500 ms).
- **FR-013**: The system MUST execute a Production smoke: `ASPNETCORE_ENVIRONMENT=Production dotnet run -- seed --mode=dry-run` exits 0 with zero `seed_applied` rows; `/health` returns 200; `/version` returns matching SHA.
- **FR-014**: The system MUST execute container health verification on Staging for `backend_api`, `admin_web`, and Flutter-web with 60-second liveness + readiness sampling.
- **FR-015**: The system MUST rehearse image-tag rollback on Staging for every container with rollback completion < 5 minutes and post-rollback health-check pass < 30 s.
- **FR-016**: The system MUST execute the Section 13 launch-readiness checklist end-to-end with every line checked + evidence-linked + timestamped + signed by an authorized actor.
- **FR-017**: The system MUST promote `.github/workflows/impeccable-scan.yml` from advisory to merge-blocking on `apps/admin_web` by flipping `.impeccable/thresholds.json` to `mode: "enforced"`, removing the advisory-exit step, wiring the `impeccable-waiver` label override path through CODEOWNERS, and rehearsing the full red → waiver → unblock → re-lock cycle on a throwaway PR.
- **FR-018**: The system MUST re-verify every Section 11 DoD checkbox against every Phase 1A–1E module; missing checkboxes are `phase-1f-blocker`s.
- **FR-019**: The system MUST capture every QA finding, sign-off, waiver, and promotion as an `audit_log_entries` row (where the action is system-internal) or as an Evidence Bundle entry (where the action is human-driven), preserving Principle 25 traceability.
- **FR-020**: The system MUST NOT introduce new product features in this spec. Any work item that introduces user-facing functionality MUST be deferred to Phase 1.5.
- **FR-021**: The system MUST treat Risk 11 (Arabic editorial reviewer) as a hard external prerequisite, NOT as a task in this spec; if unresolved at QA-start, 029 pauses and Risk 11 escalates.
- **FR-022**: The system MUST advance the SpecKit `feature.json` from the stale `phase-1D` pointer to `specs/phase-1F/029-qa-and-hardening` as part of this spec's setup tasks.
- **FR-023**: The system MUST capture launch authorization in a single signed document (`evidence/launch-readiness/launch-readiness-<DATE>.md`) signed by Product Lead + Engineering Lead + Operations Lead + Security Lead.

### Key Entities *(no new schema)*

This spec introduces **no new database tables**, **no new domain events**, **no new state machines**, and **no new modules**. It exercises and verifies the entities already created by Phase 1A–1E.

The only new artifacts are **filesystem evidence**:

- **Evidence Bundle**: directory tree at `evidence/` (or equivalent) containing per-pillar verification artifacts (regression reports, localization sign-offs, RTL diff reports, security artifacts, reliability runbook logs, performance k6 results, production-smoke captures, container-health logs, launch-readiness checklist).
- **Launch Authorization Document**: `evidence/launch-readiness/launch-readiness-<DATE>.md` — the signed, dated, multi-actor launch authorization.

---

## Business Rules

- **BR-1 (DoD audit)**: Every Phase 1A–1E spec MUST satisfy every Section 11 checkbox before 029 starts substantive verification work. Missing checkboxes = `phase-1f-blocker`.
- **BR-2 (Arabic editorial-grade)**: No Arabic string ships without named-reviewer sign-off. Risk 11 is the gating dependency; rolling per-surface sign-off is the engagement model (Q4).
- **BR-3 (RTL parity)**: Every customer-facing screen + admin screen MUST pass RTL visual regression in `ar-SA` + `ar-EG`. Zero P0 / P1 visual regressions at exit.
- **BR-4 (Security floor)**: OWASP ASVS L1, dependency scan, secret scan, auth fuzzing, IDOR sweep MUST all return zero blocker findings. Mitigated-but-known findings require Security-Lead sign-off and a Phase 1.5 tracking issue.
- **BR-5 (Reliability rehearsal)**: Live-sandbox chaos drills MUST exercise payment + shipping + notification webhook → retry → dead-letter → reconciliation paths end-to-end. Stub injection is a fallback, not a default (Q3).
- **BR-6 (Performance ceiling)**: k6 stepped ramp 1× → 3× → 5× over 60 minutes per scenario; p95 budgets enforced at every step + the 15-minute 5× hold (Q2).
- **BR-7 (Residency)**: Production smoke MUST execute against Production ACA in `Saudi Arabia Central` only. Any out-of-region resource discovered during 029 MUST be remediated before exit (ADR-010).
- **BR-8 (Rollback rehearsal)**: Image-tag rollback MUST complete in < 5 minutes per container with post-rollback health-check pass < 30 s. Tested on every container.
- **BR-9 (Evidence Bundle)**: Every Section 13 checklist line MUST map to ≥ 1 evidence artifact + ≥ 1 sign-off actor + a timestamp. The Bundle is the launch authorization document.
- **BR-10 (impeccable enforcement)**: `impeccable-scan` flips from advisory to merge-blocking on `apps/admin_web` via the four-step process in User Story 10. Rehearsal on a throwaway PR is mandatory before the flip merges to `main` (Q5).
- **BR-11 (No new features)**: 029 introduces no new features. Every work item that does is rejected and routed to Phase 1.5.
- **BR-12 (Audit trail)**: Every QA finding, sign-off, waiver, and CI-config change MUST be auditable per Principle 25 — either via `audit_log_entries` (system-internal actions) or Evidence Bundle entries (human-driven actions).
- **BR-13 (Blockers list)**: 029 MUST track three blocker labels: `phase-1f-blocker` (general), `loc-ar-blocker` (localization), `rtl-blocker` (visual), `security-blocker` (security pass), `launch-blocker` (Section 13 lines). 029 cannot exit while any of these is open.
- **BR-14 (Production read-only smoke)**: The Production smoke (User Story 7) MUST be read-only — `/health`, `/version`, and `seed --mode=dry-run`. Any write-path operation against Production is forbidden in this spec (Q1).
- **BR-15 (Per-surface localization sign-off model)**: Localization sign-offs are rolling per-surface; a final consolidated checklist line gates exit (Q4).
- **BR-16 (Throwaway-PR rehearsal for impeccable promotion)**: The threshold-flip PR cannot merge until a separate rehearsal PR has demonstrated red check → label unblock → re-lock (Q5).

---

## Success Criteria *(mandatory)*

These are the measurable conditions for 029 exit. Each is verifiable from the Evidence Bundle without re-running the QA pillars.

- **SC-1**: Functional regression returns zero P0 / P1 failures and ≤ 5 P2 findings (each with a tracked remediation issue). Evidence: `evidence/regression/regression-run-<DATE>.junit.xml`.
- **SC-2**: Arabic editorial reviewer signs off on every Arabic string surface (mobile, web, admin, email, push, SMS, PDF-invoice, PDF-tax, CMS). Evidence: `evidence/localization/<surface>-signoff-<DATE>.md` × per-surface count.
- **SC-3**: RTL visual regression sweep returns zero P0 / P1 visual regressions and ≤ 10 P2 cosmetic issues. Evidence: `evidence/rtl/rtl-sweep-<platform>-<DATE>.html` × per-platform.
- **SC-4**: Security pass returns zero High / Critical CVEs (deps), zero secret-scan findings, zero successful auth bypass, zero successful IDOR access on covered endpoints, every OWASP ASVS L1 control `implemented` or `N/A`. Evidence: `evidence/security/*`.
- **SC-5**: Reliability chaos drills + reconciliation rerun complete cleanly on payment + shipping + notification with no orphaned-row leakage. Evidence: `evidence/reliability/chaos-{payment,shipping,notification}-<DATE>.md`.
- **SC-6**: k6 load test holds p95 budgets at every ramp step + the 5× hold for catalog (< 400 ms), search (< 600 ms), checkout (< 1500 ms). Evidence: `evidence/performance/k6-{catalog,search,checkout}-<DATE>.json`.
- **SC-7**: Production smoke returns exit 0 + zero `seed_applied` rows + HTTP 200 from `/health` + matching SHA from `/version`. Evidence: `evidence/production-smoke/smoke-<DATE>.md`.
- **SC-8**: Container health verification + image-tag rollback rehearsal pass on `backend_api`, `admin_web`, Flutter-web with rollback < 5 min and post-rollback health < 30 s. Evidence: `evidence/containers/health-<container>-<DATE>.md` × 3.
- **SC-9**: Section 13 launch-readiness checklist 100% checked, every line evidence-linked + timestamped + signed. Document signed by Product Lead + Engineering Lead + Operations Lead + Security Lead. Evidence: `evidence/launch-readiness/launch-readiness-<DATE>.md`.
- **SC-10**: `impeccable-scan` is merge-blocking on `apps/admin_web` (verified by inspecting the merged threshold-flip PR + the closed rehearsal PR's check history showing red → labeled → green → label-removed → red). Evidence: `evidence/impeccable/promotion-<DATE>.md` + linked PRs.
- **SC-11**: Section 11 DoD audit returns zero missing checkboxes across every Phase 1A–1E module. Evidence: `evidence/dod-audit/dod-audit-<DATE>.md`.
- **SC-12**: Zero open blockers across `phase-1f-blocker`, `loc-ar-blocker`, `rtl-blocker`, `security-blocker`, `launch-blocker` labels at the moment of launch authorization sign-off.
- **SC-13**: SpecKit `feature.json` advanced from `phase-1D` to `specs/phase-1F/029-qa-and-hardening`. Verified by inspecting `.specify/feature.json`.

---

## Dependencies

**Hard dependencies (all must be at DoD before 029 starts substantive QA work):**

- All Phase 1A specs (governance, architecture, shared foundations).
- All Phase 1B specs (identity, catalog, search, pricing-engine, cart-checkout, order, tax-invoice, return-refund).
- All Phase 1C specs (admin shell + per-module admin surfaces, customer storefront, customer mobile).
- All Phase 1D specs (verification, B2B, promotions UX, reviews, support tickets, CMS).
- All Phase 1E specs (E1 infrastructure, 025 notifications, 026 shipping, 027 payments).
- Spec 015 admin_web Next.js scaffold live (required for `impeccable-scan` enforcement target).
- Spec E1 Production ACA stack provisioned in Saudi Arabia Central with the launch candidate image tag deployed.

**External (non-spec) dependency:**

- **Risk 11 — Arabic editorial reviewer named in the Risk Register.** Hard prerequisite for User Story 2 + SC-2 + Section 13 QA line. If unresolved at QA-start, 029 pauses.

**Tooling / infra dependencies (must be available; sourced during 029 setup if not):**

- k6 CLI installed on QA workstation + dashboard backend (Grafana k6 Cloud or self-hosted).
- `gitleaks` CLI.
- OWASP ASVS L1 control checklist (vendored or referenced).
- Provider sandbox accounts for chaos drills: HyperPay, Tap, Paymob, Kashier, Tabby, Tamara, Valu (027); Aramex, SMSA, Mylerz (026); SES, Unifonic, FCM (025).
- Visual-regression tool: Percy (default) or Chromatic — captured per platform.
- Playwright (admin E2E) + `flutter integration_test` (mobile + web) suites already in CI per spec 015.
- Postman collection for backend-API regression already in CI per spec 002.

---

## Phase Assignment

**Phase 1F — Launch Hardening · Milestone 9 · sole spec.**

Exit of this spec = exit of Phase 1F = **launch authorization granted**. Post-launch optimization work moves to Phase 1.5.

---

## Out of Scope (deferred to Phase 1.5)

- WhatsApp notification channel verification (Phase 1.5 per Section 13 Integrations note).
- Multi-vendor onboarding rehearsal (Phase 2).
- OWASP ASVS L2 / L3 controls (Phase 1.5+ — L1 is the launch floor).
- Penetration test (third-party engagement; Phase 1.5).
- Load tests beyond 5× expected launch RPS (Phase 1.5 capacity-planning workstream).
- Localization audit of marketing-site copy (out of platform scope; owned by Marketing).
- Performance regression of admin_web bundle size beyond the impeccable budget (Phase 1.5).
- Long-form Arabic editorial review of blog content beyond launch-day articles (Phase 1.5).
- Full chaos-engineering program (e.g., Litmus / Chaos Mesh integration); 029 covers targeted chaos drills only.
- BCDR full-scale failover rehearsal (cross-region) — out of single-region ADR-010 posture; revisit if multi-region is approved.
