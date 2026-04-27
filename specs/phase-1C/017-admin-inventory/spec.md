# Feature Specification: Admin Inventory

**Feature Branch**: `phase-1C-specs`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "Spec 017 admin-inventory (Phase 1C) — Next.js admin web feature mounting inside spec 015's shell. Per docs/implementation-plan.md §Phase 1C item 017: depends on spec 008 contract merged to main + spec 015. Exit: stock adjustments, low-stock queue, batch/lot, expiry, reservation inspection. Constitution P11 inventory depth: stock tracking, warehouse readiness, branch readiness, batch/lot, expiry, low-stock alerts, available-to-sell, reservation/revalidation. P25 audit on every inventory mutation. UI only — escalate gaps to spec 008."

## Clarifications

### Session 2026-04-27

- Q: When an admin manually releases a stale reservation, should the cart owner be notified? → A: **Silent release**. The release commits, an audit event is emitted, and available-to-sell recomputes. The customer's next interaction (cart view / checkout) surfaces drift naturally through spec 010's drift detection — no admin-driven push to the customer. Avoids a notification firehose for a frequent operations action.
- Q: What is the default policy for adjustments that would drive on-hand below zero? → A: **Block by default**. Only the `inventory.writeoff_below_zero` permission can override, and only with a mandatory free-text note + a confirmation dialog citing "this is a write-off below zero". Standard `inventory.adjust` permission cannot cross zero under any reason code.
- Q: Where does the near-expiry threshold live (per-SKU, per-warehouse, per-market, or global)? → A: **Per-warehouse override on a global default of 30 days**. The expiry calendar uses the warehouse's threshold when set; otherwise the global default. Per-SKU thresholds are not supported in v1 (too granular for warehouse-led ops); per-market is too coarse for the dental-supply mix.
- Q: For asynchronous ledger exports (> 50k rows), where is the download link delivered? → A: **Email + in-app notification (bell)**. The Ledger page surfaces a small "we'll notify you when ready" status widget in the meantime; when spec 023 (notifications) ships its bell + email pipeline, the link lands there. Until 023 ships, the page-level status widget is the only delivery channel and the export persists across page refreshes via the export-job id.
- Q: Should some reason codes require a mandatory free-text note on stock adjustments? → A: **Yes** — `theft_loss`, `write_off_below_zero`, and `breakage` require a non-empty note (≥ 10 chars). The form blocks save with a localized error if the note is missing for these codes. Other reason codes keep the note as optional.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Adjust stock for a warehouse with an audit-bearing reason (Priority: P1)

A warehouse admin opens the inventory module, picks a warehouse, searches a SKU, sees its current available / on-hand / reserved breakdown, fills in an adjustment (delta with sign, reason code, optional batch/lot reference, optional note), submits — the ledger gains an append-only movement entry, the on-hand changes, available-to-sell recomputes, and the audit-log reader (spec 015) shows a fresh entry with the actor + reason.

**Why this priority**: This is the hot-path operation for every warehouse team and the primary reason inventory admin exists. Cycle counts, supplier receipts, breakage, and shrinkage all flow through this single form. Without it, no other inventory story has standing data to operate on.

**Independent Test**: As a warehouse-scoped admin, perform a positive adjustment (e.g., +10 from a receipt) and a negative adjustment (e.g., -1 breakage); verify the ledger shows two movements with the correct reason codes; verify the available-to-sell number recalculates; verify the audit-log reader (spec 015) shows two fresh entries with the actor + before/after numbers + reason code.

**Acceptance Scenarios**:

1. **Given** a warehouse admin with `inventory.adjust` permission, **When** they submit a positive adjustment with a valid reason code, **Then** on-hand increases by the delta, available-to-sell recomputes, the ledger gains a `manual_adjustment` movement, and an audit event is emitted.
2. **Given** the same admin, **When** they submit a negative adjustment whose absolute value exceeds available stock, **Then** the form blocks save with a localized error and the user can either reduce the delta or convert the action into an explicit "write-off below zero" requiring an additional permission key.
3. **Given** the adjustment form is open, **When** the admin selects a SKU that has expiry-tracked batches, **Then** the form requires picking a specific batch/lot reference for the adjustment; non-batched SKUs hide that field.
4. **Given** an admin without `inventory.adjust` permission, **When** they navigate to `/admin/inventory/adjust`, **Then** they see a 403-style screen — never the form.
5. **Given** any successful adjustment, **When** the admin re-loads the SKU detail screen, **Then** the most recent ledger row is at the top, dated locale-correctly, with the actor + reason visible.

