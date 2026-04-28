# Phase 1 Data Model: Admin Orders

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)
**Date**: 2026-04-27

UI-only spec (FR-024). Every entity below is a **client-side view-model**. No new server-side tables.

---

## Client view models

### `OrderListRow`

| Field | Type | Source | Notes |
|---|---|---|---|
| `id`, `number` | `string` | spec 011 | |
| `customer` | `{ id: string; displayName: string; b2b: boolean }` | spec 011 | Display label only. |
| `marketCode` | `'ksa' \| 'eg'` | spec 011 | |
| `b2bFlag` | `boolean` | spec 011 | Convenience for filter rendering. |
| `orderState` | `string` | spec 011 | |
| `paymentState` | `string` | spec 011 | |
| `fulfillmentState` | `string` | spec 011 | |
| `refundState` | `string` | spec 011 | |
| `grandTotalMinor`, `currency` | `number`, `string` | spec 011 | |
| `placedAt` | `Date` | spec 011 | |

### `OrderDetail`

| Field | Type | Source | Notes |
|---|---|---|---|
| `id`, `number`, `marketCode`, `b2bFlag` | as above | spec 011 | |
| `customer` | `{ id, displayName, email?, phone? }` | spec 011 | Phone / email only when admin's permission set includes `orders.pii.read`. |
| `shippingAddress` | `Address` | spec 011 | Reused from spec 004 address shape. |
| `paymentSummary` | `{ method, state, capturedMinor, refundedMinor, currency }` | spec 011 | |
| `lineItems` | `LineItem[]` | spec 011 | |
| `shipments` | `Shipment[]` | spec 011 | Per-shipment carrier reference + tracking + state. |
| `totals` | `TotalsBreakdown` | spec 011 (snapshot at order time) | |
| `timelineCursor` | `string \| null` | spec 011 | First page already loaded with the detail; subsequent pages via cursor. |
| `rowVersion` | `number` | spec 011 | Optimistic concurrency. |
| `sourceQuoteId` | `string \| null` | spec 011 | Triggers `<SourceQuoteChip>` when set. |

### `LineItem`

| Field | Type | Notes |
|---|---|---|
| `id`, `productId`, `sku` | `string` | |
| `name (ar/en)` | localized | From spec 005's product. |
| `qty` | `number` | |
| `deliveredQty` | `number` | Drives the per-line refund cap. |
| `alreadyRefundedQty` | `number` | Eager refund-cap input. |
| `unitPriceMinor`, `lineSubtotalMinor` | `number` | |

### `TimelineEntry`

| Field | Type |
|---|---|
| `id` | `string` |
| `machine` | `'order' \| 'payment' \| 'fulfillment' \| 'refund'` |
| `fromState`, `toState` | `string` |
| `actor` | `{ kind: 'admin' \| 'customer' \| 'system'; id?: string; displayName?: string }` |
| `reasonNote` | `string \| null` |
| `occurredAt` | `Date` |
| `auditPermalink` | `string` (spec 015 reader URL) |
| `metadata` | `Record<string, unknown>` (free-form for stream-specific extras like carrier ref) |

### `TransitionDecision`

Returned by `lib/orders/transition-gate.ts`:

```ts
type TransitionDecision =
  | { kind: 'render'; actionKey: string; requiredPermission: string; labelKey: string; toState: string; }
  | { kind: 'hide'; reason: 'permission_missing' | 'state_machine_disallowed'; }
  | { kind: 'render_disabled'; reason: 'order_closed' | 'shipment_blocking'; labelKey: string; };
```

### `RefundDraft`

| Field | Type | Notes |
|---|---|---|
| `orderId` | `string` | |
| `lines` | `Array<{ lineId: string; qty: number; amountMinor: number }>` | |
| `reasonNote` | `string` | ≥ 10 chars, ≤ 2000. |
| `idempotencyKey` | `string` | UUID v4; rotated on 412 reload only. |
| `derivedTargetStates` | `{ payment: string; refund: string }` | Computed client-side for the confirmation step; server is authoritative on the actual transition. |
| `requiresStepUp` | `boolean` | Computed: full-amount OR amountMinor > env threshold for the order's market. |
| `stepUpAssertionId` | `string \| null` | Populated after `<StepUpDialog>` succeeds. |

### `InvoiceSectionState`

| Field | Type | Notes |
|---|---|---|
| `latestVersion` | `string` | |
| `status` | `'pending' \| 'available' \| 'failed'` | |
| `downloadUrl` | `string \| null` | Present when `available`. |
| `errorReasonCode` | `string \| null` | Present when `failed`. |
| `lastChangedAt` | `Date` | |

