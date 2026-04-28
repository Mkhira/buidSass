# Client-emitted events (catalog module)

Adds to spec 015's telemetry vocabulary. Same `TelemetryAdapter` interface, same PII guardrails.

| Event | Trigger | Properties |
|---|---|---|
| `catalog.products.list.opened` | `/catalog/products` rendered | — |
| `catalog.product.editor.opened` | Product editor rendered | `is_new` (bool), `state` |
| `catalog.product.tab.changed` | AR ↔ EN content tab | `to_locale` |
| `catalog.product.saved` | Save success | `state` (post-save) |
| `catalog.product.save.conflict` | 412 returned | — |
| `catalog.product.published` | Publish-now success | — |
| `catalog.product.scheduled` | Schedule success | `lead_time_minutes` (rounded down to nearest hour) |
| `catalog.product.discarded` | Draft discarded | — |
| `catalog.product.restricted.toggled` | Restricted flag changed | `to` (bool) |
| `catalog.media.upload.started` | Upload kicked off | `kind` ('image' / 'document'), `size_bucket` ('xs' / 's' / 'm' / 'l') |
| `catalog.media.upload.succeeded` | Upload + variant generation done | `kind`, `duration_bucket` |
| `catalog.media.upload.failed` | Upload failure | `reason_code` |
| `catalog.category.tree.opened` | `/catalog/categories` rendered | — |
| `catalog.category.reorder.committed` | DnD reorder confirmed | — |
| `catalog.category.reorder.reverted` | Optimistic reorder rolled back | `reason_code` |
| `catalog.category.deactivate.confirmed` | Deactivate completes | `affected_product_count_bucket` |
| `catalog.brand.saved` | Brand mutation success | `is_new` |
| `catalog.manufacturer.saved` | Manufacturer mutation success | `is_new` |
| `catalog.bulk_import.upload.started` | Wizard step 1 submit | `row_count_bucket` |
| `catalog.bulk_import.dryrun.completed` | Step 2 reached | `validated_count_bucket`, `errored_count_bucket` |
| `catalog.bulk_import.commit.succeeded` | Commit OK | `validated_count_bucket` |
| `catalog.bulk_import.commit.failed` | Commit aborted | `reason_code` |
| `catalog.export.started` | Export action triggered | — |

## PII guard rails

- No SKU, brand name, category label, or product id values are emitted — they're operationally identifying.
- `*_bucket` properties are coarse-grained (powers-of-10 row buckets, predefined size buckets) so cardinality stays low and individual catalog rows are not de-anonymizable.
- `lead_time_minutes` is rounded down to the nearest hour to avoid timestamp leakage.
- Test `tests/unit/catalog/telemetry.pii-guard.test.ts` asserts every event's property set.