---

### User Story 2 - Triage the low-stock queue and tune per-SKU thresholds (Priority: P2)

A warehouse admin opens the **Low stock** queue (filtered to their warehouse / market scope), sees the SKUs whose available-to-sell sits at or below their reorder threshold, sorted by severity. They open a row, review consumption velocity (last 7 / 30 / 90 days), and either trigger a reorder workflow (out-of-scope for this spec — pointer only) or edit the per-SKU threshold to recalibrate the alert.

**Why this priority**: Reduces stockouts, which directly hurt revenue. P2 because the system survives without proactive triage (admins can do reactive replenishment from the adjustment form), but the queue is the difference between a well-run warehouse and a reactive one.

**Independent Test**: Seed several SKUs with stock at or below their thresholds; open the queue; confirm the rows are present, sorted, filterable by warehouse/market; edit a threshold; confirm the row drops out of the queue when the threshold no longer trips.

**Acceptance Scenarios**:

1. **Given** a warehouse admin, **When** they open the low-stock queue, **Then** every SKU at or below its reorder threshold for their warehouse / market scope appears, sorted by severity (lower available-to-sell = higher rank).
2. **Given** the queue is open, **When** the admin edits a SKU's reorder threshold, **Then** the row updates immediately (optimistic) and persists; an audit event is emitted.
3. **Given** the queue, **When** the admin filters by category, brand, or tag, **Then** the list narrows accordingly.
4. **Given** the queue, **When** the admin clicks **Open in adjustment**, **Then** the adjustment form opens prepopulated with that SKU + warehouse.
5. **Given** an admin without `inventory.threshold.write`, **When** they try to edit a threshold, **Then** the inline editor is read-only and an error toast surfaces if they bypass the UI.

---

### User Story 3 - Manage batches, lots, and expiry (Priority: P3)

A warehouse admin records new batch/lot details when stock arrives (lot number, supplier reference, manufactured-on, expires-on, quantity, optional certificate-of-analysis document), browses the expiry calendar to see what's expiring this month / next month / later, and acts on near-expiry items (transfer / discount listing pointer / write-off).

**Why this priority**: Medical / dental supply chain has hard regulatory requirements around expiry. Critical for compliance but separable from the day-one stock-adjustment loop because non-batched SKUs are exempt.

**Independent Test**: Create a batch with an expiry date 60 days away; open the expiry calendar; confirm the batch shows up in the appropriate bucket; advance the system clock (or use a backdated batch) and confirm the near-expiry alert badge surfaces on the queue.

**Acceptance Scenarios**:

1. **Given** an admin with `inventory.batch.write`, **When** they create a new batch with all required fields, **Then** the batch persists, links to its parent SKU, optionally links to a receipt note, and emits an audit event.
2. **Given** a batch nearing its expiry threshold (default 30 days), **When** the admin opens the expiry calendar, **Then** the batch surfaces in the **Near expiry** lane with an action menu (transfer / write-off / view).
3. **Given** a batch already expired, **When** the admin opens the expiry calendar, **Then** the batch surfaces in **Expired** with read-only badge — only `inventory.batch.writeoff` admins can clear it via a write-off adjustment.
4. **Given** a batch with non-zero on-hand, **When** the admin attempts to delete it, **Then** the action is blocked with a localized error directing them to write off the remaining quantity first.
5. **Given** a batch with attached certificate-of-analysis document, **When** the admin opens the batch detail, **Then** the document is downloadable through the storage abstraction (no direct provider URL).

---

### User Story 4 - Inspect and manually release reservations (Priority: P4)

