# Client-emitted events (inventory module)

Adds to spec 015's telemetry vocabulary. Same `TelemetryAdapter`, same PII guardrails.

| Event | Trigger | Properties |
|---|---|---|
| `inventory.stock.list.opened` | `/inventory/stock` rendered | — |
| `inventory.stock.detail.opened` | `/inventory/stock/[skuId]` rendered | `warehouse_count_bucket` |
| `inventory.adjust.opened` | `/inventory/adjust` rendered | `prefilled` (bool) |
| `inventory.adjust.submitted` | Submit success | `reason_code`, `delta_sign` ('+' / '-'), `delta_size_bucket` ('xs' / 's' / 'm' / 'l' / 'xl') |
| `inventory.adjust.below_zero_blocked` | Eager validation blocked submit | `reason_code` |
| `inventory.adjust.missing_note_blocked` | Eager validation blocked submit | `reason_code` |
| `inventory.adjust.below_zero_confirmed` | Below-zero confirm dialog accepted | `reason_code` |
| `inventory.adjust.conflict_detected` | 412 returned | — |
| `inventory.adjust.conflict_resolved` | Admin reloaded + resubmitted | `same_delta` (bool) |
| `inventory.barcode.scan.opened` | Camera scanner opened | — |
| `inventory.barcode.scan.matched` | Scanner returned a barcode resolved to a SKU | — |
| `inventory.lowstock.opened` | `/inventory/low-stock` rendered | `row_count_bucket` |
| `inventory.lowstock.threshold.edited` | Inline threshold update success | — |
| `inventory.lowstock.open_in_adjust` | Quick-action to adjustment form | — |
| `inventory.batches.list.opened` | `/inventory/batches` rendered | `row_count_bucket` |
| `inventory.batches.created` | Batch create success | `has_coa_document` (bool) |
| `inventory.batches.delete_blocked_nonzero` | Delete attempted on a batch with on-hand | — |
| `inventory.expiry.opened` | `/inventory/expiry` rendered | `near_expiry_count_bucket`, `expired_count_bucket` |
| `inventory.expiry.calendar_opened` | Drill-in to date grid | — |
| `inventory.reservations.opened` | `/inventory/reservations` rendered | `row_count_bucket` |
| `inventory.reservations.released` | Manual release success | `owner_kind` |
| `inventory.ledger.opened` | `/inventory/ledger` rendered | — |
| `inventory.ledger.export.requested` | Export action triggered | `row_count_bucket` |
| `inventory.ledger.export.completed` | Export job reached `done` | `duration_bucket` |
| `inventory.ledger.export.failed` | Export job reached `failed` | `reason_code` |

## PII guard rails

- No SKU id, warehouse id, batch id, lot number, supplier reference, or product name is emitted — they're operationally identifying.
- `*_bucket` properties are coarse-grained.
- `delta_size_bucket` collapses absolute delta into 5 buckets (1, 2-9, 10-99, 100-999, ≥1000) so cardinality stays low.
- `owner_kind` is one of three closed values (`cart` / `order` / `quote`); no owner id leaks.
- Test `tests/unit/inventory/telemetry.pii-guard.test.ts` asserts every event's property set against the allow-list.
