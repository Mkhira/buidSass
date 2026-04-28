# Phase 0 Research: Admin Inventory

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

Resolves every Technical-Context decision in `plan.md`. Inherits unchanged decisions from spec 015 (Next.js App Router, iron-session auth proxy, openapi-typescript, vitest + Playwright + axe + Storybook) and spec 016 (papaparse, IndexedDB-via-idb, Tiptap is **not** in scope here). Only inventory-specific deltas documented.

---

## R1. Calendar — `react-day-picker` + `date-fns` + `date-fns-tz`

- **Decision**: `react-day-picker` ^9 for the on-demand grid view drilling into a specific month, `date-fns` ^3 + `date-fns-tz` ^3 for locale-aware date math + the active warehouse's timezone. The default expiry view is **lane-based** (Near-expiry / Expired / Future), with a "view in calendar" affordance opening a `react-day-picker` grid for the chosen lane's date range.
- **Rationale**: Operations admins scan lanes far more often than they hop between dates. The grid is reserved for "show me what's expiring on this specific date". Day-Picker handles a11y + RTL layout out of the box (its layout uses CSS logical properties).
- **Hijri formatting**: where the active locale's calendar is Hijri (deferred — KSA users may want this in v2), `date-fns-jalali` is **not** the right pick; we'd switch to a per-locale formatter rather than swap the calendar engine. This research only locks the v1 Gregorian path.
- **Alternatives rejected**: `react-calendar` (less actively maintained), `@fullcalendar/react` (heavy, designed for events not expiry buckets), hand-rolled grid (a11y + locale work would dwarf the dep cost).

## R2. Virtualization — `@tanstack/react-virtual`

- **Decision**: `@tanstack/react-virtual` ^3 for the ledger table, the expiry-lane lists at scale, and the reservations table when a warehouse exceeds 5000 active reservations.
- **Rationale**: Tanstack's virtualization primitive composes cleanly with `@tanstack/react-table` (already used in spec 015's `DataTable`). One mental model across both list types.
- **a11y**: Virtualized rows lose semantic position in the DOM tree → ARIA live-region announcement on row activation + manual `aria-rowindex` + `aria-rowcount` to keep screen readers oriented.
- **Alternatives rejected**: `react-window` (less Tanstack-friendly), `react-virtuoso` (different API; not aligned with our table layer).

## R3. SKU lookup + barcode scan affordance

- **Decision**: A `<SkuPicker>` Client Component using shadcn's `Command` (Radix `cmdk`) for keyboard-friendly fuzzy lookup against spec 005's product index. A camera-based barcode scan affordance is exposed via `BarcodeDetector` Web API (Chromium + Edge supported; falls back to manual entry on Firefox / Safari).
- **Rationale**: Warehouse admins frequently pick SKUs from physical labels; barcode scan saves seconds per adjustment and reduces typo errors. `BarcodeDetector` is a browser-native API — no third-party SDK.
- **Permissions**: prompts for camera on first use; the prompt copy explains why.
- **Alternatives rejected**: `quagga2` JS-only barcode library (large bundle, slower than native), USB scanner-only (excludes mobile-tablet warehouse users on iOS — not a v1 platform but acknowledged).

## R4. Reason-code catalog — server-published, client-localized

- **Decision**: Spec 008 publishes the closed enum of reason codes via a lightweight endpoint (`GET /v1/admin/inventory/reason-codes`). The UI fetches once per session, caches in `react-query`. Each code has an i18n key in `messages/{en,ar}.json` plus a `requiresNote: boolean` flag on the client. The server is the source of truth on which codes exist; the client maps each to its localized label + the mandatory-note flag (per Q5 — `theft_loss`, `write_off_below_zero`, `breakage`).
- **Rationale**: New reason codes added by spec 008 surface immediately on next session refresh; no client deploy needed unless the new code requires a mandatory note (in which case the client-side `requiresNote` map needs an update — that's a code change anyway).
- **Alternatives rejected**: hard-coded enum on the client (drifts the moment 008 adds a code), no client validation (every save round-trips to learn it needs a note).

## R5. Export-job poller

- **Decision**: `lib/inventory/export-job-poller.ts` is a `react-query` hook that polls `GET /v1/admin/inventory/ledger/exports/<id>` every 3 s until status is terminal (`done` / `failed`); on `done`, the response carries a presigned download URL. The page-level status widget (`<ExportJobStatus>`) consumes the hook and surfaces queued / in_progress / done / failed UI. When spec 023's bell + email pipeline ships, the same export-job is delivered there too — the page widget remains for admins still in-tab.
- **Rationale**: Aligns with the FR-021 clarification — admins can close the tab and reopen via the export-job permalink. Polling is bounded to the export's natural lifetime (typically < 60 s for 100k-row exports per spec 008's target).
- **Alternatives rejected**: WebSocket push (overkill for a one-off lifecycle), scheduled poll on a background worker (no client need).