A warehouse admin investigating a customer-support escalation ("my cart says out of stock but I can see it in the warehouse") opens the **Reservations** screen, filters by SKU + warehouse + age, sees who holds what (reservation id, owner = cart / order, TTL remaining, lines + qty), and either lets the natural TTL expire or — for stale reservations whose owning cart has clearly been abandoned — manually releases them.

**Why this priority**: Lower frequency than stock adjustments but high blast radius when wrong. Releasing the wrong reservation can break a live cart mid-checkout. Required for launch readiness but not a daily flow.

**Independent Test**: Seed a few reservations of varying TTL + owner type; open the reservations screen; confirm visibility + filtering; release one stale reservation; confirm the release persists, an audit event is emitted, and the available-to-sell recomputes.

**Acceptance Scenarios**:

1. **Given** an admin with `inventory.reservation.read`, **When** they open the reservations screen, **Then** every active reservation in their warehouse scope appears with id, owner kind (cart / order / quote), owner id (truncated, click-to-expand), SKU, qty, TTL remaining, created-at.
2. **Given** the screen, **When** they filter by SKU or owner kind or age, **Then** the list narrows accordingly.
3. **Given** an admin with `inventory.reservation.release`, **When** they manually release a reservation, **Then** the action requires a confirmation dialog citing the reservation's owner, the action commits, the available-to-sell recomputes, and an audit event is emitted carrying the actor + reason.
4. **Given** an admin without `inventory.reservation.release`, **When** they open the screen, **Then** the **Release** action is hidden / disabled.
5. **Given** any reservation displayed, **When** the admin clicks the owner id, **Then** they navigate to that owner's detail (cart → spec 015's customer screen / order → spec 018's order screen) when the linked spec has shipped its admin surface; otherwise the click surfaces a toast "owner detail not yet available".

### Edge Cases

- Two admins adjust the same SKU+warehouse simultaneously → spec 008's row-version optimistic concurrency rejects the second save with a 412; the editor surfaces a "another admin updated this stock; reload the current numbers?" dialog with no data loss.
- Negative adjustment that would cross zero → form blocks save unless the admin has `inventory.writeoff_below_zero` permission.
- Batch deletion with non-zero on-hand → blocked with a write-off-first error.
- Threshold edit on a SKU that's already below the new threshold → row stays in the queue and the severity rank updates.
- Reservation released while its owning cart is mid-checkout → spec 010's drift detection catches this on the next checkout-submit and surfaces the drift screen to the customer.
- Calendar view spanning multiple markets → the admin's role scope clamps the data; super-admins see the cross-market aggregate.
- Receipt-linked batch where the receipt was reversed → the batch keeps its receipt link for traceability; an inline note flags the receipt as reversed; admins cannot create new movements against a reversed receipt's batch but existing on-hand still flows through normal allocation.
- Locale switch with a partially-typed adjustment → the form preserves field state; numerals + dates re-render in the new locale; in-flight save still completes.
- Export a 100k-row ledger → spec 008 streams CSV; the export is asynchronous (ledger size can be large); admin sees a notice "we'll email you a download link" or watches a small progress widget.

## Requirements *(mandatory)*

### Functional Requirements

#### Shell + nav

