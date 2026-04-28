# Phase 1 Data Model: Admin Inventory

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)
**Date**: 2026-04-27

UI-only spec (FR-022). All entities below are **client-side view-models**. No new server-side tables.

---

## Client view models

### `StockSnapshot`

| Field | Type | Source | Notes |
|---|---|---|---|
| `skuId` | `string` | spec 005 / 008 | |
| `warehouseId` | `string` | spec 008 | |
| `available` | `number` | spec 008 | available-to-sell (Principle 11). |
| `onHand` | `number` | spec 008 | physical on-hand. |
| `reserved` | `number` | spec 008 | derived; used for the snapshot card. |
| `rowVersion` | `number` | spec 008 | Optimistic concurrency. |

### `AdjustmentDraft`

| Field | Type | Notes |
|---|---|---|
| `warehouseId`, `skuId` | `string` | |
| `delta` | `number` (signed integer) | Validated against `onHand + delta >= 0` unless `writeoff_below_zero` permission held. |
| `reasonCode` | `string` | From the server-published catalog (R4). |
| `batchId` | `string \| null` | Required iff SKU is batch-tracked. |
| `note` | `string` | Required (≥ 10 chars) when reasonCode ∈ {`theft_loss`, `write_off_below_zero`, `breakage`}. |
| `rowVersion` | `number` | Carried back to the server. |
| `idempotencyKey` | `string` | UUID v4; rotated only when the form's snapshot is reloaded after a 412. |

### `LowStockRow`

| Field | Type | Source | Notes |
|---|---|---|---|
| `skuId`, `name (ar/en)` | spec 005 | |
| `warehouseId` | spec 008 | |
| `available` | `number` | |
| `threshold` | `number` | Per-SKU reorder threshold from spec 008. |
| `severityRank` | `number` | client-derived: lower `available / threshold` = higher rank. |
| `velocity7d`, `velocity30d`, `velocity90d` | `number` | spec 008. |
| `nearExpiry` | `boolean` | True if any active batch falls inside the active warehouse threshold. |

### `BatchViewModel`

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | |
| `skuId` | `string` | |
| `lotNumber` | `string` | |
| `supplierReference` | `string \| null` | |
| `manufacturedOn` | `Date` | |
| `expiresOn` | `Date` | |
| `onHand` | `number` | |
| `coaDocumentId` | `string \| null` | |
| `receiptId` | `string \| null` | |
| `receiptReversed` | `boolean` | |

### `ExpiryLane`

Three lanes computed client-side from a single server response; the lane assignment is stable for the session (no flicker between renders).

| Field | Type |
|---|---|
| `kind` | `'near_expiry' \| 'expired' \| 'future'` |
| `thresholdDays` | `number` (active warehouse threshold or global default 30) |
| `batches` | `BatchViewModel[]` |

### `ReservationViewModel`

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | |
| `ownerKind` | `'cart' \| 'order' \| 'quote'` | |
| `ownerId` | `string` | Truncated for display, expandable. |
| `skuId` | `string` | |
| `warehouseId` | `string` | |
| `qty` | `number` | |
| `expiresAt` | `Date` | Server-authoritative timestamp. |
| `ttlSecondsRemaining` | `number` | Client-derived: `max(0, (expiresAt - now) / 1000)`. Updated by a single 1-Hz ticker for the whole table, paused when the tab is hidden (Page Visibility API). NOT polled from the server per row (FR-016a). |
| `createdAt` | `Date` | |
| `actorKind` | `'system' \| 'admin'` | |
| `actorId` | `string \| null` | Admin id when `actorKind === 'admin'`. |

### `LedgerRow`

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | |
| `skuId`, `warehouseId` | `string` | |
| `delta` | `number` (signed) | |
| `reasonCode` | `string` | i18n-keyed for display. |
| `source` | `'manual' \| 'reservation_convert' \| 'receipt' \| 'return' \| 'write_off' \| 'system'` | |
| `batchId` | `string \| null` | |
| `actor` | `{ kind, id, displayName? }` | |
| `occurredAt` | `Date` | |
| `auditPermalink` | `string` | Spec 015's audit-log reader URL. |