### `OrdersExportJob`

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | |
| `filterSnapshot` | `OrdersListFilters` | Read-only snapshot at create time (Q2 / FR-021). |
| `status` | `'queued' \| 'in_progress' \| 'done' \| 'failed'` | |
| `progress` | `number` (0–100, present in `in_progress`) | |
| `rowCount` | `number \| null` | Final count when `done`. |
| `downloadUrl` | `string \| null` | |
| `error` | `{ reasonCode, message } \| null` | |
| `createdAt` | `Date` | |

### `OrdersListFilters`

| Field | Type | Notes |
|---|---|---|
| `orderStates`, `paymentStates`, `fulfillmentStates`, `refundStates` | `string[]` (multi-select) | |
| `marketCode` | `'ksa' \| 'eg' \| null` | |
| `b2bFlag` | `boolean \| null` | Tristate: true / false / unset. |
| `placedAtFrom`, `placedAtTo` | `Date \| null` | |
| `searchQuery` | `string \| null` | Order number / customer email partial match. |

---

## Client state machines

### SM-1: `RefundDraftSubmissionState`

States: `Idle`, `Validating`, `OverRefundBlocked`, `StepUpRequired`, `StepUpInProgress`, `StepUpFailed`, `Submitting`, `Submitted`, `ConflictDetected` (412), `Failed` (recoverable), `FailedTerminal`.

| From | To | Trigger | Notes |
|---|---|---|---|
| `Idle` | `Validating` | `SubmitTapped` | Eager schema check. |
| `Validating` | `OverRefundBlocked` | sum of lines exceeds captured-minus-refunded | UI surfaces inline error. |
| `Validating` | `StepUpRequired` | full-amount OR amount > env threshold | Opens `<StepUpDialog>`. |
| `Validating` | `Submitting` | below threshold + valid | Direct submit. |
| `StepUpRequired` | `StepUpInProgress` | dialog mounts | spec 004 challenge. |
| `StepUpInProgress` | `Submitting` | step-up assertion succeeds | Assertion id attached. |
| `StepUpInProgress` | `StepUpFailed` | step-up rejected / cancelled | Form returns to `Idle` with toast. |
| `Submitting` | `Submitted` | spec 013 returns 2xx | |
| `Submitting` | `ConflictDetected` | 412 row-version | Overlay preserves draft. |
| `Submitting` | `Failed` | 5xx / network | Retry with same idempotency key. |
| `Submitting` | `FailedTerminal` | spec 013 returns `refund.permission_revoked` | Routes to forbidden screen. |

### SM-2: `InvoiceSectionState`

States: `Pending`, `Available`, `Failed`.

| From | To | Trigger |
|---|---|---|
| `Pending` | `Available` | spec 012 status returns `available` |
| `Pending` | `Failed` | spec 012 status returns `failed` |
| `Failed` | `Pending` | Admin (with `orders.invoice.regenerate`) clicks `Regenerate` |
| `Available` | `Pending` | Admin (with permission) re-triggers regenerate |

### SM-3: `OrdersExportJobLifecycle`

Mirrors `OrdersExportJob.status`. Driven by `lib/orders/export-job-poller.ts` (reuses 017's pattern).

### SM-4: Transition gate evaluation (per-action)

Not a multi-state machine — a **pure function** evaluated per render of `<TransitionActionBar>`:

```
input:  current order detail, admin permission set, candidate transition
output: TransitionDecision (render / hide / render_disabled)
```

The gate is **fully deterministic** and exercised by the `tests/contract/orders.no-403-after-render.spec.ts` test (SC-004 enforcement).

---

## Validation rules (client-side)

| Field | Rule |
|---|---|
| Refund line `qty` | `1 ≤ qty ≤ deliveredQty - alreadyRefundedQty` |
| Refund line `amountMinor` | `> 0` and ≤ proportional cap (line subtotal · qty/originalQty), rounded per market currency rules |
| Refund total amount | `≤ paymentSummary.capturedMinor − paymentSummary.refundedMinor` |
| Refund `reasonNote` | required, 10 ≤ length ≤ 2000 |
| Order list filters | `placedAtTo ≥ placedAtFrom` when both set; date range span hard-capped at 366 days client-side |
| Export filter span | warning above 90 days (likely > 100k rows); hard cap on row-count enforced server-side (FR-022) |

---

## Forward-compat reservations

- `OrderDetail.vendorScope` (Phase 2) — when spec 011 adds it, the detail card displays vendor info; current rendering ignores unknown fields.
- `TimelineEntry.metadata` is free-form so new metadata (e.g., carrier-tracking-update-id) lands without a view-model bump.
- `OrdersListRow.tags` reserved for future operational tags (priority, expedited, etc.) — read-render only.
