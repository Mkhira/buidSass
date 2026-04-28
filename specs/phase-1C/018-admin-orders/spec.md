# Feature Specification: Admin Orders

**Feature Branch**: `phase-1C-specs`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "Spec 018 admin-orders (Phase 1C) — Next.js admin web feature mounting inside spec 015's shell. Per docs/implementation-plan.md §Phase 1C item 018: depends on specs 011 + 013 contracts merged to main + spec 015. Exit: order list + detail, status transitions (across the four streams), refund initiation, invoice reprint, quote linkage, finance CSV export."

## Clarifications

### Session 2026-04-27

- Q: Should refund initiation require a step-up auth (re-MFA / re-password) at submit time? → A: **Yes — step-up gate above a configurable threshold**. The refund-submit endpoint requires a fresh step-up assertion (per spec 004's step-up flow) when (a) the refund is for the full remaining captured amount, OR (b) the refund amount in minor units exceeds an env threshold (default: SAR 100 = 10000 minor units in KSA, EGP 500 = 50000 minor units in EG). Below the threshold, standard `orders.refund.initiate` permission is sufficient. The UI prompts for step-up inline when the threshold is exceeded; admins without an MFA factor see a localized error directing them to enrol via spec 015's `/me` route.
- Q: When the admin changes the orders-list filters between export-job creation and completion, what does the in-flight job contain? → A: **Snapshot at create time**. The server snapshots the filter set when the job is created; subsequent UI filter changes do not affect the in-flight job. To export a different slice, the admin starts a new job. The job-detail page displays the snapshot's filters read-only so the admin can verify what was exported.
- Q: For the multi-select affordance reserved on the orders list (FR-004 mentions "for bulk actions in later specs"), should v1 ship with disabled checkboxes or hide the column entirely? → A: **Hide entirely** in v1. No checkbox column, no header-select, no bulk-action bar. When a future spec wires bulk actions, both surfaces light up together. Reduces discovery cost and prevents user-trained-helplessness from disabled controls.
- Q: When a customer chip is clicked but spec 019 (admin customers) hasn't shipped, what happens? → A: **Graceful placeholder**. Clicking the chip opens a small dialog: "customer detail coming soon" plus a "Copy customer id" affordance for cross-team handoffs. The placeholder is gated behind a feature flag (`flags.adminCustomersShipped`) — flipping it to `true` when 019 ships swaps the placeholder for the real navigation without code changes here.
- Q: Should the **Source quote** chip render to admins who lack a future `orders.quote.read` permission? → A: **Hide when the permission is missing from the admin's set**. If the permission key is **absent from spec 004's permission catalog** (i.e., not yet defined as 021 hasn't shipped), default to visible — don't gate on a not-yet-defined permission. When 021 ships and adds the key, admins without it stop seeing the chip.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Triage and progress an order end-to-end (Priority: P1)

A fulfillment / customer-support admin opens the orders module, filters the list by order state + payment state + fulfillment state + refund state + market + B2B flag + date range, finds a specific order, opens its detail screen, sees a timeline of every transition across the four streams, and progresses the order along its valid state path (e.g., **mark packed** → **handed to carrier** → **delivered**) using actions whose availability is gated by spec 011's state machines + RBAC permissions.

**Why this priority**: Order operations is the daily admin loop. Without a list + detail + state-transition surface, every fulfillment + support task degrades to a database query. With just this story, fulfillment + customer-support teams can run the entire post-purchase operations cycle on top of spec 011.

**Independent Test**: Seed orders covering each combination of order × payment × fulfillment × refund states. Walk the list — every filter narrows correctly. Open one order — every transition action either lights up (when the state machine + permissions allow) or is hidden / disabled with a clear reason. Run a happy-path fulfillment progression (placed → packed → handed-to-carrier → delivered) and confirm an audit entry per transition surfaces in spec 015's reader.

**Acceptance Scenarios**:

1. **Given** an admin with `orders.read`, **When** they open the orders list, **Then** every order in their role scope appears with order / payment / fulfillment / refund states rendered as **four independent signals** per Constitution Principle 17 — never a single collapsed badge.
2. **Given** the list is open, **When** they apply any combination of the four state filters + market + B2B flag + date range, **Then** the list narrows server-side and the URL reflects the active filter so the view is shareable.
3. **Given** an order detail screen, **When** the admin scrolls the timeline, **Then** every transition (across all four streams) appears chronologically with actor, timestamp, before / after state, and a deep link to the corresponding audit-log entry.
4. **Given** an order in `payment.captured` + `fulfillment.not_started`, **When** the admin clicks **Mark packed**, **Then** the action is gated by spec 011's `FulfillmentSm` and the admin's permission set; on success the fulfillment state advances to `packed`, an audit event is emitted, and the timeline gains a new entry.
5. **Given** any transition action, **When** spec 011 returns a 409 illegal-transition error, **Then** the UI surfaces a localized error (no raw error code) explaining why the transition is currently invalid.
6. **Given** an admin without `orders.fulfillment.write`, **When** they open the order detail, **Then** the fulfillment-progress actions are hidden / disabled — never shown as enabled then 403-on-click.

---

### User Story 2 - Initiate a refund for a captured order (Priority: P2)

A customer-support admin opens an order whose customer has reported an issue, reviews the timeline + line items, opens the **Refund** action, picks lines + quantities + amount (full / partial), supplies a mandatory reason note, and submits. The action calls into spec 013's return / refund flow; on success the order's `refund` state advances and the timeline gains an audit-bearing entry.

**Why this priority**: Customer trust + the platform's compliance story (Principle 17 + 25) need a clean refund path from the same screen the support admin is already on. P2 because most orders never need a refund, and the spec 013 customer-initiated path covers the customer-side return; this story is the admin-driven complement.

**Independent Test**: Pick a `payment.captured` + `refund.none` order. Open the refund flow, choose partial refund of one line at full quantity, supply a note, submit. Confirm the order's refund state advances to `partial`, an audit entry is emitted, the customer receives the corresponding spec 013 / spec 023 notification, and the customer's order detail in the customer app reflects the refunded state.

**Acceptance Scenarios**:

1. **Given** an admin with `orders.refund.initiate`, **When** they open the refund flow on a `payment.captured` order, **Then** every line is selectable up to its delivered-and-not-already-refunded quantity.
2. **Given** the refund flow is open, **When** the admin requests a refund amount that exceeds the captured-minus-already-refunded total, **Then** the form blocks save with a localized error (over-refund guard).
3. **Given** any refund submission, **When** it succeeds, **Then** the order's refund state advances correctly per spec 013 (none → requested → partial / full), the timeline shows the actor + reason + amount + line breakdown, and an audit event is emitted.
4. **Given** an admin without `orders.refund.initiate`, **When** they open the order detail, **Then** the **Refund** action is hidden — never shown then 403-on-click.
5. **Given** a refund flow on an order that is not yet `payment.captured`, **When** the admin opens the action, **Then** they see an explanatory empty state (no refund possible at the current payment state).

---

### User Story 3 - Reprint a tax invoice and follow quote linkage (Priority: P3)

A finance admin opens an order, downloads the latest tax invoice (calling into spec 012), and — when the order originated from a B2B quotation — sees a chip linking to the source quote (in spec 021's surface, when shipped; otherwise a graceful placeholder).

**Why this priority**: Finance admins reprint invoices on-demand for accounting. Lower frequency than fulfillment but launch-blocking for KSA + EG tax compliance. Quote linkage matters less than invoice reprint — many orders won't have a linked quote.

**Independent Test**: Pick a `payment.captured` order whose tax invoice has rendered, click **Download invoice**, confirm the PDF downloads through the storage abstraction with the latest revision; confirm the invoice carries the locale-correct branding + the order's tax breakdown. For a B2B order with a linked quote, confirm the **Source quote** chip resolves to a quote detail (or a placeholder if spec 021 hasn't shipped).

**Acceptance Scenarios**:

1. **Given** an admin with `orders.invoice.read` on a captured order whose invoice has rendered, **When** they click **Download invoice**, **Then** the latest invoice version downloads through the storage abstraction.
2. **Given** an invoice render is in flight (queued / generating per spec 012), **When** the admin opens the invoice section, **Then** the screen surfaces a clear status (pending / failed / available) with retry affordance gated on `orders.invoice.regenerate`.
3. **Given** an admin with `orders.invoice.regenerate`, **When** they trigger invoice reprint, **Then** spec 012's render queue receives the request, an audit event is emitted, and the screen shows the new render's status.
4. **Given** an order originating from a quote, **When** the admin opens the detail, **Then** a **Source quote** chip is visible; clicking it routes to spec 021's quote detail when shipped, or surfaces a "quote detail coming soon" placeholder otherwise.

---

### User Story 4 - Export an orders dataset for finance (Priority: P4)

A finance admin filters the orders list to a fiscal period (e.g., last quarter, market = KSA, payment = captured) and exports the visible-set as CSV — financial team consumes the file in their offline tooling for tax reconciliation.

**Why this priority**: Reduces operational toil (handing data to finance over chat). Lower priority because finance can survive launch by querying the database; the export simplifies the daily flow.

**Independent Test**: Apply a fiscal-period filter (last quarter), choose **Export CSV**, confirm the download streams within the SC time budget; confirm the rows match the filtered list and include every column finance asked for (defined in `contracts/csv-format.md`).

**Acceptance Scenarios**:

1. **Given** an admin with `orders.export`, **When** they apply filters and click **Export CSV**, **Then** an `ExportJob` is created and the page surfaces queued / in_progress / done status; on done the download URL surfaces inline (and via spec 023 once shipped).
2. **Given** the export action, **When** the dataset exceeds the configured row cap (default 100k rows per export), **Then** the action surfaces a clear error directing the admin to narrow the filters before re-trying.
3. **Given** the streamed CSV download, **When** the admin opens it in a spreadsheet, **Then** every column carries a stable header schema documented in `contracts/csv-format.md`, with locale-correct numerals and an explicit currency column.

### Edge Cases

- Two admins progress the same order's fulfillment simultaneously → spec 011's row-version optimistic concurrency rejects the second save; the editor surfaces a "another admin updated this order; reload?" overlay.
- A transition that's structurally legal but role-gated for the current admin → action is hidden, not shown disabled with a 403 on click.
- A refund that would over-refund (e.g., due to a prior partial refund that the admin missed) → server-side over-refund guard from spec 013 returns a 409; the UI surfaces a localized error and a link to the prior refund history.
- An invoice render that fails on the server → the screen surfaces the failure with a retry affordance (gated on `orders.invoice.regenerate`); the failure reason is i18n-keyed (no raw error code).
- A long-running CSV export that the admin abandons → the export-job continues server-side and the download link surfaces in the bell + email pipeline (per spec 023) once shipped; until then the admin re-opens the export-job page via permalink.
- An order in `order.cancelled` + `payment.refunded` → the timeline must still render correctly; transition actions are uniformly disabled with a "this order is closed" inline note.
- B2B order with multiple buyer / approver actors → the timeline shows the originating actor on each transition with a role label.
- Locale switched during a transition action → the action still completes server-side; the success toast renders in the new locale.
- Filter combination that returns zero rows → empty-state UI surfaces a clear "no orders match these filters" message with a single "clear filters" affordance.
- Permission revoked mid-action → next API call returns 403; the editor surfaces the same screen the shell uses for direct-403 navigation.

## Requirements *(mandatory)*

### Functional Requirements

#### Shell + nav

- **FR-001**: The orders module MUST mount inside spec 015's admin shell — a sidebar entry "Orders" with sub-entries: **Orders** (the list at `/orders`), **Refunds** (a pre-filtered orders list scoped to `refundState != none`, deep-link target for the bell when a refund event surfaces there), **Exports** (the `/orders/exports` job list). A **Drafts** sub-entry is **out of scope** for v1 — order drafts are owned by the customer-app cart (spec 014) + spec 010 checkout; the placeholder previously reserved here is removed to avoid an unscoped affordance.
- **FR-002**: Every page MUST use spec 015's shell primitives (`AppShell`, `DataTable`, `FormBuilder`, state primitives) — no reimplementation.
- **FR-003**: Every page MUST be keyboard-navigable + WCAG 2.1 AA (inherits spec 015's a11y bar).

#### Order list

- **FR-004**: The orders list MUST use spec 015's shared `DataTable` with: server-side cursor pagination, saved views (per-admin, persisted via spec 004 user-preferences). **Multi-select is intentionally NOT shipped in v1** — no checkbox column, no header-select, no bulk-action bar. A future spec that introduces bulk transitions will add both surfaces together.
- **FR-005**: The list MUST render order / payment / fulfillment / refund as **four independent signals** per row per Constitution Principle 17 — never a single collapsed badge.
- **FR-006**: The list MUST support filters: order state (multi-select), payment state (multi-select), fulfillment state (multi-select), refund state (multi-select), market (single-select), B2B flag (boolean), date range (placed-at). The active filter set MUST be reflected in the URL so a filtered view is shareable.
- **FR-007**: The list MUST surface a clearly-labelled empty state when zero rows match the active filter set.

#### Order detail

- **FR-008**: The order detail MUST show: header (order number, B2B flag, market, customer chip), the **four state pills** (independent), customer + shipping address card, payment summary, line items, fulfillment shipments + carrier tracking, totals breakdown, **timeline** of every transition across all four streams.
- **FR-009**: Each timeline entry MUST carry actor identity, timestamp (locale-correct), before / after state, optional reason note, and a deep link to the corresponding audit-log entry in spec 015's reader.
- **FR-009a**: The order detail page header MUST surface an `<AuditForResourceLink resourceType="Order" resourceId={orderId} />` (spec 015 FR-028f) so an admin can jump from the order to the full audit history pre-filtered to this order without manually pasting ids. Hidden when the actor lacks `audit.read`.
- **FR-010**: Status-transition actions MUST be gated by spec 011's state machines AND the actor's permission set. Disallowed actions MUST be **hidden** rather than rendered-and-403-on-click.
- **FR-011**: A 409 illegal-transition response from the server MUST surface a localized error explaining the cause (e.g., "fulfillment cannot be marked delivered until the carrier has confirmed handoff").
- **FR-012**: A 412 row-version conflict MUST surface a "another admin updated this order; reload?" overlay that preserves any in-flight reason note in a side panel.

#### Cancel order

- **FR-012a**: The order detail MUST expose a **Cancel order** action gated on `orders.cancel`. The action MUST be **hidden** when (a) the actor lacks the permission, OR (b) spec 011's order state machine disallows cancellation from the current state (e.g., `order.delivered`). On click, a confirmation dialog MUST capture a mandatory free-text reason note (≥ 10 chars, ≤ 2000) and warn the admin about the cascade impact (any captured payment will require a separate refund per FR-013; any reserved inventory is released by spec 011's cancel handler). The submit MUST be idempotent and emit an audit event with the actor + reason note. Cancel does **not** automatically refund — the admin is routed to the refund flow as a follow-up where applicable.

#### Refund initiation

- **FR-013**: The refund flow MUST present line-level refund pickers (line, quantity ≤ delivered-and-not-already-refunded, amount), a free-text reason note (mandatory ≥ 10 chars), and a confirmation step citing the post-refund payment + refund states.
- **FR-014**: The flow MUST refuse a request that would over-refund (cumulative refund > captured); the over-refund guard is server-authoritative (spec 013) but the client validates eagerly.
- **FR-015**: Submission MUST require `orders.refund.initiate`. **Step-up auth is required** for any refund that is full-amount OR whose minor-unit amount exceeds an env threshold (default: SAR 100 = 10000 in KSA, EGP 500 = 50000 in EG). The UI MUST prompt the admin for spec 004's step-up flow inline (consuming the shared `<StepUpDialog>` primitive defined in spec 015 FR-025) when the threshold is exceeded; admins without an enrolled MFA factor MUST see a localized error directing them to enrol via the `/me` shell route. A successful submission emits an audit event captured by spec 015's reader, with the audit payload carrying the step-up assertion id when one was required.
- **FR-015a**: Step-up assertions issued by spec 004 carry a TTL (default 5 minutes per spec 004's catalog). Refund retries within the TTL MUST reuse the same assertion id without re-prompting. If the submit lands after the TTL has elapsed (network blip, user hesitation), the server MUST return a fresh `step_up_required` response and the form MUST re-prompt the dialog, preserving the in-flight refund draft and idempotency key — the next submit then carries the new assertion id. The form MUST NOT silently drop a refund attempt because of a TTL expiry.
- **FR-016**: The flow MUST be hidden on orders whose payment state does not permit a refund (per spec 013); when shown but disabled (e.g., over-refund), the disabled state carries a clear localized reason.

#### Invoice reprint

- **FR-017**: The invoice section MUST display the latest invoice version's status (pending / failed / available); when available, a download action streams the file through the storage abstraction.
- **FR-018**: A regenerate action MUST be available to admins with `orders.invoice.regenerate`; on click, spec 012's render-queue retry endpoint is called and the section status updates.
- **FR-019**: Every invoice download / regenerate action MUST emit an audit event.

#### Quote linkage

- **FR-020**: When an order's source is a quote (per spec 011 → 021 linkage), the order detail MUST render a **Source quote** chip; clicking it deep-links into spec 021's quote detail when shipped, otherwise renders a "quote detail coming soon" placeholder behind a feature flag. The chip MUST be **hidden** for any admin whose permission set lacks `orders.quote.read` once that key exists in spec 004's catalog. While `orders.quote.read` is undefined in the catalog (i.e., spec 021 hasn't shipped), default to visible — never gate on a not-yet-existing permission.

#### Finance CSV export

- **FR-021**: The export action MUST stream a CSV through a Next.js Route Handler proxying spec 011's export endpoint with the active list filter set. The **filter set is snapshotted server-side at job-create time** — subsequent UI filter changes MUST NOT affect an in-flight job. The job-detail page MUST display the snapshot's filters read-only so the admin can verify what was exported. The export action MUST be gated on a deployment feature flag `flags.financeExportEnabled` (env `NEXT_PUBLIC_FLAG_FINANCE_EXPORT`); when off (e.g., spec 011's CSV header schema not yet published per `contracts/csv-format.md`) the action MUST be hidden, not rendered-and-error-on-click.
- **FR-022**: Exports exceeding the configured row cap (default 100k rows) MUST run asynchronously as an `ExportJob`; the page surfaces queued / in_progress / done / failed status and exposes the download link inline when ready. To export a different filter slice, the admin starts a new job (no live re-binding of an existing job).
- **FR-023**: Every export MUST emit an audit event with the actor, timestamp, filter snapshot, and resulting row count.

#### Architectural guardrails

- **FR-023a**: The full set of permission keys this spec consumes — `orders.read`, `orders.pii.read`, `orders.fulfillment.write`, `orders.payment.write`, `orders.cancel`, `orders.refund.initiate`, `orders.invoice.read`, `orders.invoice.regenerate`, `orders.export`, `orders.quote.read` — MUST be registered in spec 015's `contracts/permission-catalog.md` (see spec 015 FR-028b). `orders.pii.read` is intentionally distinct from `customers.pii.read` (spec 019): the former gates email / phone visibility on the order's customer card; the latter gates the same fields on the customer profile. An admin may hold one without the other; both are filed against spec 004's catalog.
- **FR-024**: This spec MUST NOT modify any backend contract. Gaps escalate to specs 011 / 012 / 013 per Phase 1C intent.
- **FR-025**: All API access MUST go through spec 015's auth proxy + generated typed clients; no ad-hoc fetch in feature code.
- **FR-026**: Both Arabic and English MUST be fully supported with full RTL when AR is active; state pill labels, transition action labels, refund-reason placeholders MUST be localized via the i18n layer (no hard-coded English).

### Key Entities *(client-side state — no backend persistence introduced)*

- **OrderListRow**: order id, number, customer chip (id + display), market, B2B flag, four state values (order / payment / fulfillment / refund), totals, placed-at.
- **OrderDetail**: id, header fields, customer card, shipping address, payment summary, line items, shipments + tracking, totals breakdown, row version.
- **TimelineEntry**: occurred-at, machine (`order` / `payment` / `fulfillment` / `refund`), from-state, to-state, actor (kind + id + display), reason note, audit-permalink.
- **RefundDraft**: order id, line selections (line id, qty, amount), reason note, eager over-refund check result, idempotency key.
- **InvoiceStatus**: latest version, render status (`pending` / `failed` / `available`), download url (when available), error reason (when failed).
- **OrdersExportJob**: id, filter snapshot, status (`queued` / `in_progress` / `done` / `failed`), download url.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A fulfillment admin can find a specific order and progress it one step in under 30 seconds from login on the typical staging dataset.
- **SC-002**: The orders list's first page returns in under 1 second on the staging dataset (target: 5M lifetime orders, 100k active).
- **SC-003**: 100 % of admin-order screens render correctly in both Arabic-RTL and English-LTR — measured by spec 015's visual-regression mechanism.
- **SC-004**: 0 status-transition actions render as enabled then 403-on-click — measured by an automated check that walks every action × every permission profile (FR-010).
- **SC-005**: ≥ 99.9 % of refund submissions persist with the correct line-level breakdown and refund-state advance (sample-tested against spec 013 contract test).
- **SC-006**: Invoice download median latency ≤ 2 s on a typical 1-page invoice on broadband.
- **SC-007**: 0 backend contract changes shipped from this spec — escalations tracked as separate spec 011 / 012 / 013 issues.
- **SC-008**: 0 user-visible English strings on any orders screen when the active locale is Arabic.

## Assumptions

- **Spec 015 shell** — shipped. Inherits auth proxy, `DataTable`, `FormBuilder`, audit-log reader, AR/RTL plumbing.
- **Spec 011 contracts merged** — required by the implementation plan. Provides order list / detail / state-transition endpoints + state-machine docs the client uses to gate actions.
- **Spec 013 contracts merged** — required. Provides the refund initiation endpoint + over-refund guard.
- **Spec 012 contract** — provides invoice render-queue endpoints. If any specific surface is missing on day 1 (e.g., regenerate-from-admin), it's escalated per FR-024.
- **Spec 021 (quote linkage)** — not yet shipped. The **Source quote** chip surfaces with a placeholder body until 021 ships.
- **B2B order surface** — the order detail renders B2B-specific fields (PO number, approver chip) when present in the spec 011 response. B2B-specific admin workflows (approver re-routing, etc.) are owned by spec 021.
- **Saved views** — orders list saved views live on spec 004's user-preferences endpoint (same channel spec 015 / 016 use).
- **Bulk actions** — the list reserves multi-select affordance but does not ship bulk transitions in v1. A later spec (or a v1.1) can wire bulk-mark-packed / bulk-print-shipping-label by re-using the multi-select.
- **Customer chip in the list** — the customer chip resolves to spec 019's customer detail when the `flags.adminCustomersShipped` feature flag is on; otherwise opens a small dialog with "customer detail coming soon" + a copy-id-to-clipboard affordance. Flipping the flag swaps the placeholder for the real navigation without code changes here.

## Dependencies

- **Spec 003 (foundations)** — storage abstraction + audit emission.
- **Spec 011 (orders)** — every order CRUD / list / state-transition contract.
- **Spec 012 (tax invoices)** — invoice render queue + signed download URL.
- **Spec 013 (returns / refunds)** — refund initiation + over-refund guard.
- **Spec 015 (admin foundation)** — shell, auth proxy, DataTable, FormBuilder, audit-log reader.
- **Spec 019 (admin customers)** — customer chip deep link (degrades gracefully).
- **Spec 021 (B2B / quotes)** — source-quote chip deep link (degrades gracefully).

## Out of Scope (this spec)

- Quotation administration UI (spec 021).
- Customer profile administration UI (spec 019).
- Verification approval workflow (spec 020).
- Bulk transitions on multi-select rows (deferred — affordance reserved).
- Shipment label printing / carrier-pickup scheduling (later admin spec).
- Returns admin workflow (the customer-driven path is owned by spec 013; an admin-driven RMA approval spec lands later).
- Customer-facing rendering of any of the above (spec 014 / spec 011 customer surfaces).