### `ExportJob`

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | |
| `status` | `'queued' \| 'in_progress' \| 'done' \| 'failed'` | |
| `progress` | `number` (0–100, only present in `in_progress`) | |
| `downloadUrl` | `string \| null` | Present when `done`; presigned URL. |
| `error` | `{ reasonCode, message } \| null` | Present when `failed`. |
| `createdAt` | `Date` | |

### `Warehouse`

| Field | Type |
|---|---|
| `id`, `code`, `name (ar/en)` | `string` |
| `marketCode` | `'ksa' \| 'eg'` |
| `nearExpiryThresholdDays` | `number \| null` (overrides global 30) |

---

## Client state machines

### SM-1: `AdjustmentSubmissionState`

States: `Idle`, `Validating`, `Submitting`, `Submitted`, `ConflictDetected` (412), `BelowZeroBlocked`, `MissingNoteBlocked`, `Failed` (recoverable), `FailedTerminal`.

| From | To | Trigger | Notes |
|---|---|---|---|
| `Idle` | `Validating` | `SubmitTapped` | Eager client validation. |
| `Validating` | `BelowZeroBlocked` | `delta` would cross zero AND admin lacks `writeoff_below_zero` | UI surfaces the localized error inline; no submit. |
| `Validating` | `MissingNoteBlocked` | reason code requires note + note < 10 chars | Same. |
| `Validating` | `Submitting` | All validations pass | Idempotency key carried. |
| `Submitting` | `Submitted` | spec 008 returns 2xx | Toast + ledger row prepended. |
| `Submitting` | `ConflictDetected` | 412 row-version conflict | Editor shows reload overlay; preserves draft side panel. |
| `ConflictDetected` | `Idle` | Admin reloads + resumes | Idempotency key rotated. |
| `Submitting` | `Failed` | spec 008 5xx / network | Retry button; same idempotency key. |
| `Submitting` | `FailedTerminal` | spec 008 returns `inventory.permission_revoked` | Hard fail; routes to forbidden screen. |

### SM-2: `BatchLifecycle`

States: `Active`, `NearExpiry`, `Expired`, `WrittenOff`.

| From | To | Trigger |
|---|---|---|
| `Active` | `NearExpiry` | `expiresOn - now <= activeThresholdDays` |
| `NearExpiry` | `Expired` | `expiresOn < now` |
| any | `WrittenOff` | All on-hand removed via `write_off_expiry` adjustments |

The UI reads server state directly; transitions are computed not stored. Visual state pill + lane assignment derive from this machine.

### SM-3: `ReservationLifecycle`

States: `Active`, `Released`, `Expired`.

| From | To | Trigger |
|---|---|---|
| `Active` | `Released` | Manual admin release (FR-017) OR system release (cart abandoned, order placed, etc.) |
| `Active` | `Expired` | `ttlSecondsRemaining <= 0` |

The UI surfaces only `Active` reservations; released and expired entries appear in the audit-log reader and the ledger as movement context.

### SM-4: `ExportJobLifecycle`

Mirrors `ExportJob.status` exactly; the poller (R5) drives transitions client-side.

---

## Validation rules (client-side)

| Field | Rule |
|---|---|
| `delta` | non-zero signed integer; absolute value ≤ env cap (default 100k per single adjustment, configurable) |
| `note` | when reason code ∈ {`theft_loss`, `write_off_below_zero`, `breakage`}: required, ≥ 10 chars, ≤ 2000 chars; otherwise optional ≤ 2000 chars |
| `batchId` | required iff SKU is batch-tracked (`spec 005 sku.batchTracked`) |
| `delta` (additional) | client-precheck: `onHand + delta < 0 ⇒ require writeoff_below_zero permission` (server is authoritative) |
| Threshold edit | non-negative integer ≤ env cap (default 1M, configurable) |
| Batch `manufacturedOn` ≤ `expiresOn` | required |
| Batch quantity | positive integer |

---

## Forward-compat reservations

- `ReservationViewModel.vendorScope` — Phase 2 marketplace; reserved for future use.
- `LedgerRow.source` is a string union — adding `transfer_in` / `transfer_out` requires only a new i18n key.
- `Warehouse.timezone` not yet surfaced — research notes that the warehouse's timezone (when published by spec 008) drives the calendar's local-day boundaries; client renders UTC for v1 with a tooltip surfacing the local time.
