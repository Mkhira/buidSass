# Locale-aware endpoints

Per spec 015 FR-028e — the registry of backend endpoints whose response contains server-localized strings. `react-query` hooks fetching these endpoints MUST include the active locale in the query key. A lint rule (`tools/lint/no-locale-leaky-cache.ts`) walks every `useQuery` / `useSuspenseQuery` call and rejects the build if a key array does not include `useLocale()` for an endpoint listed below.

## Why

`Accept-Language: ar-SA` and `Accept-Language: en-SA` return different bodies for the same URL on these endpoints. If the cache is keyed only by URL + path params, an admin who toggles AR↔EN sees stale strings until the cache TTL elapses. Including locale in the key invalidates AR-cached entries on switch to EN and vice versa.

## Numeric / pure-id endpoints — exempt

Endpoints returning only ids, counts, timestamps, monetary minor-units (no localized strings) need not include locale in the key. The list below is the **minimum** set that must.

## Registry

### Spec 004 (identity)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/admin/customers/:id` | `lockoutState.reasonNote`, `roles.*.labelKey` (resolved server-side) |
| `GET /v1/admin/customers` | same per-row |
| `GET /v1/admin/nav-manifest` | `groups.*.labelKey`, `entries.*.labelKey` (server resolves to active locale) |

### Spec 003 (audit)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/admin/audit-log` | `actor.displayName`, server-localized `metadata.*` keys |

### Spec 005 (catalog)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/admin/catalog/products` | `name`, `description` (both AR + EN are returned, but list-row preview picks the active locale's snippet) |
| `GET /v1/admin/catalog/products/:id` | `name`, `description`, `restrictedRationale` |
| `GET /v1/admin/catalog/categories` | `label.<locale>` |
| `GET /v1/admin/catalog/brands`, `/manufacturers` | `name.<locale>` |

### Spec 008 (inventory)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/admin/inventory/reason-codes` | `labelKey` resolved to localized string |
| `GET /v1/admin/inventory/ledger` | `actor.displayName`, optional `note` |

### Spec 011 (orders)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/admin/orders/:id/timeline` | `actor.displayName`, `reasonNote`, `metadata.*.label` |
| `GET /v1/admin/orders` | `customer.displayName` |

### Spec 012 (invoices)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/admin/orders/:id/invoice/status` | `errorReason.<locale>` |

### Spec 013 (returns)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/admin/orders/:id/refunds` | `reasonNote` |

## Lint behaviour

The lint matches `useQuery({ queryKey: [...], queryFn })` calls (or the `react-query` hook variants used by `lib/api/clients/<svc>.ts`) and resolves the inferred URL. If the URL matches a row above and the key array doesn't include a literal that resolves to `locale`, the lint fails with:

```
[no-locale-leaky-cache] '<endpoint>' returns server-localized strings; include
useLocale() in the query key to avoid AR↔EN cache leaks. See
specs/phase-1C/015-admin-foundation/contracts/locale-aware-endpoints.md
```

Adding a new endpoint to this registry is one PR with one row.
