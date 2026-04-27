# Implementation Plan: Admin Inventory

**Branch**: `phase-1C-specs` | **Date**: 2026-04-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1C/017-admin-inventory/spec.md`

## Summary

Mount the **inventory operations module** inside spec 015's admin shell — Stock-by-SKU, Adjust stock, Low-stock queue, Batches & lots, Expiry calendar, Reservations, Ledger. Lane B: UI only — every backend gap escalates to spec 008. Inherits the shell, auth proxy, `DataTable`, `FormBuilder`, audit-log read surface, AR-RTL plumbing, telemetry adapter, and CI hygiene from spec 015.

The adjustment form blocks below-zero by default (FR-005) — only `inventory.writeoff_below_zero` permission can override and only with a mandatory note. Reason codes `theft_loss`, `write_off_below_zero`, `breakage` require a non-empty note (≥ 10 chars). Reservation release is silent — drift surfaces naturally on the customer's next interaction via spec 010. Expiry threshold is per-warehouse on a global default of 30 days. Ledger export beyond 50k rows runs asynchronously through an `ExportJob`, surfaced first as a page-level status widget and later via spec 023's email + bell pipeline.

The expiry calendar is a tri-lane view (Near-expiry / Expired / Future) with a virtualized list per lane. The ledger uses cursor-based pagination + a Web-Streams CSV export. Reservation inspection deep-links into spec 015's customer detail and spec 018's order detail, degrading gracefully when those specs haven't shipped.

## Technical Context

**Language/Version**: TypeScript 5.5, Node.js 20 LTS (inherits spec 015's runtime).

**Primary Dependencies** (deltas on top of spec 015's stack):

- `react-day-picker` ^9 + `date-fns` ^3 + `date-fns-tz` ^3 — calendar grid for the expiry view, locale-aware formatting (Hijri-aware where the active locale demands it).
- `@tanstack/react-virtual` ^3 — virtualized list rows for ledger and the expiry-lane lists at scale (100k SKUs × 10 warehouses target).
- `papaparse` ^5 — CSV streaming + report parsing (already in 016 — same dep, no double-install).
- `react-hook-form` ^7 + `zod` ^3 — adjustment form, batch form, threshold inline editor (inherits 015 baseline).
- `@react-spring/web` ^9 — micro-animations on the low-stock severity rank changes (optional polish; keeps the eye on movements during a batch update).
- All other deps inherited from spec 015 / 016 (Next.js, react-query, react-table, next-intl, iron-session, shadcn/ui, etc.).

**Storage**: No new server-side persistence introduced by this spec. Client-side: react-query cache for list/detail; transient `IndexedDB` (via `idb` ^8 — already in 016) only to persist in-flight adjustment-form drafts when the admin survives a tab crash mid-typing on a long write-off note. No tokens, no PII in IndexedDB.

**Testing**:

- Unit + component (vitest + RTL) — every adjustment-form variant, threshold inline editor, batch form, reservation table, ledger row, expiry-lane row.
- Visual regression (Playwright + Storybook snapshots) — every inventory screen × {EN-LTR, AR-RTL} × {light, dark}.
- A11y (axe-playwright) — every inventory screen, with explicit checks for the calendar grid (keyboard navigation between days) and the inline threshold editor (focus retention + ARIA live region for the optimistic update).
- E2E (Playwright) — Story 1 (positive + negative + below-zero adjustment), Story 2 (queue → threshold edit), Story 3 (batch create + near-expiry view), Story 4 (reservation release).
- A reconciliation contract test asserts every `inventory.adjust.success` API response correlates with an audit-log entry within 1 s on staging (SC-005).

**Target Platform**: Same as spec 015 — modern desktop browsers ≥ 1280 px wide.

**Project Type**: Next.js admin web feature folder under `apps/admin_web/app/(admin)/inventory/` and `apps/admin_web/components/inventory/`. No new app or package.

**Performance Goals**:

- Adjustment-form save median ≤ 500 ms client-side latency on a typical SKU lookup (SC-001 envelope).
- Low-stock queue first page ≤ 1 s on staging dataset 100k SKUs × 10 warehouses (SC-004).
- Reservation release median ≤ 1 s end-to-end (SC-007).

**Constraints**:

- **No backend code in this PR** (FR-022). Gaps escalate to spec 008.
- **No client-side fetch outside `lib/api/`** (inherits spec 015's lint).
- **No hard-coded user-facing strings** outside `messages/{en,ar}.json` (inherits spec 015's i18n lint).
- **No raw provider URLs** for batch documents (FR-012). All certificate-of-analysis downloads go through the storage abstraction.
- **No bulk mutations** in v1 — every adjustment is a single SKU/warehouse/delta. Bulk operations (cycle counts, multi-SKU transfers) are out of scope per Assumptions.
- **Block-by-default below-zero**: client validates eagerly; server is the source of truth (FR-005).

**Scale/Scope**: ~9 inventory pages (Stock-by-SKU, Adjust, Low-stock, Batches list/detail, Expiry calendar, Reservations, Ledger, SKU detail). 4 prioritized user stories, 25 functional requirements, 9 success criteria, 5 clarifications integrated. Storybook target: ~20 stories on top of spec 015's baseline.

## Constitution Check

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Customer browse / view price unaffected — admin side. | PASS (n/a) |
| P4 Arabic / RTL editorial | Every inventory screen ships AR + EN with RTL via spec 015's i18n stack. Reason codes + movement-source labels are localized via i18n keys (FR-025). | PASS |
| P5 Market Configuration | Warehouse picker drives market scope; admins see only their warehouses. No hard-coded market literals in UI logic. | PASS |
| P6 Multi-vendor-ready | Forward-compatible. When spec 008 gains vendor scope (Phase 2), the warehouse + SKU picker render whatever the server sends. | PASS |
| P7 Branding | Tokens consumed from `packages/design_system`. No inline hex literals. | PASS |
| P11 Inventory depth | Spec covers stock tracking, warehouse readiness, batch / lot, expiry, low-stock alerts, available-to-sell, reservation/revalidation. Hits the principle squarely. | PASS |
| P22 Fixed Tech | Next.js + shadcn/ui per ADR-006. | PASS |
| P23 Architecture | Spec 015's modular shell + this feature folder. No new service. | PASS |
| P24 State Machines | Adjustment-form state (Idle / Submitting / ConflictDetected / FailedRecoverable), batch lifecycle (Active / NearExpiry / Expired / WrittenOff), reservation lifecycle (Active / Released / Expired) — all documented in `data-model.md`. | PASS |
| P25 Data & Audit | Every mutation (adjust / threshold edit / batch create / reservation release) emits an audit event server-side via spec 008, surfaced through spec 015's reader (FR-006, FR-010, FR-012, FR-017). SC-005 makes the bar explicit. | PASS |
| P27 UX Quality | Every screen ships loading / empty / error / restricted / conflict / locale-switch states (FR-024). Calendar + reservations table + ledger have explicit empty states (no data the admin's scope grants). | PASS |
| P28 AI-Build Standard | Spec ships explicit FRs, scenarios, edge cases, success criteria, 5 resolved clarifications. | PASS |
| P29 Required Spec Output | All 12 sections present. | PASS |
| P30 Phasing | Phase 1C Milestone 5/6. Depends on spec 008 contract merged + spec 015 shipped. | PASS |
| P31 Constitution Supremacy | No conflicts. | PASS |
| ADR-001 Monorepo | Code lives under `apps/admin_web/` only. | PASS |
| ADR-006 Next.js + shadcn/ui | Locked. | PASS |
| ADR-010 KSA residency | All API calls hit the backend in Azure Saudi Arabia Central. Storage abstraction in the same region. | PASS |

**No violations.**

## Project Structure

### Documentation (this feature)

```text
specs/phase-1C/017-admin-inventory/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── consumed-apis.md
│   ├── routes.md
│   └── client-events.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
apps/admin_web/
├── app/(admin)/inventory/
│   ├── layout.tsx                       # Sub-shell highlighting the inventory sidebar group
│   ├── page.tsx                         # Inventory overview cards
│   ├── stock/
│   │   ├── page.tsx                     # Stock-by-SKU list (DataTable)
│   │   └── [skuId]/page.tsx             # SKU detail (current + recent ledger + active batches)
│   ├── adjust/
│   │   └── page.tsx                     # Adjustment form (deep-linkable: ?warehouse=…&sku=…)
│   ├── low-stock/
│   │   └── page.tsx                     # Low-stock queue
│   ├── batches/
│   │   ├── page.tsx                     # Batches list
│   │   ├── new/page.tsx                 # Batch create
│   │   └── [batchId]/page.tsx           # Batch detail (incl. document download)
│   ├── expiry/
│   │   └── page.tsx                     # Tri-lane calendar
│   ├── reservations/
│   │   └── page.tsx                     # Reservations table + filters + manual release
│   └── ledger/
│       ├── page.tsx                     # Append-only movements
│       └── exports/[jobId]/page.tsx     # Export-job status (queued / in_progress / done / failed)
├── components/inventory/
│   ├── adjust/
│   │   ├── adjust-form.tsx              # Tabbed AR/EN note via FormBuilder
│   │   ├── reason-code-picker.tsx
│   │   ├── below-zero-confirm-dialog.tsx
│   │   └── conflict-overlay.tsx         # 412 stale-version flow
│   ├── low-stock/
│   │   ├── low-stock-table.tsx
│   │   ├── threshold-inline-editor.tsx
│   │   └── velocity-cell.tsx
│   ├── batch/
│   │   ├── batch-form.tsx
│   │   └── coa-uploader.tsx             # Cert-of-analysis via storage abstraction
│   ├── expiry/
│   │   ├── expiry-calendar.tsx
│   │   ├── lane.tsx                     # virtualized list per lane
│   │   └── lane-row.tsx
│   ├── reservation/
│   │   ├── reservation-table.tsx
│   │   ├── owner-link.tsx               # Deep-link to cart / order / quote
│   │   └── release-confirm-dialog.tsx
│   ├── ledger/
│   │   ├── ledger-table.tsx
│   │   ├── ledger-export-button.tsx
│   │   └── export-job-status.tsx        # Polled status widget
│   └── shared/
│       ├── warehouse-picker.tsx
│       ├── sku-picker.tsx               # SKU lookup + barcode scan affordance (browser camera scoped)
│       └── stock-snapshot-card.tsx      # available / on-hand / reserved
├── lib/inventory/
│   ├── adjust-state.ts                  # SM-1
│   ├── batch-lifecycle.ts               # SM-2
│   ├── reservation-lifecycle.ts         # SM-3
│   ├── export-job-poller.ts
│   ├── reason-codes.ts                  # i18n key + mandatory-note flag map
│   └── api.ts                           # react-query hooks wrapping spec 008 client
└── tests/
    ├── unit/inventory/...
    ├── visual/inventory.spec.ts
    └── a11y/inventory-calendar.spec.ts  # Keyboard nav over the calendar grid
