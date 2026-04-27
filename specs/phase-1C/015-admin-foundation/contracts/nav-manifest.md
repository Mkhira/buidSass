# Nav-manifest contributions

Per spec 015 FR-028g. Every admin module ships its sidebar-group contribution as a JSON file under `services/backend_api/src/<...>/NavManifest/<module>.json` (path owned by spec 004). The spec-004 `/v1/admin/nav-manifest` endpoint composes the contributions, filters each entry by the calling admin's permission set, and returns the result.

This document is the **client-side contract** the shell expects each contribution to satisfy. Server-side validation is owned by spec 004.

## Schema

```json
{
  "groupId": "catalog",
  "labelKey": "nav.group.catalog",
  "iconKey": "package",
  "order": 200,
  "entries": [
    {
      "id": "catalog.products",
      "labelKey": "nav.entry.catalog.products",
      "iconKey": "boxes",
      "route": "/catalog/products",
      "requiredPermissions": ["catalog.product.read"],
      "order": 10,
      "badgeCountKey": null
    }
  ]
}
```

| Field | Notes |
|---|---|
| `groupId` | Stable id, kebab-case, lowercase. Collisions across modules are a build error. |
| `labelKey` | i18n key resolved from `messages/{en,ar}.json`. Both locales required. |
| `iconKey` | Resolves to a `lucide-react` icon name. |
| `order` | Float, sorted ascending. Each module reserves a 100-wide range (catalog 200-299, inventory 300-399, orders 400-499, customers 500-599) to avoid renumber wars. |
| `entries[].requiredPermissions` | Logical AND. An entry is hidden if the admin lacks any listed key. |
| `entries[].badgeCountKey` | Optional. Resolves to a numeric source (e.g., `pending_returns_count`); spec 023 owns the source endpoints. Null until 023 ships. |

## Module assignments (Phase 1C)

| Module | Contribution file | Order range |
|---|---|---|
| Spec 015 (audit, /me) | `NavManifest/foundation.json` | 100–199 |
| Spec 016 catalog | `NavManifest/catalog.json` | 200–299 |
| Spec 017 inventory | `NavManifest/inventory.json` | 300–399 |
| Spec 018 orders | `NavManifest/orders.json` | 400–499 |
| Spec 019 customers | `NavManifest/customers.json` | 500–599 |
| Future: 020 verification | `NavManifest/verification.json` | 600–699 |
| Future: 021 b2b | `NavManifest/b2b.json` | 700–799 |
| Future: 022 cms | `NavManifest/cms.json` | 800–899 |
| Future: 023 notifications + support | `NavManifest/support.json` | 900–999 |

## Static fallback

Until spec 004 ships the loader (tracked as `spec-004:gap:nav-manifest-loader`), the admin app loads the contribution files at build time from a known path under `apps/admin_web/lib/auth/nav-manifest-static/<module>.json` (a copy of each spec's contribution). The shell applies the same permission filter client-side. Cutover from static → server-driven is a one-line change in `lib/auth/nav-manifest.ts` once the endpoint lands; contribution files don't move.

## Drift CI

```bash
pnpm catalog:check-nav-manifest
# Asserts every group/entry id is unique, every labelKey resolves in both
# locales, every requiredPermissions key exists in
# contracts/permission-catalog.md, every order is within the module's reserved
# range. Fails the build on any violation.
```
