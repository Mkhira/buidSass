---

description: "Tasks for Spec 017 — Admin Inventory"
---

# Tasks: Admin Inventory

**Input**: Design documents from `/specs/phase-1C/017-admin-inventory/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/{consumed-apis.md,routes.md,client-events.md}, quickstart.md

**Tests**: vitest + RTL unit/component, Playwright + Storybook visual regression (SC-003), axe-playwright a11y (SC-008 inherited), Playwright e2e for Stories 1/2/3/4. Inherits spec 015's CI / lint hygiene unchanged.

**Organization**: Tasks grouped by user story. Stories run in priority order P1 → P4.

## Format

`[ID] [P?] [Story] Description (path)`

- `[P]` — parallelizable (different files, no incomplete-task deps)
- `[USn]` — user story (US1 = P1 MVP)

## Path conventions

- App code: `apps/admin_web/`
- Inventory feature lives under `apps/admin_web/app/(admin)/inventory/`, `apps/admin_web/components/inventory/`, `apps/admin_web/lib/inventory/`

---

## Phase 1: Setup (inventory-specific deps + scaffolding)

- [ ] T001 Add inventory deps to `apps/admin_web/package.json`: `react-day-picker ^9`, `date-fns ^3`, `date-fns-tz ^3`, `@tanstack/react-virtual ^3`, `@react-spring/web ^9`. (`papaparse`, `idb`, react-hook-form already present from 016 / 015.)
- [ ] T002 [P] Run `pnpm install` and verify the build still succeeds (`pnpm build`).
- [ ] T003 [P] Add `apps/admin_web/lib/inventory/.gitkeep` and the seven feature dirs under `app/(admin)/inventory/` (stock, adjust, low-stock, batches, expiry, reservations, ledger).
- [ ] T004 [P] Append the 10 inventory permission keys (per `contracts/routes.md`) to spec 015's `apps/admin_web/lib/auth/permissions.ts` map.

---

## Phase 2: Foundational (catalog-specific shared infrastructure)

⚠️ Required before any user-story phase begins.

### Generated client + state machines

- [ ] T005 Extend / create `apps/admin_web/lib/api/clients/inventory.ts` wrapping every spec 008 endpoint used: snapshot / adjust / reason-codes / threshold-edit / batch CRUD / expiry-feed / reservations list / reservation release / ledger list / ledger export create + status + download.
- [ ] T006 Create `apps/admin_web/lib/inventory/adjust-state.ts` implementing SM-1 (`Idle` → `Validating` → `Submitting` → `Submitted` / `ConflictDetected` / `BelowZeroBlocked` / `MissingNoteBlocked` / `Failed` / `FailedTerminal`) per `data-model.md`.
- [ ] T007 [P] Create `apps/admin_web/lib/inventory/batch-lifecycle.ts` (SM-2) — derived view, no async.
- [ ] T008 [P] Create `apps/admin_web/lib/inventory/reservation-lifecycle.ts` (SM-3).
- [ ] T009 [P] Create `apps/admin_web/lib/inventory/export-job-poller.ts` (SM-4) — `react-query` hook polling every 3 s until terminal.

### Reason-codes catalog

- [ ] T010 Create `apps/admin_web/lib/inventory/reason-codes.ts` exporting `useReasonCodes()` (react-query, session-scoped) and `requiresNote(reasonCode)` returning `true` for `theft_loss`, `write_off_below_zero`, `breakage`.
- [ ] T011 [P] Create `apps/admin_web/lib/inventory/reason-codes.test.ts` covering the mandatory-note flag + i18n-key resolution.

### Sidebar contribution + i18n keys

- [ ] T012 [P] Update spec 015's nav-manifest consumer (`apps/admin_web/lib/auth/nav-manifest.ts`) to render the **Inventory** group when the admin's permission set includes any `inventory.*.read` key.
- [ ] T013 [P] Append inventory string keys to `apps/admin_web/messages/en.json` (page titles, reason-code labels, movement-source labels, conflict overlay copy, dialog titles, calendar lane titles).
- [ ] T014 [P] Mirror to `apps/admin_web/messages/ar.json`. Editorial pass deferred to the cross-cutting AR/RTL phase.

### Inventory shared primitives

- [ ] T015 [P] Create `apps/admin_web/components/inventory/shared/warehouse-picker.tsx` (Client Component reading the admin's role-scoped warehouse list).
- [ ] T016 [P] Create `apps/admin_web/components/inventory/shared/sku-picker.tsx` using shadcn `Command` + spec 005's product index, plus the BarcodeDetector affordance behind a `Scan` button.
- [ ] T017 [P] Create `apps/admin_web/components/inventory/shared/stock-snapshot-card.tsx` displaying available / on-hand / reserved with read-only treatment.

### Inventory overview page

- [ ] T018 Create `apps/admin_web/app/(admin)/inventory/layout.tsx` (sub-shell highlighting the inventory sidebar group when the admin is inside `/inventory/*`).
- [ ] T019 Create `apps/admin_web/app/(admin)/inventory/page.tsx` (overview cards linking to sub-modules).

**Checkpoint**: foundation ready.

---

## Phase 3: User Story 1 — Adjust stock with audit-bearing reason (Priority: P1) 🎯 MVP

**Goal**: Admin records a positive / negative / below-zero (with permission) adjustment; ledger + audit + recomputed available-to-sell.

**Independent Test**: Playwright e2e walks all three adjustment paths against the docker-compose backend.

### Adjustment form

- [ ] T020 [US1] Create `apps/admin_web/app/(admin)/inventory/adjust/page.tsx` (Server Component reading optional `?warehouse=…&sku=…` deep-link parameters; mounts the form).
- [ ] T021 [US1] Create `apps/admin_web/components/inventory/adjust/adjust-form.tsx` (Client Component) using spec 015's `FormBuilder` with the zod schema enforcing FR-004 (mandatory note for theft_loss / write_off_below_zero / breakage; optional note otherwise) + FR-005 (block-by-default below zero).
- [ ] T022 [US1] [P] Create `apps/admin_web/components/inventory/adjust/reason-code-picker.tsx` rendering the localized labels from R4's `useReasonCodes()`.
- [ ] T023 [US1] [P] Create `apps/admin_web/components/inventory/adjust/below-zero-confirm-dialog.tsx` per FR-005 (cites "this is a write-off below zero").
- [ ] T024 [US1] [P] Create `apps/admin_web/components/inventory/adjust/conflict-overlay.tsx` for the 412 stale-version flow (R6) — preserves the typed delta + reason in a side panel. Wraps spec 015's shared `<ConflictReloadDialog>` (spec 015 T040b).
- [ ] T025 [US1] [P] Create `apps/admin_web/components/inventory/adjust/draft-persistence.tsx` glue around `idb` so the form survives a tab crash mid-typing on a long write-off note.

### Server-side glue

- [ ] T026 [US1] [P] Create `apps/admin_web/app/api/inventory/adjustments/route.ts` (POST) proxy to spec 008 with idempotency-key forwarding.
- [ ] T027 [US1] [P] Create `apps/admin_web/app/api/inventory/reason-codes/route.ts` (GET) cached at `private, max-age=300`.

### Stock-by-SKU + SKU detail

- [ ] T028 [US1] Create `apps/admin_web/app/(admin)/inventory/stock/page.tsx` (Server Component) with spec 015's `DataTable` listing SKUs with current available across the admin's warehouses.
- [ ] T029 [US1] [P] Create `apps/admin_web/app/(admin)/inventory/stock/[skuId]/page.tsx` composing snapshot card + recent ledger + active batches + warehouse switcher (R10). Per FR-006a, mount `<AuditForResourceLink resourceType="Sku" resourceId={skuId} />` (spec 015 T040e) in the page header.
- [ ] T030 [US1] [P] Create `apps/admin_web/components/inventory/shared/recent-ledger-strip.tsx` (read-only, last 50 rows, deep-links to the full ledger).

### Tests + e2e

- [ ] T031 [US1] [P] Create `tests/unit/inventory/adjust/adjust-form.test.tsx` — required-fields / mandatory-note / below-zero-block-without-permission / below-zero-allow-with-permission / conflict overlay.
- [ ] T032 [US1] [P] Create `tests/unit/inventory/adjust/reason-code-picker.test.tsx`.
- [ ] T033 [US1] [P] Add Storybook stories for `<AdjustForm>` in idle / submitting / 412-conflict / below-zero-without-permission / below-zero-with-permission / mandatory-note-missing / success states.
- [ ] T034 [US1] [P] Create `tests/visual/inventory/adjust.spec.ts` snapshotting the seven story states × locale × theme.
- [ ] T035 [US1] [P] Create `tests/a11y/inventory/adjust.spec.ts` (axe scan + focus order + ARIA live region for the conflict overlay).
- [ ] T036 [US1] Create `e2e/inventory/story1_adjust.spec.ts` covering positive / negative / below-zero-blocked / below-zero-with-permission flows on Chromium + Firefox + WebKit; verifies audit entry appears in spec 015's reader.

**Checkpoint**: US1 (MVP) ships independently. Warehouse ops can run.

---

## Phase 4: User Story 2 — Low-stock queue + threshold tuning (Priority: P2)

**Goal**: queue surfacing SKUs at-or-below threshold with severity sort + inline threshold edit + quick-action to adjustment.

- [ ] T037 [US2] Create `apps/admin_web/app/(admin)/inventory/low-stock/page.tsx` (Server Component renders DataTable + filter set).
- [ ] T038 [US2] Create `apps/admin_web/components/inventory/low-stock/low-stock-table.tsx` consuming spec 015's `DataTable` with severity-sorted rows.
- [ ] T039 [US2] [P] Create `apps/admin_web/components/inventory/low-stock/threshold-inline-editor.tsx` (Client Component) — optimistic update + 412 conflict handling.
- [ ] T040 [US2] [P] Create `apps/admin_web/components/inventory/low-stock/velocity-cell.tsx` rendering 7d/30d/90d velocity sparkline from spec 008.
- [ ] T041 [US2] [P] Create `apps/admin_web/components/inventory/low-stock/open-in-adjust-link.tsx` deep-linking to `/inventory/adjust?warehouse=…&sku=…`.
- [ ] T042 [US2] [P] Create `apps/admin_web/app/api/inventory/thresholds/[skuId]/route.ts` (PATCH) proxy.
- [ ] T043 [US2] [P] Create unit test `tests/unit/inventory/low-stock/threshold-inline-editor.test.tsx` (optimistic + 412 + permission-revoked-mid-edit).
- [ ] T044 [US2] [P] Add Storybook stories + visual snapshots `tests/visual/inventory/low-stock.spec.ts`.
- [ ] T045 [US2] Create `e2e/inventory/story2_low_stock.spec.ts` walking queue → edit threshold → quick-action.

**Checkpoint**: US2 ships on top of US1.

---

## Phase 5: User Story 3 — Batch / lot + expiry calendar (Priority: P3)

**Goal**: batch CRUD + tri-lane expiry calendar + near-expiry alerts.

### Batch CRUD

- [ ] T046 [US3] Create `apps/admin_web/app/(admin)/inventory/batches/page.tsx` (DataTable list filtered by SKU / warehouse / expiry status).
- [ ] T047 [US3] [P] Create `apps/admin_web/app/(admin)/inventory/batches/new/page.tsx` and `[batchId]/page.tsx`. Per FR-006a, mount `<AuditForResourceLink resourceType="Batch" resourceId={batchId} />` (spec 015 T040e) in the batch detail page header.
- [ ] T048 [US3] [P] Create `apps/admin_web/components/inventory/batch/batch-form.tsx` using spec 015's `FormBuilder` with zod schema enforcing `manufacturedOn ≤ expiresOn`.
- [ ] T049 [US3] [P] Create `apps/admin_web/components/inventory/batch/coa-uploader.tsx` reusing spec 016's `<MediaPicker>` patterns over the spec 003 storage abstraction.
- [ ] T050 [US3] [P] Create `apps/admin_web/components/inventory/batch/non-zero-delete-guard-dialog.tsx` per FR-013.
- [ ] T051 [US3] [P] Create `apps/admin_web/app/api/inventory/batches/route.ts` and `[batchId]/route.ts` proxies.

### Expiry calendar

- [ ] T052 [US3] Create `apps/admin_web/app/(admin)/inventory/expiry/page.tsx` (Server Component renders three lanes from a single fetch).
- [ ] T053 [US3] [P] Create `apps/admin_web/components/inventory/expiry/expiry-calendar.tsx` (the lane container) — uses `@tanstack/react-virtual` per lane.
- [ ] T054 [US3] [P] Create `apps/admin_web/components/inventory/expiry/lane.tsx` and `lane-row.tsx`.
- [ ] T055 [US3] [P] Create `apps/admin_web/components/inventory/expiry/calendar-grid.tsx` (drill-in `react-day-picker` view) with a11y keyboard nav (R1 + a11y suite per `tests/a11y/inventory-calendar.spec.ts` below).

### Tests + e2e

- [ ] T056 [US3] [P] Create `tests/unit/inventory/batch/batch-form.test.tsx` (date validation + COA upload mocked).
- [ ] T057 [US3] [P] Create `tests/unit/inventory/expiry/expiry-calendar.test.tsx` (lane assignment, threshold resolution).
- [ ] T058 [US3] [P] Create `tests/a11y/inventory-calendar.spec.ts` covering keyboard navigation over the calendar grid (arrow keys + page-up/down).
- [ ] T059 [US3] [P] Add Storybook stories for `<ExpiryCalendar>` empty / dense / mid-month / cross-month states.
- [ ] T060 [US3] [P] Create `tests/visual/inventory/batches.spec.ts` and `tests/visual/inventory/expiry.spec.ts`.
- [ ] T061 [US3] Create `e2e/inventory/story3_batch_expiry.spec.ts` covering create batch → near-expiry surface → write-off-first guard on delete.

**Checkpoint**: US3 ships independently on top of US1.

---

## Phase 6: User Story 4 — Reservation inspection + manual release (Priority: P4)

**Goal**: list + filter active reservations; release stale ones with audit + drift fall-out.

- [ ] T062 [US4] Create `apps/admin_web/app/(admin)/inventory/reservations/page.tsx` (Server Component renders DataTable + filter set).
- [ ] T063 [US4] Create `apps/admin_web/components/inventory/reservation/reservation-table.tsx` — virtualized rows when warehouse > 5000 active reservations.
- [ ] T064 [US4] [P] Create `apps/admin_web/components/inventory/reservation/owner-link.tsx` resolving cart / order / quote owners; degrades to "owner detail not yet available" when the linked admin spec hasn't shipped. Per FR-006a, mount `<AuditForResourceLink resourceType="Reservation" resourceId={reservationId} />` inside the row's actions menu.
- [ ] T065 [US4] [P] Create `apps/admin_web/components/inventory/reservation/release-confirm-dialog.tsx` per FR-017 (silent-to-customer messaging).
- [ ] T066 [US4] [P] Per FR-016a, create `apps/admin_web/components/inventory/reservation/ttl-cell.tsx` rendering both absolute timestamp + "X minutes remaining". The countdown is computed client-side from the server's `expiresAt` timestamp via a **single 1-Hz table-wide ticker** (not per-row, not server-polled), paused when the tab is hidden via the Page Visibility API. A full re-fetch fires only on tab refocus after > 60 s away or on explicit pull-to-refresh / button click. Test under `tests/unit/inventory/reservation/ttl-cell.test.tsx` covers (a) ticker pauses on `visibilitychange: hidden`, (b) ticker resumes on `visible`, (c) re-focus after 70 s triggers re-fetch, (d) 50-row table fires exactly one ticker, not 50.
- [ ] T067 [US4] [P] Create `apps/admin_web/app/api/inventory/reservations/[reservationId]/release/route.ts` proxy.
- [ ] T068 [US4] [P] Create `tests/unit/inventory/reservation/{reservation-table,owner-link,release-confirm-dialog,ttl-cell}.test.tsx`.
- [ ] T069 [US4] [P] Add Storybook stories + visual snapshots `tests/visual/inventory/reservations.spec.ts`.
- [ ] T070 [US4] Create `e2e/inventory/story4_reservation_release.spec.ts` covering filter → release → audit verification → cross-app drift assertion (uses spec 014's customer e2e harness if available, else stops at the audit-entry verification).

**Checkpoint**: US4 ships independently.

---

## Phase 7: Ledger + export

(Cross-story — supports US1 verification and operations workflows; runs in parallel with US2/US3/US4 once Phase 2 is done.)

- [ ] T071 Create `apps/admin_web/app/(admin)/inventory/ledger/page.tsx` rendering the append-only movements table with cursor-based pagination.
- [ ] T072 [P] Create `apps/admin_web/components/inventory/ledger/ledger-table.tsx` using `@tanstack/react-virtual` for ≥ 1000-row pages.
- [ ] T073 [P] Create `apps/admin_web/components/inventory/ledger/ledger-export-button.tsx` triggering an export-job + opening the status page.
- [ ] T074 [P] Create `apps/admin_web/components/inventory/ledger/export-job-status.tsx` consuming the export-job poller. Wraps spec 015's shared `<ExportJobStatus<TFilterSnapshot>>` (spec 015 T040d) — does not reimplement.
- [ ] T075 Create `apps/admin_web/app/(admin)/inventory/ledger/exports/[jobId]/page.tsx` rendering the same `<ExportJobStatus>` standalone (deep-linkable).
- [ ] T076 [P] Create `apps/admin_web/app/api/inventory/ledger/exports/route.ts` (POST) and `[jobId]/route.ts` (GET) proxies.
- [ ] T077 [P] Create `tests/unit/inventory/ledger/{ledger-table,export-job-status}.test.tsx`.
- [ ] T078 [P] Add Storybook stories for `<LedgerTable>` (virtualized on/off) and `<ExportJobStatus>` (queued / in_progress / done / failed).
- [ ] T079 [P] Create `tests/visual/inventory/ledger.spec.ts`.

**Checkpoint**: Ledger ships alongside the user stories. Auditors can export.

---

## Phase 8: AR/RTL editorial pass (cross-cutting)

- [ ] T080 [MANUAL] [P] Editorial-grade AR translations for every key seeded in T013/T014. **MUST NOT be executed by an autonomous agent.** Constitution Principle 4 forbids machine-translated AR. Reason-code labels and movement-source labels are operationally critical and warrant editorial review (a mistranslated `write_off_below_zero` could cause real money loss). Workflow: agent commits AR keys with `"@@x-source": "EN_PLACEHOLDER"` markers; human translator replaces; CI fails the AR build if any marker remains. `/speckit-implement` MUST stop at this task.
- [ ] T081 [P] Run `pnpm lint:i18n` against the inventory feature; resolve any leak.
- [ ] T082 [P] Re-run all inventory visual snapshots in AR-RTL — fix layout bugs (especially in the calendar lanes and the ledger virtualization).
- [ ] T083 [P] Verify the calendar's date headers and the ledger's date columns format correctly under `ar-SA` and `ar-EG` locales (numerals, day-of-week first letter).

---

## Phase 9: Polish & cross-cutting concerns

- [ ] T084 [P] Run `pnpm test:a11y -- --grep inventory` and resolve every axe violation, especially calendar keyboard nav + virtualized table semantics.
- [ ] T085 [P] Run `pnpm test --coverage -- inventory` and bring branch coverage on `lib/inventory/` and `components/inventory/` to ≥ 90 %.
- [ ] T086 [P] Verify the inventory feature folder adds < 250 KB gzipped to the initial JS bundle on the inventory routes (defer `react-day-picker` calendar + `BarcodeDetector` glue via `next/dynamic` if it spills).
- [ ] T087 [P] Verify the audit-log reader (spec 015) renders the new inventory audit kinds correctly: pick a few seeded events and confirm the JSON diff renders the reason code + note legibly in both locales.
- [ ] T088 [P] Run the SC-005 reconciliation contract test against staging — every `inventory.adjust.success` API response correlates with an audit-log entry within 1 s.
- [ ] T089 [P] Run a 100k-row ledger export against staging and record dry-run + commit timing in PR description (informational; SC envelope check).
- [ ] T090 [P] Verify catalog-specific telemetry events pass the PII guard (`tests/unit/inventory/telemetry.pii-guard.test.ts`).
- [ ] T091 [P] Ensure no direct `fetch('http…')` calls bleed into `components/inventory/` (lint sweep).
- [ ] T091a [P] Append inventory-specific gap rows to `docs/admin_web-escalation-log.md` (file authored in spec 015's T098a). One row per gap.
- [ ] T091b [P] Verify SC-009 ("0 backend contract changes shipped from this spec"). Compute `sha256` of every `services/backend_api/openapi.*.json` file at PR open time and compare against the same checksums on `main` at the branch's merge-base. CI MUST fail if any backend OpenAPI doc changed. Output the comparison table to the PR description.
- [ ] T091c [P] Per FR-016b, create `apps/admin_web/app/(admin)/inventory/receipts/[receiptId]/page.tsx` — the **placeholder route** for receipt-detail until a future admin-receipts spec ships. Renders the receipt id (copyable), a localized "receipt detail not yet available" message, and a back-link to the originating batch detail. Gated on `inventory.batch.read` so navigation never 403s unexpectedly. Storybook story covers EN + AR.
- [ ] T091d [P] Per FR-016c, create localized `messages/{en,ar}.json/inventory.barcode.permission.{prompt,denied,unsupported}` keys. The `<SkuPicker>` barcode-scan affordance reads them when calling `navigator.permissions.query({ name: 'camera' })`. Visual story sweep covers AR + EN for each state.
- [ ] T091e [P] Per FR-021a, append the inventory permission keys to `specs/phase-1C/015-admin-foundation/contracts/permission-catalog.md` if not already present, and ensure `pnpm catalog:check-permissions` (spec 015 T032c) passes.
- [ ] T091f [P] Per FR-006a, verify every inventory resource page (SKU detail, batch detail, reservations) renders `<AuditForResourceLink>` — coverage check via Storybook story sweep.
- [ ] T091g [P] Per spec 015 T032d, author `apps/admin_web/lib/auth/nav-manifest-static/inventory.json` declaring the Inventory group + sub-entries per `contracts/nav-manifest.md` order range 300–399. Ensure `pnpm catalog:check-nav-manifest` (spec 015 T032e) passes.
- [ ] T092 Author DoD checklist evidence for SC-001 → SC-009 in the PR description.
- [ ] T093 Open the PR with: spec link, plan link, story-by-story demos (screen recordings or Storybook links), CI green, fingerprint marker.

---

## Dependencies

| Phase | Depends on |
|---|---|
| Phase 1 (Setup) | spec 015 merged + spec 008 contract merged |
| Phase 2 (Foundational) | Phase 1 |
| Phase 3 (US1) | Phase 2 |
| Phase 4 (US2) | Phase 2 (independent of US1; queue can render with no adjustments yet) |
| Phase 5 (US3) | Phase 2 (independent of US1 / US2) |
| Phase 6 (US4) | Phase 2 (independent of US1 / US2 / US3) |
| Phase 7 (Ledger + export) | Phase 2 (independent; supports US1 verification) |
| Phase 8 (AR/RTL) | Phase 3 + Phase 4 + Phase 5 + Phase 6 + Phase 7 |
| Phase 9 (Polish) | All prior phases |

## Parallel-execution opportunities

- **Phase 2**: T005–T017 are largely file-disjoint; large parallel fan-out for a 3–4 engineer team.
- **Within US1**: form + dialogs + conflict overlay + IndexedDB persistence + stock list + SKU detail + server proxies are independent file scopes.
- **US2 / US3 / US4 can run in parallel** with each other once Phase 2 is complete — different feature folders.
- **Phase 7 (Ledger + export)** runs in parallel with US2 / US3 / US4.

## Suggested MVP scope

**MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — 36 tasks — ships stock adjustment + stock-by-SKU + SKU detail. Operations teams can run on this alone; US2 / US3 / US4 / Ledger ship as independent PRs.

## Format check

All 93 tasks follow `- [ ] Tnnn [P?] [USn?] description (path)` and include explicit file paths. Tests interleaved with implementation per story so each is a vertical-slice PR.
