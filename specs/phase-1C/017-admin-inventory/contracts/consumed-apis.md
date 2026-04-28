# Consumed APIs

UI-only spec (FR-022). Every contract is **owned by another spec**.

| Surface | Owning spec | OpenAPI source | Wrapper |
|---|---|---|---|
| Stock snapshot per (sku, warehouse) | 008 inventory | `services/backend_api/openapi.inventory.json` | `apps/admin_web/lib/api/clients/inventory.ts` |
| Adjustment endpoints + reason-codes catalog + ledger list / export | 008 inventory | same | same |
| Low-stock queue + per-SKU threshold edit | 008 inventory | same | same |
| Batch CRUD + expiry feed | 008 inventory | same | same |
| Reservation list + manual release | 008 inventory | same | same |
| Export job (`POST` create, `GET` status, signed-URL download) | 008 inventory | same | same |
| Storage signed-URL issuance for COA documents | 003 storage abstraction | `openapi.json` | `apps/admin_web/lib/api/clients/storage.ts` (shared with spec 016) |
| Audit emissions (read-only via spec 015's reader) | 003 + spec 015 | `openapi.json` | spec 015's `audit.ts` wrapper |

## Auth proxy boundary

Inherits spec 015's auth-proxy completely. Every browser request goes through a Next.js Route Handler under `app/(admin)/inventory/.../route.ts` or `app/api/inventory/...`.

## Headers attached

Same as spec 015. No deltas.

## Escalation policy

Every backend gap discovered during implementation is **never patched here** (FR-022). File issues against spec 008 (inventory) or spec 003 (foundations) using `spec-008:gap:<short-description>` etc. Cross-link from the relevant `apps/admin_web/components/inventory/` TODO comment.