## R6. Optimistic concurrency (412 conflict UX)

- **Decision**: Same as 016 — adjustment form, threshold inline editor, batch detail editor read `rowVersion` from spec 008's response and send it back on mutate. A 412 returns the editor to a "another admin updated this stock; reload current numbers?" overlay that preserves the admin's typed delta + reason in a side panel.
- **Rationale**: Prevents silent overwrites. Critical for inventory because two admins can race on the same SKU during a busy receipt session.
- **Alternatives rejected**: pessimistic lock (out of scope for spec 008), last-write-wins (silent data loss).

## R7. Visual-regression coverage extensions

- **Decision**: Add to spec 015's Storybook visual-regression suite the following stories — each in EN-LTR + AR-RTL × {light, dark}:
  - `<AdjustForm>` in idle / submitting / 412-conflict / below-zero-without-permission / below-zero-with-permission / mandatory-note-missing / success states.
  - `<LowStockTable>` empty / 100-row / 1000-row.
  - `<ExpiryCalendar>` with seeded dates spanning each lane (and the calendar-grid drill-in).
  - `<ReservationTable>` empty / dense / mid-release-confirm states.
  - `<LedgerTable>` with virtualization on / off (default on).
  - `<ExportJobStatus>` queued / in_progress / done / failed states.
- **Rationale**: SC-003 + SC-008 carry forward from spec 015. New screens land in the same enforcement.

## R8. CI integration

- **Decision**: No new workflow file. The existing `apps/admin_web-ci.yml` (from spec 015) runs against this branch unchanged. `impeccable-scan` continues to target `apps/admin_web/` PRs (advisory; promoted to merge-blocking in spec 029).
- **Rationale**: Inheriting CI is the point of building inside the shell.

## R9. Telemetry adapter

- **Decision**: Same pattern as 015 / 016. New events listed in `contracts/client-events.md`. PII guard rails identical (no SKU, no warehouse name, no product id values; coarse buckets only).

## R10. SKU-detail page composition

- **Decision**: The SKU detail page is composed from three Server-Component sections (current snapshot, recent 50 ledger rows, active batches) + one Client Component (warehouse switcher) wired via `react-query`. Initial render is a Server Component fetch; subsequent warehouse switches refetch via the client.
- **Rationale**: Matches App Router's preferred mode and keeps the initial page load free of client-bundle weight for the snapshot card.

## R11. Receipt-link rendering

- **Decision**: Receipt references render as a chip linking out to a placeholder route under `/admin/inventory/receipts/<id>` until a future admin-receipts spec ships. The placeholder route surfaces a "receipt detail not yet available" screen with a copy-of-id action.
- **Rationale**: FR-019 requires the link rendering; deferring receipt CRUD to a later spec keeps this spec scoped.

---

## Open follow-ups for downstream specs

- **Spec 008**: confirm the reason-codes endpoint + `requiresNote` semantics. Confirm `rowVersion` on the SKU snapshot. Confirm export-job endpoints (`POST /ledger/exports`, `GET /ledger/exports/<id>`).
- **Spec 008**: confirm reservation-release endpoint accepts a `reason` field that surfaces in the audit before/after.
- **Spec 015**: confirm `FormBuilder` exposes a "side-panel preserve fields on conflict reload" affordance — if not, build it locally and back-port.
- **Spec 023**: when shipped, the export-job download link surfaces in the bell + email pipeline; this spec's page-level status widget remains as the in-tab fallback.
- **Spec 018**: when shipped, `<OwnerLink>` resolves order owners; until then, the chip degrades gracefully ("owner detail not yet available").
- **Spec 020 (verification)**: out of scope here — no inventory action depends on verification status.
