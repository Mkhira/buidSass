# Consumed APIs

UI-only spec (FR-031). Every contract below is **owned by another spec**.

| Surface | Owning spec | OpenAPI source | Wrapper |
|---|---|---|---|
| Product CRUD + state transitions + revisions | 005 catalog | `services/backend_api/openapi.catalog.json` | `apps/admin_web/lib/api/clients/catalog.ts` (extends spec 015's wrapper) |
| Category tree CRUD | 005 catalog | same | same |
| Brand / manufacturer CRUD | 005 catalog | same | same |
| Bulk-import dry-run + commit + report-download | 005 catalog | same | same |
| Streamed CSV export | 005 catalog | same | same — streaming via `Response.body` pipe |
| Storage signed-URL issuance for media + documents | 003 storage abstraction | `openapi.json` (storage paths) | `apps/admin_web/lib/api/clients/storage.ts` |
| Audit emissions (read-only here) | 003 foundations + spec 015 reader | `openapi.json` | spec 015's `audit.ts` wrapper |

## Auth proxy boundary

Inherits spec 015's auth-proxy completely. Every browser request goes through a Next.js Route Handler under `app/(admin)/catalog/.../route.ts` or `app/api/...`. No catalog page calls a backend URL directly.

**Direct-to-storage uploads**: the only browser → non-Next.js request in this spec is the upload PUT to a presigned URL issued by spec 003. The presigned URL is short-lived and scoped to a single object; no auth header needs to flow on that request.

## Headers attached to backend calls

Same as spec 015 — Bearer token from sealed cookie, correlation id, accept-language, market-code. No additions.

## Escalation policy

Standard: any backend gap discovered during implementation is **never patched here** (FR-031). File issues against spec 005 (catalog) or spec 003 (foundations) with the prefix `spec-005:gap:<short-description>` etc., cross-link from the relevant `apps/admin_web/components/catalog/` TODO comment.
