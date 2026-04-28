# Consumed APIs

This spec is **UI-only** (FR-031). Every contract below is **owned by another spec**; this document tabulates which OpenAPI document each feature folder consumes and which generated client lives in `lib/generated/api/`.

| Feature folder | Owning spec | OpenAPI source | Generated client path |
|---|---|---|---|
| `lib/features/auth/` | 004 identity-and-access | `services/backend_api/openapi.identity.json` | `lib/generated/api/identity/` |
| `lib/features/home/` | 005 catalog (+ 022 CMS stub) | `services/backend_api/openapi.catalog.json` (+ stubbed CMS adapter) | `lib/generated/api/catalog/` |
| `lib/features/catalog/` (listing + detail) | 005 catalog, 006 search, 007 pricing-and-tax-engine, 008 inventory | `openapi.catalog.json`, `openapi.search.json`, `openapi.pricing.json`, `openapi.inventory.json` | `lib/generated/api/{catalog,search,pricing,inventory}/` |
| `lib/features/cart/` | 009 cart, 007 pricing | `openapi.cart.json` (when published — currently unified into the cart bundle), `openapi.pricing.json` | `lib/generated/api/{cart,pricing}/` |
| `lib/features/checkout/` | 010 checkout, 007 pricing, 009 cart, 008 inventory | `openapi.checkout.json`, `openapi.pricing.json` | `lib/generated/api/{checkout,pricing,inventory}/` |
| `lib/features/orders/` | 011 orders, 012 tax-invoices, 013 returns | `openapi.orders.json`, `openapi.invoices.json`, `openapi.returns.json` | `lib/generated/api/{orders,invoices,returns}/` |
| `lib/features/more/` (addresses) | 004 identity | `openapi.identity.json` | `lib/generated/api/identity/` |

## Generation strategy

- **Tool**: `openapi-generator-cli` (`@openapitools/openapi-generator-cli`) configured via `apps/customer_flutter/build.yaml`.
- **Generator**: `dart-dio`.
- **Output**: `lib/generated/api/<service>/`, gitignored. CI regenerates on every PR.
- **Versioning**: Each generated client carries the OpenAPI document's `info.version`. CI compares the generated source against the committed OpenAPI checksum and fails on drift — preventing a backend contract change from silently breaking the app.

## Escalation policy (Principle of Lane B)

- A backend gap discovered during implementation is **never patched in this PR** (FR-031).
- File a GitHub issue against the owning Phase 1B spec (e.g., `spec-004:gap:android-otp-app-hash-missing`).
- Cross-link from the relevant `apps/customer_flutter/` feature-folder TODO comment.
- Block on the 1B fix only if it affects a P1 acceptance scenario (Story 1); otherwise proceed with a stub and a tracking comment.

## Headers attached to every request

Composed by interceptors in `lib/core/api/`:

| Header | Source | Purpose |
|---|---|---|
| `Authorization: Bearer <access>` | `flutter_secure_storage` | Spec 004 auth |
| `X-Correlation-Id: <uuid>` | per-request UUID v4 | Spec 003 audit + observability |
| `Accept-Language: ar-SA \| en-SA \| ar-EG \| en-EG` | `LocaleBloc` + `MarketResolver` | Server-side localized responses |
| `X-Market-Code: ksa \| eg` | `MarketResolver` | Per-market behaviour (Principle 5) |
| `Idempotency-Key: <uuid>` | only on checkout submit retries | Spec 010 idempotency |
