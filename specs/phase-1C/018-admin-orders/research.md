# Phase 0 Research: Admin Orders

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

Resolves every Technical-Context decision in `plan.md`. Inherits unchanged decisions from specs 015 / 016 / 017 (Next.js App Router, iron-session auth proxy, openapi-typescript, vitest + Playwright + axe + Storybook, react-query + react-table, papaparse, react-virtual). Only deltas documented.

---

## R1. Transition-action gating — single source of truth in `lib/orders/transition-gate.ts`

- **Decision**: A pure function `evaluateTransition({ machine, fromState, toState, permissions, orderRowVersion }): TransitionDecision` returns one of:
  - `{ kind: 'render', actionKey, requiredPermission, label }`
  - `{ kind: 'hide', reason: 'permission_missing' | 'state_machine_disallowed' }`
  - `{ kind: 'render_disabled', reason: 'order_closed' | 'shipment_blocking' }`  *(used only for terminal states like `order.cancelled`)*
- The action-bar renders only `kind: 'render'` actions; the click handler re-runs the gate before submit (defends against permission revoked mid-page-life). The "no-403-after-render" contract test (SC-004) walks every (machine × from × to × permission profile) combination and asserts that the gate's decision matches what the server would do.
- **Rationale**: One pure function, one truth, one test. Components stay declarative.
- **Alternatives rejected**: per-component inline checks (drift), backend-only gate (the UI must hide actions before showing them — server-side gate doesn't help client-side affordance).

## R2. Four-stream timeline — virtualized vertical list with stream filters

- **Decision**: A single virtualized vertical list (`@tanstack/react-virtual`) renders timeline entries from all four streams in chronological order. Each entry is colour-coded by stream (Order / Payment / Fulfillment / Refund); a header filter chip-set lets the admin narrow to one or more streams. Entries support keyboard navigation (arrow up/down focuses) + ARIA `role="article"` + `aria-rowindex`/`aria-rowcount` for screen-reader orientation in virtualization.
- **Rationale**: A unified timeline matches the operations narrative ("what happened to this order, in order"). Stream filters preserve the per-machine view when needed.
- **Alternatives rejected**: four parallel lanes (looks busy; B2B orders with many fulfillment events crowd lane 3 while lane 1 is empty), one accordion per machine (collapses the chronological view).

## R3. Refund draft form — line-level picker with eager over-refund check

- **Decision**: `<RefundDraftForm>` is built on `react-hook-form` + `zod`. Schema enforces:
  - `lines[i].qty <= deliveredQty - alreadyRefundedQty` per line (eager).
  - `sum(lines[i].amount) <= capturedTotal - alreadyRefundedTotal` (eager — the same guard the server enforces).
  - `reasonNote` length ≥ 10 chars, ≤ 2000.
  - Idempotency key (UUID v4) generated on form mount; rotated only after a 412 reload.
- The form computes the new payment + refund states client-side for the confirmation step ("after refund: payment.partially_refunded, refund.partial"). The server is authoritative on the actual transition.
- **Rationale**: Eager validation makes the form feel sharp; server-side over-refund guard from spec 013 remains the actual authority.
- **Alternatives rejected**: total-only refund (loses line-level traceability for B2B accounting), separate per-line dialog (multiplies clicks).

## R4. Step-up MFA dialog — inline wrapper around spec 004's flow

- **Decision**: When the refund-form's submit is **above** the env threshold or **full-amount**, the form intercepts the submit and opens `<StepUpDialog>` (Radix popover-style modal). The dialog calls spec 004's step-up endpoint (`POST /v1/identity/admin/step-up/start`) → renders the TOTP / push-notification challenge → on success, the dialog closes and the original submit proceeds with the step-up assertion id attached as `X-StepUp-Assertion: <id>` header.
- Threshold lookup: `env.NEXT_PUBLIC_REFUND_STEP_UP_MINOR_THRESHOLD_KSA` (default 10000) and `…_EG` (default 50000). Resolved per-market from the order's market_code.
- Admins without an enrolled MFA factor see a localized error linking to spec 015's `/me` route ("enrol an MFA factor before initiating refunds in this range").
- **Rationale**: Q1 chose threshold-gated step-up. Wrapping spec 004's flow keeps step-up logic in one place; the order spec just triggers it.
- **Alternatives rejected**: redirect to a separate step-up page (loses form context), inline TOTP input on the same form (reimplements spec 004's flow).

## R5. Invoice section — status pill + download / regenerate

- **Decision**: `<InvoiceSection>` renders one of three states:
  - **Available**: latest version + a download button (calls `/api/orders/[id]/invoice/download` route handler that proxies the storage abstraction's signed URL).
  - **Pending**: spinner + a "render in progress" message; polls every 5 s until terminal.
  - **Failed**: error message + a `Regenerate` action gated on `orders.invoice.regenerate`. Click triggers spec 012's retry endpoint and transitions to **Pending**.
- Older versions are not currently exposed (Out of Scope for v1) — when finance asks for "the previous invoice", a future spec adds version pickers.
- **Rationale**: The simplest UI surface that covers FR-017 / FR-018 / FR-019. Polling is bounded by the typical render time (< 30 s for a single-page invoice).
- **Alternatives rejected**: WebSocket push for render status (overkill for a sub-minute job), background polling without UI feedback (admins miss when their click took effect).

## R6. CSV export — filter snapshot pattern

- **Decision**: `<ExportButton>` POSTs the active filter set to `/api/orders/exports`. The route handler proxies spec 011's export-create endpoint with the snapshot. The response carries an `OrdersExportJob.id`; the page redirects to `/orders/exports/[jobId]` (deep-linkable). The job-detail page renders the snapshot's filters read-only + the status widget polled by `lib/orders/export-job-poller.ts` (reuses 017's pattern).
- The filters card on the job-detail page makes "what was exported" debuggable — a critical property when finance asks "did this export include the November refunds?".
- **Rationale**: Q2 chose snapshot-at-create. The job-detail surface materializes the snapshot for verification.
- **Alternatives rejected**: live-rebind to current filters (silent data divergence; Q2 explicitly rejected), drop the snapshot card (admin can't audit what was exported).

## R7. Customer chip + Source-quote chip — feature-flagged graceful degradation

- **Decision**: Both chips read from `lib/orders/feature-flags.ts`:
  ```ts
  export const flags = {
    adminCustomersShipped: process.env.NEXT_PUBLIC_FLAG_ADMIN_CUSTOMERS === '1',
    adminQuotesShipped: process.env.NEXT_PUBLIC_FLAG_ADMIN_QUOTES === '1',
  };
  ```
  When **off**, the chip opens a placeholder dialog with a "Copy id to clipboard" affordance (Q4 / Q5 clarifications). When **on**, the chip navigates to the real route. Flipping a flag is a config change, not a code change.
- The source-quote chip additionally honors the `orders.quote.read` permission **iff** that key exists in the admin's permission set OR is **declared** in spec 004's catalog. If the key is undeclared (i.e., 021 hasn't shipped), the chip defaults to visible. This avoids gating on a not-yet-defined permission.
- **Rationale**: Decouples spec 018 from 019 / 021 ship dates. Operations can flip flags as those specs land without an 018 redeploy.
- **Alternatives rejected**: hard-code "coming soon" (every flip is a code change), gate on the unknown permission (admins lose access when 021 ships even before they're granted the new key).

## R8. Telemetry events

- **Decision**: Same pattern as 015 / 016 / 017. New events listed in `contracts/client-events.md`. PII guard rails identical (no order id, no customer id, no amount values; coarse buckets + closed enums only).

## R9. CI integration

- **Decision**: No new workflow file. Inherits `apps/admin_web-ci.yml` from spec 015 unchanged. The "no-403-after-render" contract test (SC-004) runs as part of `pnpm test`. Visual regression continues across all admin features. `impeccable-scan` continues advisory.

---

## Open follow-ups for downstream specs

- **Spec 011**: confirm the `/timeline` endpoint returns transitions across all four streams in chronological order with stream-tag + actor + reason. Confirm `rowVersion` is exposed on order detail. Confirm export-create + export-status + export-download endpoints exist.
- **Spec 012**: confirm invoice render-status endpoint + signed-download URL + regenerate endpoint. Confirm whether the regenerate endpoint accepts an admin-supplied reason note (audit context).
- **Spec 013**: confirm the refund-create endpoint accepts line-level breakdown + reason note + idempotency key + step-up assertion id. Confirm over-refund guard semantics so the eager client check matches.
- **Spec 004**: confirm step-up start + complete endpoints. Confirm the assertion id format and TTL. Confirm the future `orders.quote.read` permission key (escalate if not yet planned for spec 021's ship list).
- **Spec 019**: when shipped, flip `NEXT_PUBLIC_FLAG_ADMIN_CUSTOMERS=1` in deployments. No code change here.
- **Spec 021**: when shipped, flip `NEXT_PUBLIC_FLAG_ADMIN_QUOTES=1`. No code change here.
- **Spec 023 (notifications)**: when shipped, the export-job download link surfaces in the bell + email pipeline; this spec's job-detail status widget remains as the in-tab fallback.
