# Mobile App Screen → API Implementation Plan

> **Status:** active index · **Scope:** customer mobile app only (`apps/customer_flutter/`) · **Last updated:** 2026-05-19
>
> Admin web (`apps/admin_web/`) and internal/webhook endpoints are explicitly out of scope here — they live in their own plans. This doc is the navigable index; the per-phase implementation specs live under `specs/mobile/`.

---

## 1. Purpose

This is the **single index** for shipping the customer-facing Flutter app against the backend. It does three things:

1. Lists every customer-tagged endpoint across the 12 OpenAPI files and tags each one with the phase that owns it (the **coverage matrix** in §6).
2. Defines the **per-screen template** every mobile spec must use, so each screen entry across the 8 phase specs is consistent and Claude-implementable end-to-end without hunting through OpenAPI files (§5).
3. Splits the work into **8 sequential phases**, each carved into a standalone Spec Kit folder under `specs/mobile/phase-N-*/` that can be run via `/speckit-implement` (§4).

There are no per-screen blueprints in this document — those live inside each phase spec. Wireframes have been relocated to `docs/mobile-screens-wireframes.md` and are linked by stable anchor from each spec.

---

## 2. Source of truth — OpenAPI registry

The backend ships a separate OpenAPI document per bounded context. The mobile app consumes only the **customer-tagged** and **public-tagged** endpoints in each file.

| Alias | Domain | OpenAPI file | Backend module | Customer-tagged ops |
|---|---|---|---|---|
| IDN | Identity and access | [`openapi.identity.json`](../services/backend_api/openapi.identity.json) | `services/backend_api/Modules/Identity` | 14 |
| CAT | Catalog | [`openapi.catalog.json`](../services/backend_api/openapi.catalog.json) | `services/backend_api/Modules/Catalog` | 4 |
| SRCH | Search | [`openapi.search.json`](../services/backend_api/openapi.search.json) | `services/backend_api/Modules/Search` | 3 |
| PRC | Pricing & promotions | [`openapi.pricing.json`](../services/backend_api/openapi.pricing.json) | `services/backend_api/Modules/Pricing` | 1 |
| STK | Inventory availability | [`openapi.inventory.json`](../services/backend_api/openapi.inventory.json) | `services/backend_api/Modules/Inventory` | 1 |
| CHK | Checkout | [`openapi.checkout.json`](../services/backend_api/openapi.checkout.json) | `services/backend_api/Modules/Checkout` | 8 |
| ORD | Orders & legacy quotations | [`openapi.orders.json`](../services/backend_api/openapi.orders.json) | `services/backend_api/Modules/Orders` | 9 |
| RET | Returns & refunds | [`openapi.returns.json`](../services/backend_api/openapi.returns.json) | `services/backend_api/Modules/Returns` | 4 |
| INV | Tax invoices | [`openapi.invoices.json`](../services/backend_api/openapi.invoices.json) | `services/backend_api/Modules/TaxInvoices` | 2 |
| REV | Reviews & moderation | [`openapi.reviews.json`](../services/backend_api/openapi.reviews.json) | `services/backend_api/Modules/Reviews` | 6 customer + 2 public |
| VER | Verification | [`openapi.verification.json`](../services/backend_api/openapi.verification.json) | `services/backend_api/Modules/Verification` | 8 |
| B2B | Quotes & companies | [`openapi.b2b.json`](../services/backend_api/openapi.b2b.json) | `services/backend_api/Modules/B2B` | 20 |

**Total mobile-callable surface:** 82 endpoints (74 customer-tagged + 2 public-tagged + 6 b2b customer ops with multi-tag use).

> `openapi.json` and `packages/shared_contracts/openapi.json` are placeholder containers — do not consume them. There is **no `openapi.cart.json`** by design: cart is client-state only; the backend touches the cart only via `POST /customer/pricing/price-cart` (preview) and `POST /v1/customer/checkout/sessions` (materialize).

---

## 3. Global API requirements (apply to every phase)

### Required request headers
| Header | Required for | Value source | Where set |
|---|---|---|---|
| `Authorization` | every protected `/v1/customer/*` and `/api/customer/*` op | `Bearer <access_token>` from session store | `apps/customer_flutter/lib/core/api/auth_interceptor.dart` |
| `Accept-Language` | all ops (locale-sensitive responses) | current Flutter locale (`ar` or `en`) | `locale_market_interceptor.dart` |
| `X-Market-Code` | all ops | current market (`SA` or `EG`) | `locale_market_interceptor.dart` |
| `X-Correlation-Id` | all ops | UUIDv4 per request | `correlation_id_interceptor.dart` |
| `Idempotency-Key` | unsafe POSTs that mutate orders/payments/returns (see §6 col "Idempotent") | UUIDv4 generated per user-initiated action | `idempotency_interceptor.dart` |

