# Inventory Routes

All routes inside spec 015's `(admin)` group; same auth-proxy + middleware order.

| Path | Permission required | Notes |
|---|---|---|
| `/inventory` | `inventory.read` | Inventory overview cards. |
| `/inventory/stock` | `inventory.read` | Stock-by-SKU list. |
| `/inventory/stock/[skuId]` | `inventory.read` | SKU detail. |
| `/inventory/adjust` | `inventory.adjust` | Adjustment form (deep-linkable). |
| `/inventory/low-stock` | `inventory.threshold.read` | Low-stock queue. Inline-edit gated by `inventory.threshold.write`. |
| `/inventory/batches` | `inventory.batch.read` | Batches list. |
| `/inventory/batches/new` | `inventory.batch.write` | Batch create. |
| `/inventory/batches/[batchId]` | `inventory.batch.read` | Batch detail; write gated by `inventory.batch.write`. |
| `/inventory/expiry` | `inventory.batch.read` | Expiry calendar. |
| `/inventory/reservations` | `inventory.reservation.read` | Reservations table. Release gated by `inventory.reservation.release`. |
| `/inventory/ledger` | `inventory.read` | Ledger list. |
| `/inventory/ledger/exports/[jobId]` | `inventory.read` | Export-job status page. |
| `/inventory/receipts/[receiptId]` | `inventory.batch.read` | **Placeholder route** for receipt-detail until a future admin-receipts spec ships. Renders the id (copyable), a localized "receipt detail not yet available" message, and a back-link to the originating batch (FR-016b). Same gate as the originating batch detail so navigation never 403s unexpectedly. |

## Permission matrix

```
inventory.read
inventory.adjust
inventory.writeoff_below_zero
inventory.threshold.read
inventory.threshold.write
inventory.batch.read
inventory.batch.write
inventory.batch.writeoff
inventory.reservation.read
inventory.reservation.release
```

The shell already enforces middleware permission checks. New keys above are added to spec 004's permission catalog via the standard escalation channel (file `spec-004:gap:inventory-permissions` if any are missing on day 1).

## Sidebar group registration

When the admin's nav-manifest includes any `inventory.*` read key, spec 015's sidebar surfaces an "Inventory" group with the relevant sub-entries. Entries are contributed via the manifest, never hard-coded.
