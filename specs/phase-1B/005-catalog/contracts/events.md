# Catalog Domain Events — Audit Log & Reindex Contract

**Feature**: `specs/phase-1B/005-catalog/spec.md`
**Owned-by**: spec 005 (emits) / spec 003 (persists audit) / spec 006 (consumes reindex)
**Transport**: MediatR `INotification` inside the monolith.

All events share the audit envelope from spec 003/004:

```jsonc
{
  "actorId": "uuid | null",
  "actorType": "admin | system | customer",
  "targetId": "uuid",
  "targetType": "category | brand | manufacturer | product | variant | media | document",
  "actionKey": "catalog.<event-name>",
  "before": "object | null",
  "after": "object | null",
  "marketCode": "EG | KSA",
  "correlationId": "string",
  "occurredAt": "ISO-8601"
}
```

---

## Audit events (persisted by spec 003)

| Action key | Emitter | Notes |
|---|---|---|
| `catalog.category.created` | `CreateCategory` handler | |
| `catalog.category.updated` | `UpdateCategory` handler | before/after diff on slug, name, active |
| `catalog.category.moved` | `MoveCategory` handler | before/after include `parentId`, `position`, `path` |
| `catalog.category.deactivated` | `DeactivateCategory` | |
| `catalog.category.reactivated` | `ReactivateCategory` | |
| `catalog.brand.created` | `CreateBrand` | |
| `catalog.brand.updated` | `UpdateBrand` | |
| `catalog.brand.deactivated` | `DeactivateBrand` | |
| `catalog.manufacturer.created` | `CreateManufacturer` | |
| `catalog.manufacturer.updated` | `UpdateManufacturer` | |
| `catalog.manufacturer.deactivated` | `DeactivateManufacturer` | |
| `catalog.product.created` | `CreateProduct` | draft state |
| `catalog.product.updated` | `UpdateProduct` | content/attribute/category diff |
| `catalog.product.published` | `PublishProduct` | FR-015 parity check must have passed |
| `catalog.product.archived` | `ArchiveProduct` | |
| `catalog.product.restriction.changed` | `UpdateProduct` when `restricted_for_purchase` or rationale changes | Emits even if toggled back to false; spec 009 listens to re-validate carts |
| `catalog.variant.created` | `CreateVariant` | |
| `catalog.variant.updated` | `UpdateVariant` | |
| `catalog.variant.activated` | `UpdateVariant` when `status` → `active` | |
| `catalog.variant.deactivated` | `UpdateVariant` when `status` → `inactive` | |
| `catalog.variant.archived` | `UpdateVariant` when `status` → `archived` | SKU freed per FR-009a + Clarification Q3 |
| `catalog.variant.sku.reused` | `CreateVariant` detecting reuse of a previously-archived SKU | `before.meta.previousVariantIds` populated |
| `catalog.media.uploaded` | `UploadProductMedia` | only after clean virus-scan |
| `catalog.media.reordered` | `ReorderProductMedia` | |
| `catalog.media.primary.changed` | `SetPrimaryMedia` | |
| `catalog.media.deleted` | `DeleteProductMedia` | soft delete |
| `catalog.document.uploaded` | `UploadProductDocument` | |
| `catalog.document.deleted` | `DeleteProductDocument` | |

---

## Reindex events (consumed by spec 006)

Fire-and-forget in-process notifications. Spec 006 subscribes and enqueues an incremental reindex. SC-008 tests latency ≤ 2 s.

| Event | Emitted from | Payload |
|---|---|---|
| `ProductCreated` | `CreateProduct` | `{ productId, marketCode }` |
| `ProductUpdated` | `UpdateProduct` | `{ productId, changedFields[], marketCode }` |
| `ProductPublished` | `PublishProduct` | `{ productId, marketCode }` |
| `ProductArchived` | `ArchiveProduct` | `{ productId, marketCode }` |
| `ProductVariantChanged` | Any variant create/update/status change | `{ productId, variantId, marketCode }` |
| `ProductMediaChanged` | Media upload/reorder/primary/delete | `{ productId, marketCode }` |
| `CategoryTreeChanged` | Category create/update/move/deactivate/reactivate | `{ categoryId, marketCode }` |
| `BrandChanged` | Brand create/update/deactivate | `{ brandId, marketCode }` |

---

## FR traceability

- **FR-019** (search reindex) satisfied by the reindex events above.
- **FR-026** (audit on every CRUD + publish/archive + restriction change + media/document change) satisfied by the audit events above.
- **FR-027** (multi-vendor-ready) — `vendor_id` is included in every audit `before`/`after` so the transition to Phase 2 marketplace replay is available.

Any new catalog handler MUST register a new event name in this catalog and ship a paired audit-log test.
