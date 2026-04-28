# Implementation Plan: Admin Orders

**Branch**: `phase-1C-specs` | **Date**: 2026-04-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1C/018-admin-orders/spec.md`

## Summary

Mount the **order operations module** inside spec 015's admin shell — Orders list, Order detail with the four-stream timeline + state-machine-gated transitions, Refund initiation flow (with step-up MFA above an env threshold), Invoice reprint via spec 012, Source-quote chip via spec 021 (placeholder until 021 ships), and async finance CSV exports. Lane B: UI only — every backend gap escalates to specs 011 / 012 / 013.

The order list renders the four states (`order` / `payment` / `fulfillment` / `refund`) as **independent signals** per row (Constitution Principle 17). Status-transition actions are **rendered iff** spec 011's state-machine docs say they are valid AND the actor's permission set includes the required key — never rendered-and-403-on-click (FR-010, SC-004). Refund submission triggers a step-up auth (per spec 004) when the amount exceeds an env threshold or the action is full-amount. CSV export creates an `OrdersExportJob` whose filter set is **snapshotted at create time** (Q2 clarification); subsequent UI filter changes don't affect an in-flight job. The customer chip and source-quote chip both degrade gracefully to placeholders behind feature flags until specs 019 / 021 ship.

The shell, auth proxy, `DataTable`, `FormBuilder`, audit-log read surface, AR-RTL plumbing, telemetry adapter, and CI hygiene are all inherited from spec 015. The four-stream timeline is the only major new composite primitive.

## Technical Context

**Language/Version**: TypeScript 5.5, Node.js 20 LTS (inherits spec 015's runtime).

**Primary Dependencies** (deltas on top of specs 015 / 016 / 017):

- `@tanstack/react-virtual` ^3 — virtualized rows for the orders list and the timeline (timelines on long-running B2B orders can run to hundreds of entries).
- `react-hook-form` ^7 + `zod` ^3 — refund-draft form (inherits spec 015 baseline).
- `papaparse` ^5 — CSV download / consume / preview path (inherits 016 / 017).
- `@radix-ui/react-popover` (vendored via shadcn) — step-up dialog for refunds.
- All other deps inherited (Next.js, react-query, react-table, next-intl, iron-session, shadcn/ui).

**Storage**: No new server-side persistence introduced. Client-side: react-query cache for list/detail/timeline; transient `IndexedDB` (via `idb` ^8) only to persist in-flight refund-draft notes if the admin survives a tab crash. No tokens, no PII in IndexedDB.

**Testing**:

- Unit + component (vitest + RTL) — list filters, four-stream pill row, timeline composite, transition-action gate, refund draft form, step-up dialog, invoice section, export-job status.
- Visual regression (Playwright + Storybook snapshots) — every order screen × {EN-LTR, AR-RTL} × {light, dark}, with explicit stories for "many timeline entries" and "long B2B PO display".
- A11y (axe-playwright) — every order screen, with explicit checks for the timeline (correct semantics for a chronological list), the refund draft form (number/qty inputs), and the step-up dialog (focus management).
- E2E (Playwright) — Story 1 (list → detail → progress fulfillment), Story 2 (refund happy + over-refund + step-up), Story 3 (invoice reprint), Story 4 (CSV export).
- A "no-403-after-render" contract test that walks each transition action × representative permission profile, asserting the action is **either** rendered enabled with a valid path **or** hidden — never rendered enabled but blocked server-side (SC-004).

**Target Platform**: Same as spec 015 — modern desktop browsers ≥ 1280 px wide.

**Project Type**: Next.js admin web feature folder under `apps/admin_web/app/(admin)/orders/` and `apps/admin_web/components/orders/`. No new app or package.

**Performance Goals**:

- Orders-list first page ≤ 1 s on staging dataset (5M lifetime, 100k active orders) — SC-002.
- Order-detail composite (header + four pills + timeline + lines + invoice section) ≤ 1.5 s to interactive on broadband.
- Refund-form submit median ≤ 800 ms (excluding step-up assertion latency, which is owned by spec 004).
- Invoice download median ≤ 2 s on a typical 1-page invoice — SC-006.

**Constraints**:

- **No backend code in this PR** (FR-024). Gaps escalate to specs 011 / 012 / 013.
- **No client-side fetch outside `lib/api/`** (inherits spec 015's lint).
- **No hard-coded user-facing strings** outside `messages/{en,ar}.json` (inherits 015's i18n lint).
- **Transition actions hidden when not allowed** (FR-010). Lint check + contract test guarantee no 403-on-click.
- **Step-up assertion required** for above-threshold or full-amount refunds (FR-015). Client triggers spec 004's step-up dialog inline.
- **Filter-snapshot at job-create** (FR-021). Client must not rebind a running export to current filters.

**Scale/Scope**: ~7 admin-orders pages (list, detail, refund flow, invoice section, export-jobs list, export-job detail, source-quote placeholder). 4 prioritized user stories, 26 functional requirements, 8 success criteria, 5 clarifications integrated. Storybook target: ~25 stories on top of 015's baseline.

## Constitution Check

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Customer browse / view price unaffected — admin side. | PASS (n/a) |
| P4 Arabic / RTL editorial | Every order screen ships AR + EN with RTL via spec 015's i18n stack. State labels, transition labels, refund-reason placeholders all i18n-keyed (FR-026). | PASS |
| P5 Market Configuration | List exposes a market filter; the admin's role scope clamps results. No hard-coded market literals. | PASS |
| P6 Multi-vendor-ready | Forward-compatible. When spec 011 adds vendor-scoped fields, the timeline + detail render whatever the server sends and ignore unknown fields. | PASS |
| P7 Branding | Tokens consumed from `packages/design_system`. No inline hex literals. | PASS |
| P9 B2B | Order detail surfaces B2B fields (PO number, approver chip) when present in the spec 011 response. B2B-specific admin workflows (approver re-routing, etc.) deferred to spec 021. | PASS (forward-compatible) |
| P17 Order / Payment / Fulfillment / Refund | List + detail render all four states as **independent signals** (FR-005, FR-008). Timeline groups transitions by stream. No collapsed badge anywhere. | PASS — squarely on principle |
| P22 Fixed Tech | Next.js + shadcn/ui per ADR-006. | PASS |
| P23 Architecture | Spec 015's modular shell + this feature folder. No new service. | PASS |
| P24 State Machines | Refund-draft state (Idle / StepUpRequired / Submitting / Submitted / OverRefundBlocked / Failed) + invoice-section state (Pending / Available / Failed) + transition-action gating per spec 011's machines — all documented in `data-model.md`. | PASS |
| P25 Data & Audit | Every status-transition + refund + invoice-regenerate + export emits an audit event server-side; the audit-log reader (spec 015) is the read surface. Step-up assertion id rides on the refund-audit payload (FR-015). | PASS |
| P27 UX Quality | Every screen ships loading / empty / error / restricted / conflict (412) / step-up-required / locale-switch states. The "no orders match these filters" empty state is explicit (FR-007). | PASS |
| P28 AI-Build Standard | Spec ships explicit FRs, scenarios, edge cases, success criteria, 5 resolved clarifications. | PASS |
| P29 Required Spec Output | All 12 sections present. | PASS |
| P30 Phasing | Phase 1C Milestone 5/6. Depends on specs 011/012/013 contracts merged + spec 015 shipped. | PASS |
| P31 Constitution Supremacy | No conflicts. | PASS |
| ADR-001 Monorepo | Code under `apps/admin_web/`. | PASS |
| ADR-006 Next.js + shadcn/ui | Locked. | PASS |
| ADR-010 KSA residency | API calls hit Azure Saudi Arabia Central. Storage abstraction in same region. | PASS |

**No violations.**

## Project Structure

### Documentation (this feature)

```text
specs/phase-1C/018-admin-orders/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── consumed-apis.md
│   ├── routes.md
│   ├── client-events.md
│   └── csv-format.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
apps/admin_web/
├── app/(admin)/orders/
│   ├── layout.tsx                       # Sub-shell highlighting the orders sidebar group
│   ├── page.tsx                         # Orders list (DataTable)
│   ├── [orderId]/
│   │   ├── page.tsx                     # Order detail (header + 4 pills + timeline + lines + invoice + actions)
│   │   ├── refund/page.tsx              # Refund flow (intercepted-route side panel + dedicated page)
│   │   └── invoice/page.tsx             # Invoice section drilldown (with reprint affordance)
│   └── exports/
│       ├── page.tsx                     # Export jobs list
│       └── [jobId]/page.tsx             # Export job detail (filter snapshot + status + download)
├── components/orders/
│   ├── list/
│   │   ├── orders-table.tsx             # Wraps spec 015's DataTable
│   │   ├── filter-bar.tsx               # 4 state filters + market + B2B + date range
│   │   ├── four-state-pill-row.tsx      # The four independent signals
│   │   └── customer-chip.tsx            # With the FR-019 placeholder behind feature flag
│   ├── detail/
│   │   ├── detail-header.tsx
│   │   ├── customer-card.tsx
│   │   ├── shipping-address-card.tsx
│   │   ├── payment-summary-card.tsx
│   │   ├── line-items-table.tsx
│   │   ├── shipments-card.tsx           # Carrier + tracking + per-shipment actions
│   │   ├── totals-breakdown.tsx
│   │   ├── timeline.tsx                 # Virtualized 4-stream timeline
│   │   ├── timeline-entry.tsx
│   │   ├── transition-action-bar.tsx    # State-machine + permission-gated actions
│   │   ├── transition-action-button.tsx
│   │   └── source-quote-chip.tsx        # Hidden when permission missing
│   ├── refund/
│   │   ├── refund-draft-form.tsx        # Line picker + qty + amount + reason + zod
│   │   ├── line-refund-row.tsx
│   │   ├── over-refund-warning.tsx
│   │   └── step-up-dialog.tsx           # Wraps spec 004's step-up flow
│   ├── invoice/
│   │   ├── invoice-section.tsx          # Status surface + download / regenerate actions
│   │   └── invoice-status-pill.tsx
│   ├── exports/
│   │   ├── exports-table.tsx
│   │   ├── filter-snapshot-card.tsx
│   │   └── export-job-status.tsx        # Shared with 017's pattern
│   └── shared/
│       ├── state-pill.tsx               # Single state pill (used in 4 places per row)
│       ├── conflict-overlay.tsx         # 412 stale-version flow
│       └── illegal-transition-toast.tsx # 409 surface
├── lib/orders/
│   ├── transition-gate.ts               # State-machine + permission gating logic
│   ├── refund-state.ts                  # SM-1 client model
│   ├── invoice-section-state.ts         # SM-2
│   ├── export-job-poller.ts             # Reuses 017's pattern
│   ├── feature-flags.ts                 # adminCustomersShipped, adminQuotesShipped
│   └── api.ts                           # react-query hooks wrapping specs 011 / 012 / 013 clients
└── tests/
    ├── unit/orders/...
    ├── visual/orders.spec.ts
    └── contract/orders.no-403-after-render.spec.ts   # SC-004 enforcement
