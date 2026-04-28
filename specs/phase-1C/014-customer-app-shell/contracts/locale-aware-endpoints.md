# Locale-aware endpoints (customer app)

Per spec 014 FR-009a — the registry of customer-app endpoints whose response contains server-localized strings. A locale switch (FR-030, AR↔EN toggle) MUST: (a) discard any in-flight response from these endpoints and re-issue with the new `Accept-Language`; (b) invalidate any cached response (Bloc state / repository memoization) for these endpoints.

A custom Dart lint under `tool/lint/no_locale_leaky_cache.dart` walks every `Repository` / `*DataSource` whose return type touches one of these endpoints and asserts the cache key includes the active language code, or that the cache is bound to a `LocaleBloc`-keyed scope.

This file is the customer-app twin of `specs/phase-1C/015-admin-foundation/contracts/locale-aware-endpoints.md`.

## Registry

### Spec 005 (catalog)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/catalog/products/:id` | `name`, `description`, `attributes.*.label`, `restrictedRationale` |
| `GET /v1/catalog/products` | `name`, `description` per row |
| `GET /v1/catalog/categories` | `label.<locale>` |
| `GET /v1/catalog/brands` | `name.<locale>` |

### Spec 006 (search)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/search` | `hits[].name`, `hits[].snippet`, `facets[].label.<locale>` |
| `GET /v1/search/suggest` | `suggestions[].label.<locale>` |

### Spec 011 (orders)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/orders/:id` | timeline `reasonNote` strings, line `name` |
| `GET /v1/orders/:id/timeline` | `reasonNote`, `metadata.*.label` |

### Spec 012 (invoices)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/orders/:id/invoice/status` | `errorReason.<locale>` |

### Spec 022 (CMS, stub until 022 ships)

| Endpoint | Localized fields |
|---|---|
| `GET /v1/cms/home` | `banners[].title.<locale>`, `featured[].title.<locale>` |
| `GET /v1/cms/page/:slug` | `body.<locale>`, `title.<locale>` |

## Endpoints exempt from re-fetch

These endpoints do **not** carry server-localized strings; locale switches do not trigger discard / re-issue:

- `GET /v1/cart/*` (line ids, qty, ids, totals — names come from the **product** endpoints which ARE in the registry above).
- `GET /v1/checkout/sessions/*` (ids, amounts, payment-method codes — labels come from i18n keys on the client).
- `GET /v1/identity/me`, `/v1/identity/sessions/*` (ids, timestamps, `accountState` enum — label comes from i18n keys on the client).

## Lint behaviour

The lint walks `lib/features/**/data/` files for HTTP calls whose path matches a registered endpoint and asserts the calling repository keys its cache by `LocaleBloc.state.locale` (or rebuilds on locale change via `BlocListener`). On a violation:

```
[no-locale-leaky-cache] '<endpoint>' carries server-localized strings; key the
cache or repository scope on LocaleBloc.locale, or invalidate on
LocaleChanged. See specs/phase-1C/014-customer-app-shell/contracts/
locale-aware-endpoints.md
```

Adding a new endpoint to this registry is one PR with one row.