- **FR-001**: The inventory module MUST mount inside spec 015's admin shell — a sidebar group "Inventory" with sub-entries: Stock by SKU, Adjust stock, Low-stock queue, Batches & lots, Expiry calendar, Reservations, Ledger.
- **FR-002**: Every page MUST use spec 015's shell primitives (`AppShell`, `DataTable`, `FormBuilder`, state primitives) — no reimplementation.
- **FR-003**: Every page MUST be keyboard-navigable + WCAG 2.1 AA (inherits spec 015's a11y bar).

#### Stock adjustment (per Principle 11 + Principle 25)

- **FR-004**: The adjustment form MUST present: warehouse picker, SKU picker, current available / on-hand / reserved snapshot (read-only), delta (signed integer), reason code (closed enum from spec 008), batch/lot reference (required if SKU is batch-tracked), and a free-text note. The note is **mandatory** (≥ 10 characters) when the reason code is `theft_loss`, `write_off_below_zero`, or `breakage`; **optional** otherwise. The form MUST block save with a localized error if a required note is missing for these reason codes.
- **FR-005**: The form MUST refuse any save that would drive on-hand below zero **by default**. Only an actor holding the `inventory.writeoff_below_zero` permission may override, and only after (a) the reason code is `write_off_below_zero`, (b) the mandatory note (FR-004) is filled, and (c) a confirmation dialog citing "this is a write-off below zero — the note above will be visible in the audit log" is acknowledged. Standard `inventory.adjust` permission cannot cross zero under any reason code.
- **FR-006**: Every save MUST emit an audit event via spec 003 with the before / after on-hand, the reason code, the actor, and the optional note. The audit-log reader (spec 015) is the read surface.
- **FR-006a**: The SKU detail screen, the batch detail screen, and the reservations table's owner-link MUST each surface an `<AuditForResourceLink>` (spec 015 FR-028f) deep-linking to the audit reader pre-filtered by the resource. Hidden when the actor lacks `audit.read`.
- **FR-007**: The form MUST surface an "another admin updated this stock; reload?" dialog on a 412 conflict; the reload preserves the admin's typed delta + reason in a side panel so they can re-apply if still appropriate.

#### Low-stock queue + thresholds

- **FR-008**: The queue MUST surface every SKU at or below its reorder threshold within the admin's warehouse / market scope, sorted by severity (lower available-to-sell = higher rank).
- **FR-009**: The queue MUST support filtering by category, brand, tag, expiry status (expiring soon / expired), and free-text SKU.
- **FR-010**: A per-SKU threshold MUST be editable inline (where the admin holds `inventory.threshold.write`); the edit emits an audit event.
- **FR-011**: Each row MUST surface consumption velocity (last 7 / 30 / 90 days) and a quick action **Open in adjustment** prepopulating the form.

#### Batch / lot + expiry

- **FR-012**: Batch / lot CRUD MUST capture: lot number, supplier reference, manufactured-on, expires-on, initial quantity, optional certificate-of-analysis document via the storage abstraction.
- **FR-013**: A batch with non-zero on-hand MUST NOT be deletable; the editor MUST direct the admin to write-off the remaining quantity first.
- **FR-014**: The expiry calendar MUST present three lanes: Near-expiry (within the active threshold), Expired, Future. Each lane uses the locale-correct date format. The active threshold is resolved as `warehouse.nearExpiryThresholdDays` if set on the current warehouse; otherwise the global default of 30 days. Per-SKU thresholds are out of scope for v1.
- **FR-014a**: Editing `warehouse.nearExpiryThresholdDays` is **out of scope for this spec**. The field is read-only here — the calendar consumes whatever spec 008 publishes per warehouse. A warehouse-admin surface (later spec) owns the editor; until it ships, the value is set via spec 008's seeder / ops console. The Out-of-Scope section reiterates this; the data-model field stays so the calendar can render the per-warehouse override the moment ops sets one.
- **FR-015**: Near-expiry / expired SKUs MUST surface a badge on the SKU detail and on the low-stock queue row.

#### Reservation inspection

- **FR-016**: The reservations screen MUST show every active reservation within the admin's scope with: id, owner kind (cart / order / quote), owner id (truncated, click-to-expand), SKU, qty, TTL remaining, created-at, actor (when system created vs. admin created).
- **FR-016a**: TTL countdown MUST be computed client-side from the server's `expiresAt` timestamp, not by polling. The reservations table fetches the page once on mount + on filter / pagination change; per-row "X seconds remaining" updates from a single client-side ticker (1 Hz, paused when the tab is hidden via `Page Visibility API`). A full re-fetch is triggered only on tab refocus after > 60 s away or on explicit pull-to-refresh / button click. This avoids the 5-Hz-per-row polling storm a naive implementation invites.
- **FR-017**: Manual release MUST require `inventory.reservation.release` permission and a confirmation dialog citing the owner; the release emits an audit event. Release MUST be **silent to the cart owner** — no admin-driven push notification is sent. Drift surfaces naturally through spec 010's drift detection on the customer's next cart view or checkout submit.
- **FR-018**: Owner-id deep links MUST resolve to the owning admin surface when present (spec 015 customer detail, spec 018 order detail) and degrade gracefully ("owner detail not yet available") when not.

#### Ledger

- **FR-019**: The ledger MUST be append-only and surface every movement with: SKU, warehouse, delta, reason code, actor, batch / lot reference (when applicable), source (manual / reservation-convert / receipt / return / write-off / system), occurred-at, audit-log permalink (deep-links into spec 015's reader).
- **FR-020**: The ledger MUST support cursor-based pagination and the same filter set as the low-stock queue plus a movement-source filter.
- **FR-021**: Ledger export MUST stream CSV through a Next.js Route Handler proxying spec 008's export endpoint; for ledgers > 50k rows the export MUST be asynchronous, identified by an `ExportJob` id. The download link MUST be delivered through (a) email and (b) the in-app bell once spec 023 ships its notifications + email pipeline. Until 023 ships, the Ledger page surfaces a **status widget** (queued / in_progress / done / failed) that persists across page refreshes via the export-job id and exposes the download link inline when ready.

#### Architectural guardrails

- **FR-016b**: The receipt-detail link from a batch (per spec 008's `receiptId` linkage) MUST resolve to `/admin/inventory/receipts/<id>`. Until a future admin-receipts spec ships the full surface, that route MUST render a **placeholder screen** with: the receipt id (copyable), a "receipt detail not yet available" localized message, and a back-link to the originating batch. The placeholder route is registered with the shell's `(admin)` group, gated on `inventory.batch.read` (same gate as the originating batch detail), so navigation does not 403 unexpectedly. Once the receipts spec ships, the placeholder is replaced in-place — no link path changes.
- **FR-016c**: The barcode-scan affordance on the adjustment form's `<SkuPicker>` MUST request camera access via the browser's `navigator.permissions` API with **localized prompt copy** (in `messages/{en,ar}.json` under `inventory.barcode.permission.*`). The localized strings cover (a) the in-app explanation rendered above the system prompt, (b) the deny-state recovery copy ("camera access denied — type the SKU instead or enable camera in browser settings"), and (c) the scanner-not-supported fallback for Firefox / Safari. No raw English string flashes on AR builds. Camera access is **already permitted** by the global `Permissions-Policy: camera=(self)` header set by spec 015's CSP middleware (see spec 015 FR-028c + `contracts/csp.md`); this spec does not relax the policy further.

- **FR-021a**: The full set of permission keys this spec consumes — `inventory.read`, `inventory.adjust`, `inventory.writeoff_below_zero`, `inventory.threshold.read`, `inventory.threshold.write`, `inventory.batch.read`, `inventory.batch.write`, `inventory.batch.writeoff`, `inventory.reservation.read`, `inventory.reservation.release` — MUST be registered in spec 015's `contracts/permission-catalog.md` (see spec 015 FR-028b).
- **FR-022**: This spec MUST NOT modify any backend contract. Gaps escalate to spec 008.
- **FR-023**: All API access MUST go through spec 015's auth proxy + generated typed clients; no ad-hoc fetch in feature code.
- **FR-024**: Every page MUST handle: loading / empty / error / restricted / conflict (412) / locale-switch states explicitly.
- **FR-025**: Both Arabic and English MUST be fully supported with full RTL when AR is active; reason-code labels and movement-source labels MUST be localized via the localization layer (i18n keys, never hard-coded English).

### Key Entities *(client-side state — no server persistence introduced)*

- **AdjustmentDraft**: warehouse id, SKU id, delta (signed integer), reason code, optional batch/lot id, optional note, current snapshot (available / on-hand / reserved), row-version cursor.
- **LowStockRow**: SKU id, name (ar/en), warehouse id, available-to-sell, threshold, severity rank, velocity (7d/30d/90d), expiry badge.
- **BatchViewModel**: id, SKU id, lot number, supplier reference, manufactured-on, expires-on, on-hand, certificate-of-analysis document ref, receipt link (optional), reversed-receipt flag.
- **ReservationViewModel**: id, owner kind, owner id, SKU id, warehouse id, qty, TTL remaining, created-at, actor.
- **LedgerRow**: id, SKU id, warehouse id, delta, reason code, source, batch/lot id (optional), actor, occurred-at, audit permalink.
- **ExportJob**: id, status (`queued` / `in_progress` / `done` / `failed`), download url (when done).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A warehouse admin can record a stock adjustment in under 30 seconds from opening the form to seeing the audit entry, on a typical SKU lookup.
- **SC-002**: ≥ 99 % of stock-adjustment saves complete without a 412 conflict on a single-admin warehouse.
- **SC-003**: 100 % of inventory screens render correctly in both Arabic-RTL and English-LTR (inherits spec 015's visual-regression mechanism).
- **SC-004**: The low-stock queue's first page returns in under 1 second on the staging dataset (target: 100k SKUs × 10 warehouses).
- **SC-005**: 0 stock adjustments persist without a corresponding audit event (verified by a periodic reconciliation script — owned by ops).
- **SC-006**: The expiry calendar's near-expiry lane surfaces every batch within the configured threshold ≥ 99.9 % of the time on the staging dataset.
- **SC-007**: Reservation release median latency ≤ 1 s end-to-end (click → audit + recomputed available-to-sell).
- **SC-008**: 0 user-visible English strings on any inventory screen when the active locale is Arabic.
- **SC-009**: 0 backend contract changes shipped from this spec — escalations tracked as spec-008 issues.

## Assumptions

- **Spec 015 shell** — shipped. Inherits auth proxy, `DataTable`, `FormBuilder`, audit-log reader, AR/RTL plumbing.
- **Spec 008 contracts merged** — required by the implementation plan. Any contract gap is escalated to spec 008 (not patched here).
- **Reason-code catalog** — owned by spec 008. The UI consumes whatever closed enum the server publishes; new reason codes need an i18n key in this spec's `messages/{en,ar}.json` and a server-side enum entry in spec 008. The default v1 catalog: `manual_adjustment`, `cycle_count_correction`, `breakage`, `theft_loss`, `supplier_receipt`, `customer_return`, `write_off_expiry`, `write_off_below_zero`, `transfer_in`, `transfer_out`.
- **Receipt linkage** — spec 008 owns receipt-note records; this spec consumes them as opaque references. Receipt CRUD is out of scope here (a later admin-receipts spec, not yet planned).
- **Reorder workflow** — out of scope for this spec. The low-stock queue surfaces a stub "Trigger reorder" link only when spec 008 / a later procurement spec ships the action; until then the link is hidden behind a feature flag.
- **Batch deletion / write-off cadence** — admins write off remaining stock through the adjustment form with `write_off_expiry` or `write_off_below_zero` reason codes; the batch record is then deletable.
- **Ledger size** — the ledger can grow large (millions of rows over time). The default UI pagination is cursor-based at 50 rows / page; export streams server-side and switches to async at > 50k rows (FR-021).
- **Reservation TTL display** — spec 008 publishes the TTL on each reservation; the UI renders both absolute timestamp and "X minutes remaining" — locale-correct.

## Dependencies

- **Spec 003 (foundations)** — storage abstraction + audit emission.
- **Spec 008 (inventory)** — every backend contract this spec consumes.
- **Spec 015 (admin foundation)** — shell, auth proxy, DataTable, FormBuilder, audit-log reader.
- **Spec 018 (admin orders)** — owner-id deep link from reservations (degrades gracefully if 018 hasn't shipped).
- **Spec 019 (admin customers)** — cart owner deep link (degrades gracefully).

## Out of Scope (this spec)

- Receipt CRUD UI (later admin spec).
- Warehouse settings editor (including `nearExpiryThresholdDays`) — owned by a future warehouse-admin spec.
- Reorder workflow / supplier PO management (later procurement spec).
- Multi-warehouse transfer workflow with two-step approval (deferred — manual transfers via paired `transfer_out` / `transfer_in` adjustments work in v1).
- Cycle-count workflow (counting wave generation, count session UI) — deferred.
- B2B-specific inventory views (spec 021 area).
- Customer-facing stock visibility (owned by spec 014 / spec 005).
