---

description: "Tasks for Spec 018 — Admin Orders"
---

# Tasks: Admin Orders

**Input**: Design documents from `/specs/phase-1C/018-admin-orders/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/{consumed-apis.md,routes.md,client-events.md,csv-format.md}, quickstart.md

**Tests**: vitest + RTL unit/component, Playwright + Storybook visual regression (SC-003), axe-playwright a11y, Playwright e2e for Stories 1/2/3/4. The "no-403-after-render" contract test (SC-004) walks every transition × permission profile against the gate function. Inherits spec 015's CI / lint hygiene.

**Organization**: Tasks grouped by user story. Stories run in priority order P1 → P4.

## Format

`[ID] [P?] [Story] Description (path)`

- `[P]` — parallelizable
- `[USn]` — user story (US1 = P1 MVP)

## Path conventions

- App code: `apps/admin_web/`
- Orders feature lives under `apps/admin_web/app/(admin)/orders/`, `apps/admin_web/components/orders/`, `apps/admin_web/lib/orders/`

---

## Phase 1: Setup (orders-specific deps + scaffolding)

- [ ] T001 Add no new runtime deps for 018 — every primitive is already in the admin app from specs 015 / 016 / 017. Verify `package.json` resolves cleanly with `pnpm install`.
- [ ] T002 [P] Add the 10 orders permission keys (per `contracts/routes.md`) to spec 015's `apps/admin_web/lib/auth/permissions.ts` map.
- [ ] T003 [P] Add `apps/admin_web/lib/orders/.gitkeep` and the four feature dirs under `app/(admin)/orders/` (`[orderId]`, `[orderId]/refund`, `[orderId]/invoice`, `exports`).
- [ ] T004 [P] Append orders feature flags to `apps/admin_web/lib/env.ts` (the env-typing helper from spec 015): `flags.adminCustomersShipped`, `flags.adminQuotesShipped`, `flags.financeExportEnabled`, plus the per-market step-up thresholds.

---

## Phase 2: Foundational (orders-specific shared infrastructure)

⚠️ Required before any user-story phase begins.

### Generated client + state machines + transition gate

- [ ] T005 Extend / create `apps/admin_web/lib/api/clients/orders.ts` wrapping every spec 011 endpoint: list / detail / timeline (cursor) / state-transition / export-create / export-status / export-download.
- [ ] T006 [P] Create `apps/admin_web/lib/api/clients/invoices.ts` wrapping spec 012 (status / download / regenerate).
- [ ] T007 [P] Create `apps/admin_web/lib/api/clients/returns.ts` wrapping spec 013 refund-create.
- [ ] T008 Create `apps/admin_web/lib/orders/transition-gate.ts` exporting `evaluateTransition(...)` — the pure decision function from research §R1.
- [ ] T009 [P] Create `apps/admin_web/lib/orders/transition-gate.test.ts` covering every (machine × from × to × permission profile) combination.
- [ ] T010 [P] Create `apps/admin_web/lib/orders/refund-state.ts` (SM-1) per `data-model.md`.
- [ ] T011 [P] Create `apps/admin_web/lib/orders/invoice-section-state.ts` (SM-2).
- [ ] T012 [P] Create `apps/admin_web/lib/orders/export-job-poller.ts` (SM-3) — reuses spec 017's polling pattern.
- [ ] T013 [P] Create `apps/admin_web/lib/orders/feature-flags.ts` exporting the flag map per research §R7.

### Sidebar contribution + i18n + permission keys

- [ ] T014 [P] Update spec 015's nav-manifest consumer to render the **Orders** group when the admin's permission set includes any `orders.*.read` key.
- [ ] T015 [P] Append orders i18n keys to `apps/admin_web/messages/en.json` (state-pill labels, transition-action labels, refund placeholders, step-up dialog, invoice-section copy, export-job statuses, customer + source-quote chip placeholder copy).
- [ ] T016 [P] Mirror to `apps/admin_web/messages/ar.json`. Editorial pass deferred to the cross-cutting AR/RTL phase.

### Shared composites

- [ ] T017 [P] Create `apps/admin_web/components/orders/shared/state-pill.tsx` — single state pill consumed in both list (4×) and detail (4×).
- [ ] T018 [P] Create `apps/admin_web/components/orders/shared/conflict-overlay.tsx` for the 412 stale-version flow.
- [ ] T019 [P] Create `apps/admin_web/components/orders/shared/illegal-transition-toast.tsx` for the 409 surface.

### Orders overview

- [ ] T020 Create `apps/admin_web/app/(admin)/orders/layout.tsx` (sub-shell highlighting the orders sidebar group when inside `/orders/*`).

**Checkpoint**: foundation ready.

---

## Phase 3: User Story 1 — Triage and progress an order end-to-end (Priority: P1) 🎯 MVP

**Goal**: list → filter → detail → timeline → state-machine-gated transitions; happy-path fulfillment progression with audit emission.

### Orders list

- [ ] T021 [US1] Create `apps/admin_web/app/(admin)/orders/page.tsx` (Server Component renders DataTable + filter bar + initial cursor page).
- [ ] T022 [US1] Create `apps/admin_web/components/orders/list/orders-table.tsx` wrapping spec 015's `DataTable` (no checkbox column per FR-004 / Q3 clarification).
- [ ] T023 [US1] [P] Create `apps/admin_web/components/orders/list/filter-bar.tsx` — 4 multi-select state filters + market + B2B tristate + date range; URL-synced.
- [ ] T024 [US1] [P] Create `apps/admin_web/components/orders/list/four-state-pill-row.tsx` rendering the four independent signals (FR-005).
- [ ] T025 [US1] [P] Create `apps/admin_web/components/orders/list/customer-chip.tsx` honouring `flags.adminCustomersShipped` + the placeholder dialog (Q4).
- [ ] T026 [US1] [P] Create `tests/unit/orders/list/{orders-table,filter-bar,four-state-pill-row,customer-chip}.test.tsx`.
- [ ] T027 [US1] [P] Add Storybook stories for the list (empty / dense / saved-views applied) in EN-LTR + AR-RTL × {light, dark}.
- [ ] T028 [US1] [P] Create `tests/visual/orders/list.spec.ts`.

### Order detail

- [ ] T029 [US1] Create `apps/admin_web/app/(admin)/orders/[orderId]/page.tsx` (Server Component composes header + cards + timeline + actions).
- [ ] T030 [US1] [P] Create `apps/admin_web/components/orders/detail/detail-header.tsx`, `customer-card.tsx`, `shipping-address-card.tsx`, `payment-summary-card.tsx`, `line-items-table.tsx`, `shipments-card.tsx`, `totals-breakdown.tsx`.
- [ ] T031 [US1] Create `apps/admin_web/components/orders/detail/timeline.tsx` — virtualized 4-stream view per research §R2; stream-filter chips.
- [ ] T032 [US1] [P] Create `apps/admin_web/components/orders/detail/timeline-entry.tsx` rendering one entry with stream colour + actor + permalink.
- [ ] T033 [US1] Create `apps/admin_web/components/orders/detail/transition-action-bar.tsx` consuming `evaluateTransition` for each candidate transition; renders only `kind: 'render'` decisions (FR-010).
- [ ] T034 [US1] [P] Create `apps/admin_web/components/orders/detail/transition-action-button.tsx` (handles click → optimistic UI → audit appears in timeline).
- [ ] T035 [US1] [P] Create `apps/admin_web/components/orders/detail/source-quote-chip.tsx` honouring `flags.adminQuotesShipped` + the conditional permission gate (Q5).
- [ ] T036 [US1] [P] Create `apps/admin_web/app/api/orders/[orderId]/transitions/[machine]/route.ts` proxy for state-transition mutations.
- [ ] T037 [US1] [P] Create `tests/unit/orders/detail/{detail-header,customer-card,line-items-table,timeline,transition-action-bar,source-quote-chip}.test.tsx`.
- [ ] T038 [US1] [P] Add Storybook stories for detail in default / restricted / cancelled-and-refunded / many-timeline-entries states × locale × theme.
- [ ] T039 [US1] [P] Create `tests/visual/orders/detail.spec.ts`.

### SC-004 contract test

- [ ] T040 [US1] Create `tests/contract/orders/no-403-after-render.spec.ts` asserting that for every (machine × fromState × toState × representative permission profile) combination, the gate's decision matches what spec 011's server would return.

### Story 1 e2e

- [ ] T041 [US1] Create `e2e/orders/story1_list_detail_transitions.spec.ts` running list → filter → open detail → progress fulfillment (placed → packed → handed-to-carrier → delivered) on Chromium + Firefox + WebKit; verifies audit entries appear in spec 015's reader.

**Checkpoint**: US1 (MVP) ships independently. Fulfillment + customer-support teams can run.

---

## Phase 4: User Story 2 — Refund initiation (Priority: P2)

**Goal**: line-level refund draft with eager over-refund check + step-up MFA above threshold.

### Refund flow

- [ ] T042 [US2] Create `apps/admin_web/app/(admin)/orders/[orderId]/refund/page.tsx` (intercepting route — side-panel over detail, standalone on direct nav).
- [ ] T043 [US2] Create `apps/admin_web/components/orders/refund/refund-draft-form.tsx` using `react-hook-form` + the zod schema from research §R3.
- [ ] T044 [US2] [P] Create `apps/admin_web/components/orders/refund/line-refund-row.tsx` (per-line qty + amount editor with per-line cap from `deliveredQty - alreadyRefundedQty`).
- [ ] T045 [US2] [P] Create `apps/admin_web/components/orders/refund/over-refund-warning.tsx` rendered inline when the eager guard would block.
- [ ] T046 [US2] [P] Create `apps/admin_web/components/shell/step-up-dialog.tsx` wrapping spec 004's step-up flow (research §R4). Calls `/api/auth/step-up/start` + `/api/auth/step-up/complete`. **Place in the shell, not in `components/orders/`** — spec 019 (admin-customers) account-actions reuses this same dialog. Refund flow imports it from the shell. (Originally scoped under `components/orders/refund/`; relocated post-`/speckit-analyze` so 019 doesn't duplicate; the canonical task is now spec 015 T040c which this references.)
- [ ] T046a [US2] [P] Per FR-015a, when the refund-form submit lands after the step-up assertion's TTL elapsed (server returns a fresh `step_up_required` response), the form MUST re-prompt `<StepUpDialog>` while preserving the in-flight refund draft + idempotency key. Test under `tests/unit/orders/refund/step-up-ttl-reprompt.test.tsx` simulates a 5-minute-stale assertion and asserts the form re-prompts and the next submit carries the new assertion id. The form MUST NOT silently drop a refund attempt because of TTL expiry.
- [ ] T047 [US2] [P] Create `apps/admin_web/app/api/orders/[orderId]/refund/route.ts` proxy. Forwards `X-StepUp-Assertion` + `Idempotency-Key` headers to spec 013.
- [ ] T048 [US2] [P] Create `apps/admin_web/app/api/auth/step-up/start/route.ts` and `complete/route.ts` proxies (if not already shipped by spec 015).
- [ ] T049 [US2] [P] Create `tests/unit/orders/refund/{refund-draft-form,line-refund-row,over-refund-warning,step-up-dialog}.test.tsx`.
- [ ] T050 [US2] [P] Add Storybook stories for the refund form: idle / over-refund-blocked / step-up-required / submitting / submitted / 412-conflict.
- [ ] T051 [US2] [P] Create `tests/visual/orders/refund.spec.ts`.
- [ ] T052 [US2] [P] Create `tests/a11y/orders/refund.spec.ts` covering focus management on the step-up dialog + ARIA wiring on the line picker.
- [ ] T053 [US2] Create `e2e/orders/story2_refund.spec.ts` covering: partial refund below threshold (no step-up), full refund (step-up triggers), over-refund (server-side block), 412 reload.

### Cancel order (FR-012a)

- [ ] T053a [US2] [P] Per FR-012a, create `apps/admin_web/components/orders/detail/cancel-order-button.tsx` rendering inside the transition-action-bar when (a) the actor holds `orders.cancel` AND (b) spec 011's order state machine permits cancellation from the current state. Hidden otherwise (per FR-010 hide-not-disable). On click, opens `<CancelOrderDialog>` (T053b).
- [ ] T053b [US2] [P] Create `apps/admin_web/components/orders/detail/cancel-order-dialog.tsx` capturing the mandatory reason note (≥ 10 chars, ≤ 2000) + warning copy: "any captured payment will require a separate refund (FR-013); reserved inventory is released by spec 011's cancel handler." Confirms via `<Confirmation>` (spec 015 T040). On submit, posts to the cancel-order proxy (T053c) with idempotency key + reason note.
- [ ] T053c [US2] [P] Create `apps/admin_web/app/api/orders/[orderId]/cancel/route.ts` proxy. Forwards `Idempotency-Key` + reason note JSON body to spec 011's cancel endpoint. Per `contracts/audit-redaction.md`, the cancel reason note path (`cancel.reasonNote`) is registered for redaction.
- [ ] T053d [US2] [P] Create `tests/unit/orders/detail/{cancel-order-button,cancel-order-dialog}.test.tsx` covering hide-when-permission-missing, hide-when-state-disallowed, success, 412 conflict.
- [ ] T053e [US2] [P] Add Storybook stories for the cancel dialog: idle / submitting / 412-conflict / failed.
- [ ] T053f [US2] Extend `e2e/orders/story1_list_detail_transitions.spec.ts` (or add `story2b_cancel.spec.ts` if size grows) to walk a captured order → cancel → verify audit entry → verify refund-flow CTA appears as a follow-up affordance.

**Checkpoint**: US2 ships on top of US1.

---

## Phase 5: User Story 3 — Invoice reprint + source-quote chip (Priority: P3)

**Goal**: download / regenerate the latest invoice; source-quote chip resolves correctly.

- [ ] T054 [US3] Create `apps/admin_web/app/(admin)/orders/[orderId]/invoice/page.tsx` (Server Component renders the section as a drilldown).
- [ ] T055 [US3] Create `apps/admin_web/components/orders/invoice/invoice-section.tsx` rendering the three states (Pending / Available / Failed) per research §R5.
- [ ] T056 [US3] [P] Create `apps/admin_web/components/orders/invoice/invoice-status-pill.tsx`.
- [ ] T057 [US3] [P] Create `apps/admin_web/app/api/orders/[orderId]/invoice/download/route.ts` proxy (signed-URL fetch + stream-back).
- [ ] T058 [US3] [P] Create `apps/admin_web/app/api/orders/[orderId]/invoice/regenerate/route.ts` proxy.
- [ ] T059 [US3] [P] Create `tests/unit/orders/invoice/{invoice-section,invoice-status-pill}.test.tsx`.
- [ ] T060 [US3] [P] Add Storybook stories + visual snapshots `tests/visual/orders/invoice.spec.ts`.
- [ ] T061 [US3] Create `e2e/orders/story3_invoice.spec.ts` (download + regenerate flow).

**Checkpoint**: US3 ships independently on top of US1.

---

## Phase 6: User Story 4 — Finance CSV export (Priority: P4)

**Goal**: filter snapshot → async export job → download.

- [ ] T062 [US4] Create `apps/admin_web/app/(admin)/orders/exports/page.tsx` (Server Component DataTable of recent jobs).
- [ ] T063 [US4] [P] Create `apps/admin_web/components/orders/exports/exports-table.tsx`.
- [ ] T064 [US4] Create `apps/admin_web/app/(admin)/orders/exports/[jobId]/page.tsx` rendering filter snapshot + status + download.
- [ ] T065 [US4] [P] Create `apps/admin_web/components/orders/exports/filter-snapshot-card.tsx` (renders `OrdersListFilters` read-only).
- [ ] T066 [US4] [P] Create `apps/admin_web/components/orders/exports/export-job-status.tsx` consuming the export-job poller (T012).
- [ ] T067 [US4] [P] Create `apps/admin_web/app/api/orders/exports/route.ts` (POST create) + `[jobId]/route.ts` (GET status) + `[jobId]/download/route.ts` (signed-URL stream-back).
- [ ] T068 [US4] [P] Wire the **Export** button on the list page (added in T021/T023's filter-bar) to call `POST /api/orders/exports` with the current filter set and redirect to the job-detail page.
- [ ] T069 [US4] [P] Create `tests/unit/orders/exports/{exports-table,filter-snapshot-card,export-job-status}.test.tsx`.
- [ ] T070 [US4] [P] Add Storybook stories for the wizard's three states (queued / in_progress / done / failed) + visual snapshots `tests/visual/orders/exports.spec.ts`.
- [ ] T071 [US4] Create `e2e/orders/story4_export.spec.ts` covering filter → export → wait → download → CSV-content shape check.

**Checkpoint**: US4 ships independently.

---

## Phase 7: AR/RTL editorial pass (cross-cutting)

- [ ] T072 [MANUAL] [P] Editorial-grade AR translations for every key seeded in T015/T016. **MUST NOT be executed by an autonomous agent.** Constitution Principle 4 forbids machine-translated AR. State-pill labels (`order.placed`, `payment.captured`, `fulfillment.handed_to_carrier`, `refund.partial`) and refund-flow copy need human review — a mistranslated state could mask a real transition. Workflow: agent commits AR keys with `"@@x-source": "EN_PLACEHOLDER"` markers; human translator replaces; CI fails the AR build if any marker remains. `/speckit-implement` MUST stop at this task.
- [ ] T073 [P] Run `pnpm lint:i18n` against the orders feature; resolve any leak.
- [ ] T074 [P] Re-run all orders visual snapshots in AR-RTL — fix layout bugs (especially in the timeline column widths and the four-pill row).
- [ ] T075 [P] Verify state-pill labels render correctly under `ar-SA` and `ar-EG` locales — including the four-stream chip filters on the timeline.

---

## Phase 8: Polish & cross-cutting concerns

- [ ] T076 [P] Run `pnpm test:a11y -- --grep orders` and resolve every axe violation, with explicit attention to the timeline (virtualized list semantics) + step-up dialog (focus management).
- [ ] T077 [P] Run `pnpm test --coverage -- orders` and bring branch coverage on `lib/orders/` and `components/orders/` to ≥ 90 %.
- [ ] T078 [P] Verify the orders feature folder adds < 200 KB gzipped to the initial JS bundle on the orders routes (defer step-up dialog + export-job poller via `next/dynamic` if it spills).
- [ ] T079 [P] Run the SC-004 no-403-after-render contract test against staging — no transition action is rendered enabled when the server would return 403 for the actor.
- [ ] T080 [P] Verify the audit-log reader (spec 015) renders the new orders audit kinds correctly: pick a few seeded events (transition + refund + invoice-regenerate + export) and confirm the JSON diff renders the relevant fields legibly in both locales.
- [ ] T081 [P] Verify orders telemetry events pass the PII guard (`tests/unit/orders/telemetry.pii-guard.test.ts`).
- [ ] T082 [P] Ensure no direct `fetch('http…')` calls bleed into `components/orders/` (lint sweep).
- [ ] T083 [P] Verify the `flags.financeExportEnabled` flag correctly hides the Export button when off — covers the case where spec 011's CSV schema isn't yet published.
- [ ] T083a [P] Append orders-specific gap rows to `docs/admin_web-escalation-log.md` (file authored in spec 015's T098a). One row per gap.
- [ ] T083b [P] Verify SC-007 ("0 backend contract changes shipped from this spec"). Compute `sha256` of every `services/backend_api/openapi.*.json` file at PR open time and compare against the same checksums on `main` at the branch's merge-base. CI MUST fail if any backend OpenAPI doc changed. Output the comparison table to the PR description.
- [ ] T083c [P] Per FR-009a, mount `<AuditForResourceLink resourceType="Order" resourceId={orderId} />` (spec 015 T040e) in the order-detail page header. Storybook story covers permitted + not-permitted states.
- [ ] T083d [P] Per FR-023a, append the orders permission keys (including the new `orders.cancel` from FR-012a and the existing `orders.payment.write`) to `specs/phase-1C/015-admin-foundation/contracts/permission-catalog.md` if not already present. Ensure `pnpm catalog:check-permissions` (spec 015 T032c) passes.
- [ ] T083e [P] Per FR-001 (post-third-pass — Drafts removed), confirm no sidebar entry references "Drafts" anywhere in `messages/{en,ar}.json` or in `nav-manifest-static/orders.json`. Add a unit test under `tests/unit/orders/nav.test.ts` asserting only `Orders`, `Refunds` (filtered alias), and `Exports` sub-entries appear.
- [ ] T083f [P] Per spec 015 T032d, author `apps/admin_web/lib/auth/nav-manifest-static/orders.json` declaring the Orders group + sub-entries (`Orders` → `/orders`, `Refunds` → `/orders?refundFilter=any`, `Exports` → `/orders/exports`) per `contracts/nav-manifest.md` order range 400–499. The Refunds entry uses the same `/orders` route with the preset filter chip (per `contracts/routes.md`); no separate page. Ensure `pnpm catalog:check-nav-manifest` (spec 015 T032e) passes.
- [ ] T083g [P] Per FR-022a, ensure the refund and cancel reason-note paths are registered in `contracts/audit-redaction.md` (already done) and that the audit-log reader's JSON viewer (spec 015 T074a) correctly redacts them for an admin holding `audit.read` but lacking `orders.refund.initiate` / `orders.cancel`. Add a fixture-driven test.
- [ ] T084 Author DoD checklist evidence for SC-001 → SC-008 in the PR description.
- [ ] T085 Open the PR with: spec link, plan link, story-by-story demos (screen recordings or Storybook links), CI green, fingerprint marker.

---

## Dependencies

| Phase | Depends on |
|---|---|
| Phase 1 (Setup) | spec 015 merged + specs 011 / 012 / 013 contracts merged |
| Phase 2 (Foundational) | Phase 1 |
| Phase 3 (US1) | Phase 2 |
| Phase 4 (US2) | Phase 2 + Phase 3 (refund flow opens from order detail) |
| Phase 5 (US3) | Phase 2 + Phase 3 (invoice section drilldown lives on the order detail) |
| Phase 6 (US4) | Phase 2 + Phase 3 (export button is on the list page) |
| Phase 7 (AR/RTL) | Phase 3 + Phase 4 + Phase 5 + Phase 6 |
| Phase 8 (Polish) | All prior phases |

## Parallel-execution opportunities

- **Phase 2**: T005–T019 are largely file-disjoint; large parallel fan-out for a 3–4 engineer team.
- **Within US1**: list / filter-bar / four-state-pill / customer-chip / detail cards / timeline / transition-action-bar / source-quote chip / API proxy are independent file scopes.
- **US2 / US3 / US4 can run in parallel** with each other once Phase 3 ships — different feature folders.

## Suggested MVP scope

**MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — 41 tasks — ships list + detail + state-machine-gated transitions. Fulfillment + customer-support teams can run on this alone.

## Format check

All 85 tasks follow `- [ ] Tnnn [P?] [USn?] description (path)` and include explicit file paths.