### Auth lifecycle
- Bootstrap: `POST /v1/customer/identity/session/refresh` → `GET /v1/customer/identity/me` on app start. If refresh fails, route to Login and clear store.
- 401 on any protected request: trigger one refresh attempt; on second 401, sign out + Login route.
- 403 surfaces as a localized "not eligible" state, not a re-auth prompt (Principle 8 restricted products use 403).

### Error contract (uniform across all `/v1/*` and `/api/*` responses)
```json
{
  "error": { "code": "STRING_CODE", "message": "localized human text", "correlationId": "uuid", "details": { } }
}
```
- `code` drives the UI branch (mapped per screen in the spec).
- `correlationId` MUST be surfaced in every error toast (Principle 25 auditability).
- `details` carries field-level validation arrays where applicable.

### Idempotency, retries, network
- Safe methods (GET) are retried by Dio with exponential backoff (max 3) only on network/5xx.
- Unsafe methods (POST/PATCH/PUT/DELETE) are **never** auto-retried by the interceptor — only on explicit user action, and only with the same `Idempotency-Key` for the duration of one user intent (`apps/customer_flutter/lib/core/api/idempotency_interceptor.dart`).
- Offline reads fall back to last-known cached payload where the screen contract allows it (column in §6).

### Route-prefix rules
- `/v1/customer/*` — modern customer surface (identity, catalog, search, checkout, orders, returns, invoices, inventory, reviews).
- `/v1/public/*` — unauthenticated reads (review aggregates).
- `/api/customer/*` — older customer surface still in use (verification, b2b).
- `/customer/*` — pricing only (no version prefix by design — preserved here).
- Wrap each prefix in a per-alias gateway repository under `lib/features/<alias>/data/`; **never** call raw paths from widgets or Blocs (current pattern in `apps/customer_flutter/lib/core/api/`).

---

## 4. The 8 phases — owner specs & dependencies

Each phase = one Spec Kit folder under `specs/mobile/phase-N-name/` carrying the standard layout (`spec.md`, `plan.md`, `tasks.md`, `data-model.md`, `contracts/`, `quickstart.md`, `checklists/`). Each folder is independently `/speckit-implement`-able.

| # | Spec folder | Goal | Endpoints (§6 tag) | Depends on |
|---|---|---|---|---|
| 1 | [`specs/mobile/phase-1-auth-identity/`](../specs/mobile/phase-1-auth-identity/) | Foundation (core API client + interceptors + theme + routing) + auth & session screens | IDN.customer (14) | — |
| 2 | [`specs/mobile/phase-2-catalog/`](../specs/mobile/phase-2-catalog/) | Home, categories, brands, product list, product detail, restricted-product UX | CAT (4), PRC.price-cart preview, STK.availability, REV.public-aggregates (2) | 1 |
| 3 | [`specs/mobile/phase-3-search/`](../specs/mobile/phase-3-search/) | Search entry, autocomplete, results, lookup, AR normalization, facets | SRCH (3) | 1, 2 |
| 4 | [`specs/mobile/phase-4-cart-checkout/`](../specs/mobile/phase-4-cart-checkout/) | Cart panel, pricing preview, full 4-step checkout, address/shipping/payment/submit, drift, COD/bank-transfer | PRC.price-cart, CHK (8), STK.availability | 1, 2 |
| 5 | [`specs/mobile/phase-5-orders/`](../specs/mobile/phase-5-orders/) | Orders list, order detail (state + payment + fulfillment + refund sub-states), reorder, cancel, tracking, payment retry | ORD.customer-orders (5) | 4 |
| 6 | [`specs/mobile/phase-6-returns-invoices/`](../specs/mobile/phase-6-returns-invoices/) | Returns list/create/detail with photos, invoice preview + PDF download/share | RET (4), INV (2), ORD.return-eligibility | 5 |
| 7 | [`specs/mobile/phase-7-trust-compliance/`](../specs/mobile/phase-7-trust-compliance/) | Verification submit/document/resubmit/renew, reviews submit/edit/report/list | VER (8), REV (6 customer) | 1, 5 |
| 8 | [`specs/mobile/phase-8-b2b/`](../specs/mobile/phase-8-b2b/) | Quotes from cart/product, quote actions, quote documents, company profile/branches/invitations/memberships, legacy quotations | B2B (20), ORD.legacy-quotations (4) | 1, 4 |