```

**Structure Decision**: One feature folder under `app/(admin)/orders/` mirroring the route structure. The four-stream timeline is the only major new composite — every other piece reuses primitives from specs 015 / 016 / 017. Refund flow uses Next.js intercepting routes (the side-panel form opens over the detail page on direct navigation but renders standalone on a refresh / share). `lib/orders/` holds the transition-gate logic + state machines + feature-flag map; the gate is the linchpin of SC-004.

## Complexity Tracking

| Choice | Why | Simpler alternative rejected because |
|---|---|---|
| Hide-not-disable for transition actions (FR-010, SC-004) | Disabled buttons train admins that "this should work" — they get clicked, return 403, look broken. Hiding makes the UI honest about the admin's actual capabilities. | Disabled-but-rendered actions clutter the screen and produce a flow of 403 errors that look like bugs. |
| Server-side filter snapshot for export jobs (Q2) | Predictability: an admin who waits on a slow export shouldn't get a different dataset because they tweaked a filter mid-job. | Late-binding the filter to the running job introduces silent data divergence. |
| Step-up MFA gate above threshold for refunds (Q1) | Refunds move money. A step-up factor reduces the blast radius of a stolen admin session by a meaningful order of magnitude. | Standard `orders.refund.initiate` permission alone treats a SAR 5 refund and a SAR 50000 refund as equally cheap actions. |
| Virtualized timeline (`@tanstack/react-virtual`) | Long-running B2B orders accumulate hundreds of timeline entries; naive rendering blows the frame budget. | Server-side pagination of the timeline collapses the at-a-glance "what happened on this order" use case. |
| `transition-gate` library encoded once, used everywhere | The gate must be evaluated identically across action-bar render, action-click handler, and the no-403 contract test. One source-of-truth function avoids drift. | Inline checks in each component drift the moment a permission key is renamed. |
| Customer + source-quote chip behind feature flags | When 019 / 021 ship, the chip behaviour changes globally with one config flip; no PR needed in this spec. | Hard-coded "coming soon" requires a code change to swap. |
