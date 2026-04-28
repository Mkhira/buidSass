# Admin Permission Catalog

Single registry of every permission key consumed across the Phase 1C admin specs (015 – 019). Spec 004's permission catalog mirrors this file; downstream specs **append** rather than re-declare.

A CI check (owned by spec 015) diffs the keys below against the catalog spec 004 returns and fails the build on drift.

## Conventions

- Keys are dot-segmented: `<module>.<noun>.<verb>` or `<module>.<verb>`.
- `*.read` keys gate visibility (list / detail).
- `*.write` keys gate mutating UI (forms, action buttons).
- `*.pii.read` keys are field-level — server pre-redacts the wire response when missing; client `<MaskedField>` is defence-in-depth.
- A permission missing from this file is treated as **undefined** (not "denied"). The shell's policy in that case is documented on the consuming UI element (e.g., spec 018 FR-020 source-quote chip "default-to-visible while undefined").

## Registry

### Shell + audit (spec 015)

| Key | Purpose |
|---|---|
| `audit.read` | Read access to the audit-log reader (`/audit`). |

### Catalog (spec 016)

| Key | Purpose |
|---|---|
| `catalog.read` | Catalog overview visibility. |
| `catalog.product.read` | Products list / detail. |
| `catalog.product.write` | Product create / edit / publish / discard. |
| `catalog.product.bulk_import` | Bulk-import wizard. |
| `catalog.product.export` | CSV export action. |
| `catalog.category.read` | Categories tree. |
| `catalog.category.write` | Categories drag-reorder / add / edit / deactivate. |
| `catalog.brand.read` | Brands list / detail. |
| `catalog.brand.write` | Brand mutations. |
| `catalog.manufacturer.read` | Manufacturers list / detail. |
| `catalog.manufacturer.write` | Manufacturer mutations. |

### Inventory (spec 017)

| Key | Purpose |
|---|---|
| `inventory.read` | Stock-by-SKU + ledger visibility. |
| `inventory.adjust` | Stock adjustment form (above zero). |
| `inventory.writeoff_below_zero` | Override on adjustments crossing zero (mandatory note + confirmation). |
| `inventory.threshold.read` | Low-stock queue visibility. |
| `inventory.threshold.write` | Per-SKU threshold inline edit. |
| `inventory.batch.read` | Batches list / expiry calendar. |
| `inventory.batch.write` | Batch create / edit. |
| `inventory.batch.writeoff` | Clear expired batches via write-off. |
| `inventory.reservation.read` | Reservations table. |
| `inventory.reservation.release` | Manual release action. |

### Orders (spec 018)

| Key | Purpose |
|---|---|
| `orders.read` | Orders list / detail. |
| `orders.pii.read` | Customer email / phone visibility on the order's customer card. **Distinct from `customers.pii.read`** — same fields, different access scope. |
| `orders.fulfillment.write` | Fulfillment-state transitions (mark packed / handed to carrier / delivered). |
| `orders.payment.write` | Admin-driven payment-state forces (rare; platform-role gated). |
| `orders.cancel` | Cancel order action (FR-012a). |
| `orders.refund.initiate` | Refund flow. Step-up MFA required above env threshold or for full-amount refunds. |
| `orders.invoice.read` | Invoice download. |
| `orders.invoice.regenerate` | Invoice render-queue retry. |
| `orders.export` | Finance CSV export. Gated additionally on `flags.financeExportEnabled` until spec 011's CSV header schema is published. |
| `orders.quote.read` | Source-quote chip visibility. Defined when spec 021 ships; UI defaults to visible while undefined. |

### Customers (spec 019)

| Key | Purpose |
|---|---|
| `customers.read` | Customers list / profile. |
| `customers.pii.read` | Customer email / phone visibility on the customer profile. **Distinct from `orders.pii.read`** — same fields, different access scope. |
| `customers.b2b.read` | B2B Company card + Companies sub-entry. |
| `customers.suspend` | Suspend action. Step-up MFA always required. |
| `customers.unlock` | Unlock action. Step-up MFA always required. |
| `customers.password_reset.trigger` | Trigger password-reset action. Step-up MFA always required. |

## PII-key split rationale

`orders.pii.read` and `customers.pii.read` gate **the same data fields** (email, phone) but at different access scopes:

- An admin investigating a fulfillment issue may need order-context PII without a license to browse all customer profiles.
- A customer-support admin may need profile-level PII without seeing every order's customer card on the orders list.

Split keys allow that separation. An admin holding **both** sees PII everywhere; an admin holding **neither** sees the `<MaskedField>` placeholder everywhere; an admin holding one sees PII only in that surface.

## Drift CI

```bash
# In apps/admin_web's CI workflow (spec 015):
pnpm catalog:check-permissions
# Compares the keys in this file to the catalog returned by spec 004's
# /v1/admin/permission-catalog endpoint. Fails on any add / remove not present
# in both surfaces.
```

If spec 004 hasn't shipped the endpoint, the check is a no-op and a `spec-004:gap:permission-catalog-endpoint` issue is filed.