**Dependency graph (top-to-bottom = build order):**

```
                 ┌─────────────┐
                 │  Phase 1    │ Auth + foundation (interceptors, gateway, theme)
                 │  IDN.x14    │
                 └──────┬──────┘
                        │
            ┌───────────┴───────────┐
            ▼                       ▼
     ┌─────────────┐         ┌─────────────┐
     │  Phase 2    │         │  Phase 7    │ (depends on 1 + 5; can start UI after 1, wires data after 5)
     │  Catalog    │         │  Trust/Comp │
     └──────┬──────┘         └──────▲──────┘
            │                       │
   ┌────────┴──────────┐            │
   ▼                   ▼            │
┌─────────┐      ┌────────────┐     │
│ Phase 3 │      │ Phase 4    │     │
│ Search  │      │ Cart/Chkout│     │
└─────────┘      └──────┬─────┘     │
                        │           │
                        ▼           │
                 ┌────────────┐     │
                 │ Phase 5    │─────┘
                 │ Orders     │
                 └──────┬─────┘
                        │
                        ▼
                 ┌────────────┐
                 │ Phase 6    │
                 │ Returns+Inv│
                 └────────────┘

                 ┌────────────┐
                 │ Phase 8    │ (depends on 1 + 4; surfaces beside Phase 5)
                 │ B2B        │
                 └────────────┘
```

**Bottom-nav routing** (set in Phase 1, referenced by 2/4/5/more):
- `Home` → catalog Home (Phase 2)
- `Categories` → category list (Phase 2)
- `Cart` → cart panel (Phase 4)
- `Orders` → orders list (Phase 5)
- `Settings` → "More" hub (locale + sessions + verification CTA + reviews + B2B entry)

---

## 5. Per-screen template (mandatory schema)

Every screen entry in every `specs/mobile/phase-N-*/spec.md` MUST use this template verbatim. Consistency = Claude-implementable.

```markdown
### S-<phase>.<n> <Screen Name>

**Status:** Done | In progress | Planned        ← derived from apps/customer_flutter/lib/features/<area>/screens/
**Route:** `/path/in/app`                        ← go_router path
**OpenAPI source:** `services/backend_api/openapi.<domain>.json`
**Wireframe:** [docs/mobile-screens-wireframes.md#<anchor>](../../docs/mobile-screens-wireframes.md#<anchor>)
**Bottom nav:** visible | hidden

#### Endpoints used
| Method | Path                                 | When                | Idempotent | Notes                                  |
|--------|--------------------------------------|---------------------|------------|----------------------------------------|
| POST   | /v1/customer/identity/sign-in        | on submit           | yes        | clears refresh-token store on success  |

#### Response data shape (trimmed — only fields this screen reads)
```json
{
  "accountId": "uuid",
  "accessToken": "jwt",
  "refreshToken": "jwt",
  "expiresInSeconds": 900,
  "profile": {
    "displayName": "string",
    "locale": "ar | en",
    "marketCode": "SA | EG"
  }
}
```

#### UI states
| State        | Trigger                              | What renders                                                  |
|--------------|--------------------------------------|---------------------------------------------------------------|
| initial      | first frame                          | empty form, focus first input                                 |
| loading      | submit pressed                       | button spinner, inputs disabled                               |
| loaded       | 2xx response                         | route forward                                                 |
| empty        | 200 + empty payload (where possible) | localized empty illustration + CTA                            |
| validation   | 422                                  | inline per-field errors from `error.details`                  |
| error-401    | 401                                  | re-auth bottom sheet                                          |
| error-403    | 403                                  | "not eligible" panel with explainer                           |
| error-409    | 409                                  | drift/conflict resolution UX (reusable widget from Phase 1)   |
| error-5xx    | 5xx                                  | retry banner + correlation-id text                            |
| offline      | DioException.connectionError         | offline badge + cached view (or retry CTA if no cache)        |

#### Bloc scaffold (ADR-002)
- Bloc class: `<Feature>Bloc`
- Events: `<Feature>Started`, `<Feature>Submitted`, `<Feature>Retried`, `<Feature>Refreshed`, …
- States (sealed): `<Feature>Initial`, `<Feature>Loading`, `<Feature>Loaded(data)`, `<Feature>Empty`, `<Feature>Failure(kind, message, correlationId)`
- Repo dep: `<Feature>Repository` in `lib/features/<area>/data/`

#### Acceptance criteria (DoD — must all be checked before screen is "done")
- [ ] Renders in AR + EN with correct RTL mirroring (Principle 4)
- [ ] Brand palette respected (`#1F6F5F` primary, etc. — Principle 7)
- [ ] Loading skeleton appears within 200 ms of navigation
- [ ] 401 path triggers session refresh; on failure, routes to Login and clears store
- [ ] 5xx / network error surfaces localized message + correlation-id (Principle 25)
- [ ] Restricted-product UX renders price + disabled CTA (Principle 8, where applicable)
- [ ] B2B vs consumer pricing rendered correctly (Principle 10, where applicable)
- [ ] All bilingual copy is editorial-grade Arabic, not machine-translated (Principle 4)
- [ ] Unit tests: Bloc transitions + repo happy path + 401/403/409/5xx
- [ ] Widget tests: each UI state renders without exception