```

**Structure Decision**: One feature folder per page (`stock`, `adjust`, `low-stock`, `batches`, `expiry`, `reservations`, `ledger`) under the existing `app/(admin)/inventory/` route group. Components live under `components/inventory/<page>/` mirroring the route structure. `lib/inventory/` holds inventory-specific shared logic (state machines, reason-code map, export-job poller) but never mounts UI. Shell + DataTable + FormBuilder come from spec 015; CSV / IndexedDB / Uppy components come from spec 016 unchanged.

## Complexity Tracking

| Choice | Why | Simpler alternative rejected because |
|---|---|---|
| Tri-lane expiry calendar with `react-day-picker` per lane | Operations teams scan lanes more often than discrete dates; the lane view collapses to a calendar grid only on demand. | A single calendar grid would force admins to scroll month-by-month to find what's near-expiry. |
| Virtualized lists (`@tanstack/react-virtual`) on ledger + expiry lanes | 100k-row ledgers and dense expiry months blow naive React rendering past frame budget. | Server-side pagination alone hides the visual shape of "what's expiring in the next 30 days". |
| `IndexedDB` for in-flight adjustment-form drafts | A long mandatory write-off note shouldn't be lost to a tab crash mid-typing. Persisted state holds only form fields, no tokens. | localStorage is too small / synchronous; in-memory loses on refresh. |
| Block-by-default below-zero with permission override | Compliance-default-on. Admins who genuinely need to write off below zero have an explicit permission path with a mandatory note. | Soft warnings get clicked through; the cost of an accidental over-write of stock is operationally large. |
| Async ledger export with page-level status widget (until 023 ships) | A 100k+-row export shouldn't hold the browser open. The export-job model also lets the admin close the tab and come back. | Long-running synchronous downloads are unreliable on flaky LAN connections. |
| Calendar keyboard navigation explicit in a11y suite | WCAG calendar grids are notoriously hard to get right; a dedicated test slot prevents regressions. | Generic axe scan misses arrow-key keyboard reorder regressions. |
