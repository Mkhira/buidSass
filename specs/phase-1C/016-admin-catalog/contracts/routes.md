# Catalog Routes

All routes live inside spec 015's `(admin)` route group; same auth proxy, same middleware order.

| Path | Permission required | Notes |
|---|---|---|
| `/catalog` | `catalog.read` | Catalog overview — links to sub-modules. |
| `/catalog/products` | `catalog.product.read` | List (DataTable). |
| `/catalog/products/new` | `catalog.product.write` | New product editor. |
| `/catalog/products/[productId]` | `catalog.product.read` | Editor — write actions gated on `catalog.product.write`. |
| `/catalog/products/[productId]/revisions` | `catalog.product.read` | Revision history. |
| `/catalog/categories` | `catalog.category.read` | Tree editor — write gated on `catalog.category.write`. |
| `/catalog/brands` | `catalog.brand.read` | List + write per `catalog.brand.write`. |
| `/catalog/brands/new`, `/catalog/brands/[brandId]` | `catalog.brand.write` | |
| `/catalog/manufacturers` | `catalog.manufacturer.read` | |
| `/catalog/manufacturers/new`, `/catalog/manufacturers/[mfgId]` | `catalog.manufacturer.write` | |
| `/catalog/bulk-import` | `catalog.product.bulk_import` | Wizard step 1 — upload. |
| `/catalog/bulk-import/[sessionId]` | `catalog.product.bulk_import` | Wizard step 2 — review report + commit. |
| `/catalog/bulk-import/export` (route handler) | `catalog.product.export` | Streamed CSV export. |

## Permission matrix (initial)

The shell already enforces middleware permission checks. The following keys are added to spec 004's permission catalog (escalation: file `spec-004:gap:catalog-permissions` if missing on day 1):

```
catalog.read
catalog.product.read
catalog.product.write
catalog.product.bulk_import
catalog.product.export
catalog.category.read
catalog.category.write
catalog.brand.read
catalog.brand.write
catalog.manufacturer.read
catalog.manufacturer.write
```

## Sidebar group registration

When the admin's nav-manifest includes any of the catalog read keys, spec 015's sidebar surfaces a "Catalog" group with the relevant sub-entries. The group is contributed via the manifest, not hard-coded — adding a new sub-module (e.g., spec 017's inventory) is a pure manifest change.