#### Edge cases (always consider these, omit any that don't apply)
- Restricted product gating (Principle 8)
- B2B vs consumer pricing (Principle 10)
- Market-specific behavior (`SA` vs `EG`, Principle 5)
- Idempotency on retry of mutating ops
- Stale cart / drift on checkout (Phase 4)
- Locale switch mid-flow (Phase 1)
- Offline open → online resume
```

---

## 6. Endpoint coverage matrix (single source of truth)

Every customer-callable endpoint across the 12 OpenAPI files appears in **exactly one row** below, tagged with its owning phase spec. Admin (`/v1/admin/*`, `/api/admin/*`, `/admin/*`) and internal (`/v1/internal/*`, `/internal/*`, `/v1/webhooks/*`) endpoints are intentionally absent.

| # | Method | Path | OpenAPI file | Phase | Screen ID(s) | Idempotent | Notes |
|---|---|---|---|---|---|---|---|
| **Identity (14)** | | | | | | | |
| 1 | POST | /v1/customer/identity/register | identity | 1 | S-1.4 | no | |
| 2 | POST | /v1/customer/identity/sign-in | identity | 1 | S-1.3 | yes | sets refresh-token cookie/store |
| 3 | POST | /v1/customer/identity/sign-out | identity | 1 | S-1.10 (More) | no | clears tokens |
| 4 | POST | /v1/customer/identity/session/refresh | identity | 1 | S-1.1 (Splash) | yes | auto on 401 |
| 5 | GET  | /v1/customer/identity/me | identity | 1 | S-1.1, S-1.10 (More) | safe | profile + perms gate |
| 6 | PATCH | /v1/customer/identity/locale | identity | 1 | S-1.9 (Locale) | yes | persists `ar`/`en` |
| 7 | POST | /v1/customer/identity/otp/request | identity | 1 | S-1.5 | yes | rate-limited server-side |
| 8 | POST | /v1/customer/identity/otp/verify | identity | 1 | S-1.5 | no | one-shot per code |
| 9 | POST | /v1/customer/identity/password/reset-request | identity | 1 | S-1.6 | yes | |
| 10 | POST | /v1/customer/identity/password/reset-complete | identity | 1 | S-1.7 | no | token-bound |
| 11 | POST | /v1/customer/identity/password/change | identity | 1 | S-1.10 (Account security) | no | requires current password |
| 12 | POST | /v1/customer/identity/email/confirm | identity | 1 | S-1.8 | no | deep-link entry |
| 13 | GET  | /v1/customer/identity/sessions | identity | 1 | S-1.11 (Sessions list) | safe | |
| 14 | DEL  | /v1/customer/identity/sessions/{sessionId} | identity | 1 | S-1.11 | no | revoke remote session |
| **Catalog (4)** | | | | | | | |
| 15 | GET | /v1/customer/catalog/categories | catalog | 2 | S-2.1 (Home), S-2.2 (Categories) | safe | cached |
| 16 | GET | /v1/customer/catalog/brands | catalog | 2 | S-2.1, S-2.4 (Brand list) | safe | cached |
| 17 | GET | /v1/customer/catalog/categories/{slug}/products | catalog | 2 | S-2.3 (Category detail), S-2.5 (Product list) | safe | filters: market, page, pageSize, sort, brand, priceMin, priceMax, restricted |
| 18 | GET | /v1/customer/catalog/products/{slug} | catalog | 2 | S-2.6 (Product detail) | safe | optional market |
| **Search (3)** | | | | | | | |
| 19 | POST | /v1/customer/search/autocomplete | search | 3 | S-3.2 (Autocomplete) | safe* | debounced |
| 20 | POST | /v1/customer/search/products | search | 3 | S-3.3 (Search results) | safe* | facets + sort |
| 21 | POST | /v1/customer/search/lookup | search | 3 | S-3.4 (Lookup by SKU/barcode) | safe* | |
| **Pricing (1)** | | | | | | | |
| 22 | POST | /customer/pricing/price-cart | pricing | 4 | S-4.1 (Cart), S-4.2 (Cart pricing panel); also Phase-2 PDP preview | safe* | preview only; no side-effects |
| **Inventory (1)** | | | | | | | |
| 23 | GET | /v1/customer/inventory/availability | inventory | 2 / 4 | S-2.6 (PDP), S-4.1 (Cart) | safe | productIds + market query |
| **Checkout (8)** | | | | | | | |
| 24 | POST | /v1/customer/checkout/sessions | checkout | 4 | S-4.3 (Checkout start) | yes | idempotent on retry |
| 25 | GET  | /v1/customer/checkout/sessions/{sessionId}/summary | checkout | 4 | S-4.4 (Checkout summary) | safe | |
| 26 | PATCH | /v1/customer/checkout/sessions/{sessionId}/address | checkout | 4 | S-4.5 (Address step) | yes | |
| 27 | GET  | /v1/customer/checkout/sessions/{sessionId}/shipping-quotes | checkout | 4 | S-4.6 (Shipping step) | safe | |
| 28 | PATCH | /v1/customer/checkout/sessions/{sessionId}/shipping | checkout | 4 | S-4.6 | yes | |
| 29 | PATCH | /v1/customer/checkout/sessions/{sessionId}/payment-method | checkout | 4 | S-4.7 (Payment step) | yes | |
| 30 | POST | /v1/customer/checkout/sessions/{sessionId}/submit | checkout | 4 | S-4.8 (Order review/submit) | **yes — Idempotency-Key required** | terminal write |
| 31 | POST | /v1/customer/checkout/sessions/{sessionId}/accept-drift | checkout | 4 | S-4.9 (Drift dialog) | yes | |
| **Orders (5 customer + 4 legacy)** | | | | | | | |
| 32 | GET  | /v1/customer/orders | orders | 5 | S-5.1 (Orders list) | safe | filters: status, market, from, to, page, pageSize |
| 33 | GET  | /v1/customer/orders/{id} | orders | 5 | S-5.2 (Order detail) | safe | |
| 34 | POST | /v1/customer/orders/{id}/cancel | orders | 5 | S-5.3 (Cancel) | yes | |
| 35 | POST | /v1/customer/orders/{id}/reorder | orders | 5 | S-5.4 (Reorder) | yes | rehydrates cart |
| 36 | GET  | /v1/customer/orders/{id}/return-eligibility | orders | 6 | S-6.2 (Return create entry) | safe | called from return wizard |
| 37 | GET  | /v1/customer/quotations | orders | 8 | S-8.legacy.1 (Legacy quotations list) | safe | legacy quotations live in B2B phase |
| 38 | GET  | /v1/customer/quotations/{id} | orders | 8 | S-8.legacy.2 | safe | |
| 39 | POST | /v1/customer/quotations/{id}/accept | orders | 8 | S-8.legacy.2 | yes | |
| 40 | POST | /v1/customer/quotations/{id}/reject | orders | 8 | S-8.legacy.2 | yes | |
| **Returns (4)** | | | | | | | |
| 41 | GET  | /v1/customer/returns | returns | 6 | S-6.1 (Returns list) | safe | |
| 42 | GET  | /v1/customer/returns/{id} | returns | 6 | S-6.3 (Return detail) | safe | |
| 43 | POST | /v1/customer/returns/photos | returns | 6 | S-6.2 (Return create — upload step) | yes | multipart |
| 44 | POST | /v1/customer/orders/{orderId}/returns | returns | 6 | S-6.2 | **yes — Idempotency-Key required** | terminal write |
| **Invoices (2)** | | | | | | | |
| 45 | GET  | /v1/customer/orders/{orderId}/invoice | invoices | 6 | S-6.4 (Invoice preview) | safe | |
| 46 | GET  | /v1/customer/orders/{orderId}/invoice.pdf | invoices | 6 | S-6.5 (Invoice PDF) | safe | binary; share/save flow |
| **Reviews (6 customer + 2 public)** | | | | | | | |
| 47 | GET  | /v1/public/reviews/aggregates | reviews | 2 | S-2.5 (Product list cards) | safe | unauth ok |
| 48 | GET  | /v1/public/reviews/aggregates/{product_id} | reviews | 2 | S-2.6 (PDP rating block) | safe | unauth ok |
| 49 | POST | /v1/customer/reviews | reviews | 7 | S-7.5 (Submit review) | **yes — Idempotency-Key required** | verified-buyer gate |
| 50 | GET  | /v1/customer/reviews/me | reviews | 7 | S-7.6 (My reviews) | safe | |
| 51 | GET  | /v1/customer/reviews/me/{id} | reviews | 7 | S-7.7 (My review detail) | safe | |
| 52 | PATCH | /v1/customer/reviews/{id} | reviews | 7 | S-7.7 | yes | edit window |
| 53 | POST | /v1/customer/reviews/{id}/report | reviews | 7 | S-7.8 (Report) | yes | |
| 54 | GET  | /v1/customer/reviews/report-reasons | reviews | 7 | S-7.8 | safe | per-market |
| **Verification (8)** | | | | | | | |
| 55 | GET  | /api/customer/verifications | verification | 7 | S-7.1 (Verification list) | safe | |
| 56 | GET  | /api/customer/verifications/active | verification | 7 | S-7.1 | safe | banner data |
| 57 | GET  | /api/customer/verifications/schema | verification | 7 | S-7.2 (Submit form) | safe | per-market dynamic schema |
| 58 | POST | /api/customer/verifications | verification | 7 | S-7.2 | **yes — Idempotency-Key required** | terminal write |
| 59 | GET  | /api/customer/verifications/{id} | verification | 7 | S-7.3 (Verification detail) | safe | |
| 60 | POST | /api/customer/verifications/{id}/documents | verification | 7 | S-7.3 (Document upload) | yes | multipart |
| 61 | POST | /api/customer/verifications/{id}/resubmit | verification | 7 | S-7.4 (Resubmit) | yes | |
| 62 | POST | /api/customer/verifications/renew | verification | 7 | S-7.4 (Renew) | yes | |
| **B2B (20)** | | | | | | | |
| 63 | POST | /api/customer/companies | b2b | 8 | S-8.7 (Company registration) | **yes** | |
| 64 | GET  | /api/customer/companies/{id} | b2b | 8 | S-8.8 (Company profile) | safe | |
| 65 | PATCH | /api/customer/companies/{id} | b2b | 8 | S-8.8 | yes | |
| 66 | POST | /api/customer/companies/{id}/branches | b2b | 8 | S-8.9 (Branch add) | yes | |
| 67 | DEL  | /api/customer/companies/{id}/branches/{branchId} | b2b | 8 | S-8.9 | yes | |
| 68 | POST | /api/customer/companies/{id}/invitations | b2b | 8 | S-8.10 (Invite user) | yes | |
| 69 | POST | /api/customer/companies/invitations/{token}/accept | b2b | 8 | S-8.11 (Accept invite) | yes | deep-link |
| 70 | POST | /api/customer/companies/invitations/{token}/decline | b2b | 8 | S-8.11 | yes | |
| 71 | PATCH | /api/customer/companies/{id}/memberships/{membershipId} | b2b | 8 | S-8.12 (Memberships) | yes | role change |
| 72 | DEL  | /api/customer/companies/{id}/memberships/{membershipId} | b2b | 8 | S-8.12 | yes | |
| 73 | GET  | /api/customer/quotes | b2b | 8 | S-8.1 (My quotes) | safe | |
| 74 | GET  | /api/customer/quotes/awaiting-my-approval | b2b | 8 | S-8.2 (Awaiting approval) | safe | |
| 75 | POST | /api/customer/quotes/from-cart | b2b | 8 | S-8.3 (Quote from cart) | **yes** | |
| 76 | POST | /api/customer/quotes/from-product | b2b | 8 | S-8.4 (Quote from product) | **yes** | |
| 77 | GET  | /api/customer/quotes/{id} | b2b | 8 | S-8.5 (Quote detail) | safe | |
| 78 | POST | /api/customer/quotes/{id}/submit-acceptance | b2b | 8 | S-8.5 (Quote actions) | yes | |
| 79 | POST | /api/customer/quotes/{id}/finalize-acceptance | b2b | 8 | S-8.5 | yes | |
| 80 | POST | /api/customer/quotes/{id}/reject-acceptance | b2b | 8 | S-8.5 | yes | |
| 81 | POST | /api/customer/quotes/{id}/request-revision | b2b | 8 | S-8.5 | yes | |
| 82 | POST | /api/customer/quotes/{id}/withdraw | b2b | 8 | S-8.5 | yes | |
| 83 | POST | /api/customer/quotes/{id}/save-as-template | b2b | 8 | S-8.5 | yes | |
| 84 | GET  | /api/customer/quotes/{quoteId}/versions/{versionId}/documents/{locale} | b2b | 8 | S-8.6 (Quote document) | safe | binary |

**Out of scope here (do NOT wire to mobile UI):**
- All `/v1/admin/*`, `/api/admin/*`, `/admin/*` operations (admin web app).
- All `/v1/internal/*`, `/internal/*` operations (backend-to-backend).
- `/v1/webhooks/payment-gateway/{providerId}` (provider → backend).
- Identity test endpoints `_test/protected`, `_test/step-up-protected` (test scaffolding only).

---

## 7. Cross-cutting concerns (every spec must honor these)

1. **Bilingual + RTL (Principle 4):** every screen exists in AR and EN. AR copy is editorial-grade, not machine-translated. RTL layout is verified per screen, including icons that flip (back arrow, chevrons) and those that don't (logo).
2. **Market awareness (Principle 5):** currency formatting, VAT/tax presentation, COD eligibility, shipping methods, verification fields, and legal page links all read from `X-Market-Code`. No hardcoded market logic.
3. **Brand palette (Principle 7):** `#1F6F5F` primary, `#2FA084` secondary, `#6FCF97` accent, `#EEEEEE` neutral. Semantic colors added for success/warning/error/info but no drift from the palette without design approval.
4. **Restricted products (Principle 8):** product is visible, price is visible, add-to-cart is gated. Eligibility checked on both add-to-cart and checkout. Use a single reusable widget for the gate (`RestrictionGate`).
5. **Centralized pricing (Principle 10):** UI never computes totals from line items — always reads from `POST /customer/pricing/price-cart` or checkout-session summary. Cart panel and PDP both call the pricing engine for preview.
6. **State machines explicit (Principle 24):** order detail renders **four** sub-states (`orderState`, `paymentState`, `fulfillmentState`, `refundState`) separately; never merged into a single status pill.
7. **Audit + traceability (Principle 25):** correlation-id is always surfaced in errors and copied to telemetry events.
8. **UX state coverage (Principle 27):** every screen must render loading, loaded, empty, restricted, error variants (401/403/409/5xx), and offline as listed in §5.
9. **AI-build standard (Principle 28):** each phase spec is explicit, structured, low-ambiguity, acceptance-criteria-driven. No "support this somehow" language.
10. **ADR-002 — Bloc:** strict unidirectional flow. Sealed event/state classes. No `setState` for screen state. No Riverpod, Provider, or GetX.

---

## 8. Implementation order & status

Phases are intended to be implemented strictly in order, but later phases that don't depend on earlier customer flows (e.g., Phase 7 reviews don't strictly need orders implemented to ship verification screens) can run in parallel where the depends-on graph allows.

| # | Spec folder | Status snapshot (as of 2026-05-19) |
|---|---|---|
| 1 | phase-1-auth-identity | **Partially done** — login, register, OTP, password reset screens exist (`apps/customer_flutter/lib/features/auth/screens/`). Splash, email confirm, sessions list, locale switcher, account security, MFA = planned. Foundation interceptors exist (`core/api/`). |
| 2 | phase-2-catalog | **Done** (data + bloc layer) — 4 gateways (`features/{catalog,pricing,inventory,reviews}/data/`), 6 shared widgets (`features/catalog/widgets/`), 5 catalog blocs (`CatalogHomeBloc`, `CategoriesListBloc`, `BrandsListBloc`, `ProductListBloc`, `ProductDetailV2Bloc`), 3 new screens (`categories_list_screen`, `brands_list_screen`, `product_list_screen`, `product_detail_v2_screen`). 204 tests passing. Router/DI wiring to switch existing routes to the new blocs lands in Phase 1.5 polish — old `ListingBloc` + `ProductDetailBloc` stay until then. |
| 3 | phase-3-search | **Done** — `features/search/` ships gateway + stub, `RecentSearchesStore` (LRU/10/account-namespaced), `SearchBloc` (250 ms debounce + `switchMap`-cancelled autocomplete + facets/sort/pagination), `LookupBloc` (manual + barcode scan + permission flow), `SearchScreen` + `LookupScreen`, `/search` + `/search/lookup` routes, home AppBar search icon. `SEARCH_CLIENT_SHIPPED` flag flips DI from stub to real gateway. Tests: 24 search-suite passing; overall 230 green. |
| 4 | phase-4-cart-checkout | **Partially done** — cart, checkout, drift, order_confirmation screens exist. Pricing preview wire-up, address step, shipping step, payment-method step refinement = verify against code; may be partial. |
| 5 | phase-5-orders | **Partially done** — orders_list + order_detail exist. Reorder, cancel, tracking timeline, payment retry = verify. |
| 6 | phase-6-returns-invoices | **Planned** — no `features/returns/` or `features/invoices/` folders. |
| 7 | phase-7-trust-compliance | **Partially planned** — only `more/screens/verification_cta_screen.dart` exists. No reviews feature folder. |
| 8 | phase-8-b2b | **Planned** — no `features/b2b/` or `features/quotes/` folders. |

> **Verification rule:** before treating any screen as "Done" in a spec, run a diff against `apps/customer_flutter/lib/features/<area>/` — code may have drifted from the spec.

---

## 9. Risks & mitigations

| # | Risk | Mitigation |
|---|---|---|
| 1 | Mixed route prefixes (`/v1`, `/api`, `/customer`, `/v1/public`) increase client complexity. | One gateway repository per alias (`IDN`, `CAT`, …) under `lib/features/<alias>/data/`. Never call raw paths from widgets or Blocs. |
| 2 | Some OpenAPI surfaces are path-only with light schema detail (notably parts of B2B). | Bind DTOs from contract files in `specs/phase-1D/021-quotes-and-b2b/contracts/` and backend slice tests before UI implementation. Each phase spec's `data-model.md` documents the required shape per screen. |
| 3 | No explicit cart OpenAPI surface — cart is client-state until pricing preview / checkout-session creation. | Documented as deliberate in §2. Phase-4 spec defines the local cart model and the exact moments it talks to the backend (`price-cart`, `checkout/sessions`). |
| 4 | State-transition endpoints produce 409 conflicts (cart drift, expired checkout, double-submit). | Standardized 409 dialog widget delivered in Phase 1 foundation; reused by Phases 4–8. |
| 5 | Several screens already exist in code but may have drifted from this spec (auth, cart, checkout, orders). | Phase specs flag Done screens with a **"verify-against-code"** acceptance criterion that requires reading `apps/customer_flutter/lib/features/<area>/` before marking the screen complete. |
| 6 | Verification + B2B use the older `/api/customer/*` prefix while everything else uses `/v1/customer/*`. | Gateway repositories absorb the prefix; UI is unaware. |
| 7 | Idempotency-Key reuse across user intents would let a stale retry resurface a different action. | One key is bound to one user intent (button press) and expires when the intent resolves; see `idempotency_interceptor.dart`. |
| 8 | Multi-language quote documents are returned as binaries with a locale path param. | Phase 8 spec defines a single download/share widget that resolves current locale; AR documents render with correct RTL viewers. |

---

## 10. How to use this index

- **Building a new screen?** Find it in the matrix (§6), open the owning phase spec, and follow the per-screen template (§5).
- **Adding a new endpoint?** Add the row to §6, decide its owning phase, append the screen entry to that spec's `spec.md`. Update the registry count in §2.
- **Running `/speckit-implement`?** Target a single phase folder (`specs/mobile/phase-N-*/`). All needed context lives inside.
- **Reviewing a PR that touches `apps/customer_flutter/`?** Cross-check the affected screen against its entry in the owning phase spec and verify the acceptance-criteria checkboxes.

---

## 11. Maintenance

- This file is regenerated whenever the OpenAPI surface changes. Run `scripts/extract-customer-endpoints.sh` (TBD — currently the matrix is hand-maintained from the openapi.*.json files).
- When a phase ships, flip its status row in §8 from "Partially done" / "Planned" to "Done" and update each screen entry's Status badge inside that phase's `spec.md`.
- The 8-phase structure is locked for the customer mobile shell. Adding new domains (e.g., loyalty, subscriptions) should either extend an existing phase or be proposed as a Phase 9 amendment per Principle 32.
