# Mobile App Screen to API Implementation Plan

Status: draft implementation plan
Owner: product + mobile + backend team
Scope: customer mobile app + admin mode in same app shell
Last updated: 2026-05-19

## 1) Purpose

This document maps each mobile screen to the exact API endpoints it needs, where those APIs are defined, and what each API requires.

It is organized in phases so implementation can run in a safe order with clear dependencies.

## 2) Source Of Truth

Primary source files:
- [openapi.identity.json](../services/backend_api/openapi.identity.json)
- [openapi.catalog.json](../services/backend_api/openapi.catalog.json)
- [openapi.search.json](../services/backend_api/openapi.search.json)
- [openapi.checkout.json](../services/backend_api/openapi.checkout.json)
- [openapi.orders.json](../services/backend_api/openapi.orders.json)
- [openapi.invoices.json](../services/backend_api/openapi.invoices.json)
- [openapi.returns.json](../services/backend_api/openapi.returns.json)
- [openapi.reviews.json](../services/backend_api/openapi.reviews.json)
- [openapi.verification.json](../services/backend_api/openapi.verification.json)
- [openapi.b2b.json](../services/backend_api/openapi.b2b.json)
- [openapi.pricing.json](../services/backend_api/openapi.pricing.json)
- [openapi.inventory.json](../services/backend_api/openapi.inventory.json)

Important note:
- [openapi.json](../services/backend_api/openapi.json) and [packages/shared_contracts/openapi.json](../packages/shared_contracts/openapi.json) are currently empty path containers and should not be used as a source for implementation.

## 3) Domain Alias Index (where to find API + code)

| Alias | Domain | OpenAPI | Backend module | Contract reference |
|---|---|---|---|---|
| IDN | Identity and access | [openapi.identity.json](../services/backend_api/openapi.identity.json) | `services/backend_api/Modules/Identity` | [identity-and-access-contract.md](../specs/phase-1B/004-identity-and-access/contracts/identity-and-access-contract.md) |
| CAT | Catalog | [openapi.catalog.json](../services/backend_api/openapi.catalog.json) | `services/backend_api/Modules/Catalog` | [catalog-contract.md](../specs/phase-1B/005-catalog/contracts/catalog-contract.md) |
| SRCH | Search | [openapi.search.json](../services/backend_api/openapi.search.json) | `services/backend_api/Modules/Search` | [search-contract.md](../specs/phase-1B/006-search/contracts/search-contract.md) |
| CHK | Checkout | [openapi.checkout.json](../services/backend_api/openapi.checkout.json) | `services/backend_api/Modules/Checkout` | [checkout-contract.md](../specs/phase-1B/010-checkout/contracts/checkout-contract.md) |
| ORD | Orders and quotations | [openapi.orders.json](../services/backend_api/openapi.orders.json) | `services/backend_api/Modules/Orders` | [orders-contract.md](../specs/phase-1B/011-orders/contracts/orders-contract.md) |
| INV | Invoices | [openapi.invoices.json](../services/backend_api/openapi.invoices.json) | `services/backend_api/Modules/TaxInvoices` | [tax-invoices-contract.md](../specs/phase-1B/012-tax-invoices/contracts/tax-invoices-contract.md) |
| RET | Returns and refunds | [openapi.returns.json](../services/backend_api/openapi.returns.json) | `services/backend_api/Modules/Returns` | [returns-contract.md](../specs/phase-1B/013-returns/contracts/returns-contract.md) |
| REV | Reviews and moderation | [openapi.reviews.json](../services/backend_api/openapi.reviews.json) | `services/backend_api/Modules/Reviews` | [reviews-and-moderation-contract.md](../specs/phase-1D/022-reviews-moderation/contracts/reviews-and-moderation-contract.md) |
| VER | Verification | [openapi.verification.json](../services/backend_api/openapi.verification.json) | `services/backend_api/Modules/Verification` | [verification-contract.md](../specs/phase-1D/020-verification/contracts/verification-contract.md) |
| B2B | Quotes and companies | [openapi.b2b.json](../services/backend_api/openapi.b2b.json) | `services/backend_api/Modules/B2B` | [quotes-and-b2b-contract.md](../specs/phase-1D/021-quotes-and-b2b/contracts/quotes-and-b2b-contract.md) |
| PRC | Pricing and promotions | [openapi.pricing.json](../services/backend_api/openapi.pricing.json) | `services/backend_api/Modules/Pricing` | [pricing-contract.md](../specs/phase-1B/007-a-pricing-and-tax-engine/contracts/pricing-contract.md), [promotions-ux-and-campaigns-contract.md](../specs/phase-1D/007-b-promotions-ux-and-campaigns/contracts/promotions-ux-and-campaigns-contract.md) |
| STK | Inventory stock and reservations | [openapi.inventory.json](../services/backend_api/openapi.inventory.json) | `services/backend_api/Modules/Inventory` | [inventory-contract.md](../specs/phase-1B/008-inventory/contracts/inventory-contract.md) |

## 4) Global API Requirements (applies to all phases)

Request requirements:
- `Authorization: Bearer <access_token>` for all protected customer/admin endpoints.
- `Accept-Language` header is required by client interceptor.
- `X-Market-Code` header is required by client interceptor.
- `X-Correlation-Id` header should be sent on every request.
- `Idempotency-Key` is required for checkout submit endpoint.

Current app references:
- [api_module.dart](../apps/customer_flutter/lib/core/api/api_module.dart)
- [locale_market_interceptor.dart](../apps/customer_flutter/lib/core/api/locale_market_interceptor.dart)
- [correlation_id_interceptor.dart](../apps/customer_flutter/lib/core/api/correlation_id_interceptor.dart)
- [idempotency_interceptor.dart](../apps/customer_flutter/lib/core/api/idempotency_interceptor.dart)

Security and behavior rules:
- Do not send bearer tokens over non-https outside local development.
- Keep retries idempotent only for safe methods or explicit idempotency keys.
- Use role based routing after `me` endpoint resolves permissions/profile.

## 5) Phase Summary

| Phase | Goal | Main personas | Exit criteria |
|---|---|---|---|
| 0 | Contract baseline and SDK generation | Mobile + backend | Generated clients compile, endpoint inventory signed off |
| 1 | Auth and session foundation | Customer + admin | Login/register/otp/reset/session management complete |
| 2 | Customer discovery experience | Customer | Home/search/listing/product detail complete |
| 3 | Cart and checkout flow | Customer | Checkout from cart to confirmation complete |
| 4 | Post purchase journey | Customer + operations | Orders, invoices, returns complete |
| 5 | Trust and compliance surfaces | Customer + moderation | Reviews and verification complete |
| 6 | B2B customer journey | Customer B2B | Quote and company workflows complete |
| 7 | Admin foundation and identity ops | Admin | Admin shell, auth, role/session/invite management complete |
| 8 | Admin commerce operations | Admin ops | Orders/returns/invoices/reviews/verification/search queues complete |
| 9 | Admin commercial controls | Merch and finance | Catalog/pricing/inventory/b2b admin controls complete |
| 10 | Internal adapters and webhook handling | Backend integration | Internal/webhook endpoints connected through backend adapters |

## 6) Detailed Screen To API Plan

### Phase 0 - Contract baseline and SDK generation

Implementation tasks:
1. Generate customer and admin API clients from all non-empty OpenAPI files.
2. Keep one client namespace per alias (`IDN`, `CAT`, `SRCH`, `CHK`, `ORD`, `INV`, `RET`, `REV`, `VER`, `B2B`, `PRC`, `STK`).
3. Add CI check that fails if generated client is stale.
4. Open contract gaps ticket for endpoints with surface-only shapes (notably parts of `B2B`).

Outputs:
- `lib/generated/api/<domain>/...`
- Request/response mappers in repository layer.

### Phase 1 - Auth and session foundation

| Screen | Needed API | What API needs | Where to find API |
|---|---|---|---|
| Splash/session bootstrap | `POST /v1/customer/identity/session/refresh`, `GET /v1/customer/identity/me` | `RefreshSessionRequest` body for refresh; bearer for `me` | `IDN` |
| Login | `POST /v1/customer/identity/sign-in` | `CustomerSignInRequest` body | `IDN` |
| Register | `POST /v1/customer/identity/register` | `RegisterRequest` body | `IDN` |
| OTP verify | `POST /v1/customer/identity/otp/request`, `POST /v1/customer/identity/otp/verify` | `RequestOtpRequest`, `VerifyOtpRequest` bodies | `IDN` |
| Password reset request | `POST /v1/customer/identity/password/reset-request` | `RequestPasswordResetRequest` body | `IDN` |
| Password reset confirm | `POST /v1/customer/identity/password/reset-complete` | `CompletePasswordResetRequest` body | `IDN` |
| Email confirmation deep link | `POST /v1/customer/identity/email/confirm` | `ConfirmEmailRequest` body | `IDN` |
| Account security | `POST /v1/customer/identity/password/change`, `POST /v1/customer/identity/sign-out` | `ChangePasswordRequest`, `SignOutRequest` bodies | `IDN` |
| Device/session management | `GET /v1/customer/identity/sessions`, `DELETE /v1/customer/identity/sessions/{sessionId}` | `sessionId` path param for delete | `IDN` |
| Locale settings | `PATCH /v1/customer/identity/locale` | `SetLocaleRequest` body | `IDN` |

### Phase 2 - Customer discovery experience

| Screen | Needed API | What API needs | Where to find API |
|---|---|---|---|
| Home | `GET /v1/customer/catalog/categories`, `GET /v1/customer/catalog/brands` | optional `market` query | `CAT` |
| Search landing | `POST /v1/customer/search/autocomplete` | `AutocompleteRequest` body | `SRCH` |
| Search result list | `POST /v1/customer/search/products`, `POST /v1/customer/search/lookup` | `SearchProductsRequest`, `LookupRequest` bodies | `SRCH` |
| Category listing | `GET /v1/customer/catalog/categories/{slug}/products` | required `slug` path, optional filters: `market,page,pageSize,sort,brand,priceMin,priceMax,restricted` | `CAT` |
| Product detail | `GET /v1/customer/catalog/products/{slug}` | required `slug`, optional `market` | `CAT` |
| Product rating summary | `GET /v1/public/reviews/aggregates/{product_id}` | required `product_id`, optional `market_code` | `REV` |
| Product rating batch preload | `GET /v1/public/reviews/aggregates` | required `product_ids`, optional `market_code` | `REV` |
| Stock badge and availability | `GET /v1/customer/inventory/availability` | required `productIds` and `market` query | `STK` |

### Phase 3 - Cart and checkout flow

| Screen | Needed API | What API needs | Where to find API |
|---|---|---|---|
| Cart pricing panel | `POST /customer/pricing/price-cart` | `PriceCartRequest` body | `PRC` |
| Checkout start | `POST /v1/customer/checkout/sessions` | request body object | `CHK` |
| Checkout summary | `GET /v1/customer/checkout/sessions/{sessionId}/summary` | required `sessionId` path | `CHK` |
| Checkout shipping quotes | `GET /v1/customer/checkout/sessions/{sessionId}/shipping-quotes` | required `sessionId` path | `CHK` |
| Checkout address step | `PATCH /v1/customer/checkout/sessions/{sessionId}/address` | required `sessionId` path + body | `CHK` |
| Checkout shipping step | `PATCH /v1/customer/checkout/sessions/{sessionId}/shipping` | required `sessionId` path + body | `CHK` |
| Checkout payment step | `PATCH /v1/customer/checkout/sessions/{sessionId}/payment-method` | required `sessionId` path + body | `CHK` |
| Checkout submit | `POST /v1/customer/checkout/sessions/{sessionId}/submit` | required `sessionId`; required `Idempotency-Key` header | `CHK` |
| Checkout drift handling | `POST /v1/customer/checkout/sessions/{sessionId}/accept-drift` | required `sessionId` path | `CHK` |
| Checkout confirmation | depends on submit response + order id navigation | no extra call required if submit returns full outcome | `CHK` + `ORD` |

### Phase 4 - Post purchase journey

| Screen | Needed API | What API needs | Where to find API |
|---|---|---|---|
| Orders list | `GET /v1/customer/orders` | optional filters: `status,market,from,to,page,pageSize` | `ORD` |
| Order detail | `GET /v1/customer/orders/{id}` | required `id` path | `ORD` |
| Cancel order | `POST /v1/customer/orders/{id}/cancel` | required `id` path + body | `ORD` |
| Reorder | `POST /v1/customer/orders/{id}/reorder` | required `id` path | `ORD` |
| Return eligibility | `GET /v1/customer/orders/{id}/return-eligibility` | required `id` path | `ORD` |
| Return create wizard | `POST /v1/customer/returns/photos`, `POST /v1/customer/orders/{orderId}/returns` | upload body for photos, then create return body with `orderId` path | `RET` |
| Returns list | `GET /v1/customer/returns` | optional `status,page,pageSize` | `RET` |
| Return detail | `GET /v1/customer/returns/{id}` | required `id` path | `RET` |
| Invoice preview | `GET /v1/customer/orders/{orderId}/invoice` | required `orderId` path | `INV` |
| Invoice PDF download | `GET /v1/customer/orders/{orderId}/invoice.pdf` | required `orderId` path | `INV` |
| Legacy quotation list | `GET /v1/customer/quotations` | optional `status` query | `ORD` |
| Legacy quotation detail/actions | `GET /v1/customer/quotations/{id}`, `POST /v1/customer/quotations/{id}/accept`, `POST /v1/customer/quotations/{id}/reject` | required `id` path for all | `ORD` |

### Phase 5 - Trust and compliance

| Screen | Needed API | What API needs | Where to find API |
|---|---|---|---|
| My reviews list | `GET /v1/customer/reviews/me` | optional `page,page_size` query | `REV` |
| My review detail | `GET /v1/customer/reviews/me/{id}` | required `id` path | `REV` |
| Submit review | `POST /v1/customer/reviews` | review request body | `REV` |
| Edit review | `PATCH /v1/customer/reviews/{id}` | required `id` path + patch body | `REV` |
| Report review | `GET /v1/customer/reviews/report-reasons`, `POST /v1/customer/reviews/{id}/report` | reasons has no params; report requires `id` path + body | `REV` |
| Verification dashboard | `GET /api/customer/verifications`, `GET /api/customer/verifications/active`, `GET /api/customer/verifications/schema` | pagination/filter from API defaults; schema call is required for dynamic form | `VER` |
| Verification detail | `GET /api/customer/verifications/{id}` | required `id` path | `VER` |
| Submit verification | `POST /api/customer/verifications` | `SubmitVerificationRequest` body | `VER` |
| Upload verification document | `POST /api/customer/verifications/{id}/documents` | required `id` path + `AttachDocumentRequest` body | `VER` |
| Resubmit verification | `POST /api/customer/verifications/{id}/resubmit` | required `id` + `ResubmitWithInfoRequest` body | `VER` |
| Renew verification | `POST /api/customer/verifications/renew` | `RequestRenewalRequest` body | `VER` |

### Phase 6 - B2B customer journey

| Screen | Needed API | What API needs | Where to find API |
|---|---|---|---|
| Quote request from cart | `POST /api/customer/quotes/from-cart` | body required by contract; treat as idempotent user action | `B2B` |
| Quote request from product | `POST /api/customer/quotes/from-product` | body required by contract | `B2B` |
| My quotes list | `GET /api/customer/quotes` | query/filter per contract | `B2B` |
| Awaiting my approval list | `GET /api/customer/quotes/awaiting-my-approval` | no required params | `B2B` |
| Quote detail | `GET /api/customer/quotes/{id}` | required `id` path | `B2B` |
| Quote actions | `POST /api/customer/quotes/{id}/withdraw`, `POST /api/customer/quotes/{id}/request-revision`, `POST /api/customer/quotes/{id}/submit-acceptance`, `POST /api/customer/quotes/{id}/finalize-acceptance`, `POST /api/customer/quotes/{id}/reject-acceptance`, `POST /api/customer/quotes/{id}/save-as-template` | required `id` path for each action | `B2B` |
| Quote document download | `GET /api/customer/quotes/{quoteId}/versions/{versionId}/documents/{locale}` | required `quoteId`, `versionId`, `locale` path params | `B2B` |
| Company registration | `POST /api/customer/companies` | company create body | `B2B` |
| Company profile | `GET /api/customer/companies/{id}`, `PATCH /api/customer/companies/{id}` | required `id` path | `B2B` |
| Company branches | `POST /api/customer/companies/{id}/branches`, `DELETE /api/customer/companies/{id}/branches/{branchId}` | required `id`, `branchId` | `B2B` |
| Company invitations | `POST /api/customer/companies/{id}/invitations`, `POST /api/customer/companies/invitations/{token}/accept`, `POST /api/customer/companies/invitations/{token}/decline` | required `id` or `token` path | `B2B` |
| Company memberships | `PATCH /api/customer/companies/{id}/memberships/{membershipId}`, `DELETE /api/customer/companies/{id}/memberships/{membershipId}` | required `id`, `membershipId` path | `B2B` |

### Phase 7 - Admin foundation and identity

| Screen | Needed API | What API needs | Where to find API |
|---|---|---|---|
| Admin login | `POST /v1/admin/identity/sign-in` | `AdminSignInRequest` body | `IDN` |
| Admin profile | `GET /v1/admin/identity/me` | bearer token | `IDN` |
| Auth guard debug screen (non-production) | `GET /v1/admin/identity/_test/protected`, `GET /v1/admin/identity/_test/step-up-protected`, `GET /v1/customer/identity/_test/protected` | bearer token; use only for test/staging validation | `IDN` |
| Admin mfa challenge | `POST /v1/admin/identity/mfa/challenge` | `CompleteMfaChallengeRequest` body | `IDN` |
| Admin step-up | `POST /v1/admin/identity/mfa/step-up`, `POST /v1/admin/identity/mfa/step-up/confirm` | otp request and confirm bodies | `IDN` |
| Admin totp setup/rotate | `POST /v1/admin/identity/mfa/totp/enroll`, `POST /v1/admin/identity/mfa/totp/confirm`, `POST /v1/admin/identity/mfa/totp/rotate` | respective TOTP bodies | `IDN` |
| Admin invitation management | `POST /v1/admin/identity/invitations`, `DELETE /v1/admin/identity/invitations/{invitationId}`, `POST /v1/admin/identity/invitation/accept` | invite or accept body, invitationId path for delete | `IDN` |
| Admin account sessions | `GET /v1/admin/identity/accounts/{accountId}/sessions`, `DELETE /v1/admin/identity/accounts/{accountId}/sessions/{sessionId}` | required `accountId`, `sessionId` path | `IDN` |
| Admin account mfa and role | `GET /v1/admin/identity/accounts/{accountId}/mfa/factors`, `POST /v1/admin/identity/accounts/mfa/reset`, `PATCH /v1/admin/identity/accounts/{accountId}/role` | account ids and request bodies | `IDN` |

Notes:
- `_test` endpoints are not for production UI; keep behind debug-only tooling.

### Phase 8 - Admin commerce operations

| Screen | Needed API | What API needs | Where to find API |
|---|---|---|---|
| Orders queue | `GET /v1/admin/orders`, `GET /v1/admin/orders/export` | optional export filters: `market,from,to,format` | `ORD` |
| Order detail and audit | `GET /v1/admin/orders/{id}`, `GET /v1/admin/orders/{id}/audit` | required `id` path | `ORD` |
| Fulfillment actions | `POST /v1/admin/orders/{id}/fulfillment/start-picking`, `POST /v1/admin/orders/{id}/fulfillment/mark-packed`, `POST /v1/admin/orders/{id}/fulfillment/mark-handed-to-carrier`, `POST /v1/admin/orders/{id}/fulfillment/create-shipment`, `POST /v1/admin/orders/{id}/fulfillment/mark-delivered` | required `id` path, shipment create body | `ORD` |
| Payment operations | `POST /v1/admin/orders/{id}/payments/confirm-bank-transfer`, `POST /v1/admin/orders/{id}/payments/force-state` | required `id` path + body | `ORD` |
| Admin quotation panel | `POST /v1/admin/quotations`, `POST /v1/admin/quotations/{id}/send`, `POST /v1/admin/quotations/{id}/expire`, `POST /v1/admin/quotations/{id}/convert` | required `id` for actions | `ORD` |
| Checkout sessions monitor | `GET /v1/admin/checkout/sessions`, `POST /v1/admin/checkout/sessions/{sessionId}/expire` | filters for list; required `sessionId` path for expire | `CHK` |
| Returns queue | `GET /v1/admin/returns`, `GET /v1/admin/returns/{id}`, `GET /v1/admin/returns/export` | list/export filters for market/date/state | `RET` |
| Return decisions | `POST /v1/admin/returns/{id}/approve`, `POST /v1/admin/returns/{id}/approve-partial`, `POST /v1/admin/returns/{id}/reject`, `POST /v1/admin/returns/{id}/inspect`, `POST /v1/admin/returns/{id}/mark-received`, `POST /v1/admin/returns/{id}/issue-refund`, `POST /v1/admin/returns/{id}/force-refund` | required `id` path; several actions require body | `RET` |
| Refund operations | `POST /v1/admin/refunds/{refundId}/retry`, `POST /v1/admin/refunds/{refundId}/confirm-bank-transfer` | required `refundId` path; confirm transfer body | `RET` |
| Return policy editor | `GET /v1/admin/return-policies`, `PUT /v1/admin/return-policies/{market}` | required `market` path + policy body | `RET` |
| Invoice center | `GET /v1/admin/invoices/`, `GET /v1/admin/invoices/{id}`, `GET /v1/admin/invoices/by-number/{invoiceNumber}`, `GET /v1/admin/invoices/{id}/pdf` | path/query filters as listed in OpenAPI | `INV` |
| Invoice jobs and actions | `GET /v1/admin/invoices/render-queue`, `POST /v1/admin/invoices/render-queue/{jobId}/retry`, `POST /v1/admin/invoices/{id}/regenerate`, `POST /v1/admin/invoices/{id}/resend`, `GET /v1/admin/invoices/export` | required `jobId` or `id` path; action bodies where required | `INV` |
| Reviews moderation queue | `GET /v1/admin/reviews/queue`, `GET /v1/admin/reviews/{id}`, `GET /v1/admin/reviews/by-customer/{customer_id}` | queue filters: `state,market_code,triggered_by,community_report_count_min,media_only` | `REV` |
| Reviews moderation actions | `POST /v1/admin/reviews/{id}/decide`, `GET /v1/admin/reviews/{id}/notes`, `POST /v1/admin/reviews/{id}/notes`, `DELETE /v1/admin/reviews/{id}` | required `id` path; decide and notes bodies | `REV` |
| Reviews policy | `GET /v1/admin/reviews/policy/wordlists`, `PUT /v1/admin/reviews/policy/wordlists`, `DELETE /v1/admin/reviews/policy/wordlists`, `PATCH /v1/admin/reviews/policy/markets/{market_code}` | required `market_code` path for policy patch | `REV` |
| Verification queue | `GET /api/admin/verifications`, `GET /api/admin/verifications/{id}`, `GET /api/admin/verifications/{id}/documents/{documentId}/open` | queue filters: `state,page,page_size`; required ids for detail/open | `VER` |
| Verification decisions | `POST /api/admin/verifications/{id}/approve`, `POST /api/admin/verifications/{id}/reject`, `POST /api/admin/verifications/{id}/request-info`, `POST /api/admin/verifications/{id}/revoke` | required `id` path + `ReviewerDecisionRequest` body | `VER` |
| Search operations | `GET /v1/admin/search/health`, `GET /v1/admin/search/jobs`, `POST /v1/admin/search/reindex`, `GET /v1/admin/search/reindex/{jobId}/stream` | list filters; reindex uses `index` query; stream requires `jobId` | `SRCH` |

### Phase 9 - Admin commercial controls

| Screen | Needed API | What API needs | Where to find API |
|---|---|---|---|
| Catalog products | `GET /v1/admin/catalog/products`, `GET /v1/admin/catalog/products/{id}`, `POST /v1/admin/catalog/products`, `PATCH /v1/admin/catalog/products/{id}` | required `id` for detail/update; create/update bodies | `CAT` |
| Product workflow | `POST /v1/admin/catalog/products/{id}/submit-for-review`, `POST /v1/admin/catalog/products/{id}/publish`, `POST /v1/admin/catalog/products/{id}/archive`, `POST /v1/admin/catalog/products/{id}/cancel-schedule` | required `id` path | `CAT` |
| Product media and docs | `POST /v1/admin/catalog/products/{id}/media`, `PATCH /v1/admin/catalog/products/{id}/media/{mediaId}`, `DELETE /v1/admin/catalog/products/{id}/media/{mediaId}`, `POST /v1/admin/catalog/products/{id}/documents` | required `id` and `mediaId` path | `CAT` |
| Catalog categories | `POST /v1/admin/catalog/categories`, `PATCH /v1/admin/catalog/categories/{id}`, `DELETE /v1/admin/catalog/categories/{id}`, `POST /v1/admin/catalog/categories/{id}/reparent` | required `id` path; category bodies | `CAT` |
| Catalog brands/manufacturers | `POST /v1/admin/catalog/brands`, `PATCH /v1/admin/catalog/brands/{id}`, `POST /v1/admin/catalog/manufacturers`, `PATCH /v1/admin/catalog/manufacturers/{id}` | required `id` path for patch | `CAT` |
| Catalog bulk import | `POST /v1/admin/catalog/products/bulk-import` | file/body payload | `CAT` |
| Pricing promotions | `GET /admin/pricing/promotions`, `POST /admin/pricing/promotions`, `PUT /admin/pricing/promotions/{id}`, `DELETE /admin/pricing/promotions/{id}`, `POST /admin/pricing/promotions/{id}/activate`, `POST /admin/pricing/promotions/{id}/deactivate` | required `id` path for update/actions | `PRC` |
| Pricing coupons | `GET /admin/pricing/coupons`, `POST /admin/pricing/coupons`, `PUT /admin/pricing/coupons/{id}`, `POST /admin/pricing/coupons/{id}/deactivate`, `GET /admin/pricing/coupons/{id}/redemptions` | required `id` path for actions | `PRC` |
| Pricing tax rates | `GET /admin/pricing/tax-rates`, `POST /admin/pricing/tax-rates`, `PATCH /admin/pricing/tax-rates/{id}` | required `id` for patch | `PRC` |
| B2B pricing tiers | `GET /admin/pricing/b2b-tiers`, `POST /admin/pricing/b2b-tiers`, `PUT /admin/pricing/b2b-tiers/{id}`, `DELETE /admin/pricing/b2b-tiers/{id}` | required `id` for update/delete | `PRC` |
| Product tier pricing | `POST /admin/pricing/products/{productId}/tier-prices`, `DELETE /admin/pricing/products/{productId}/tier-prices` | required `productId` path | `PRC` |
| Account tier assignment | `POST /admin/pricing/accounts/{accountId}/tier`, `GET /admin/pricing/explanations/{ownerKind}/{ownerId}` | required `accountId`, `ownerKind`, `ownerId` path | `PRC` |
| Inventory batches | `GET /v1/admin/inventory/batches`, `GET /v1/admin/inventory/batches/{id}`, `POST /v1/admin/inventory/batches`, `PATCH /v1/admin/inventory/batches/{id}` | required `id` path for detail/update | `STK` |
| Inventory movements | `POST /v1/admin/inventory/movements/adjust`, `POST /v1/admin/inventory/movements/transfer`, `POST /v1/admin/inventory/movements/writeoff` | movement bodies | `STK` |
| Admin quote queue | `GET /api/admin/quotes`, `GET /api/admin/quotes/{id}`, `POST /api/admin/quotes/{id}/draft`, `POST /api/admin/quotes/{id}/publish` | required `id` path for detail/actions | `B2B` |
| Admin company control | `POST /api/admin/companies/{id}/suspend` | required `id` path | `B2B` |

### Phase 10 - Internal adapters and webhook handling

These endpoints should not be called directly from mobile UI. They must be integrated through backend workflows, workers, or gateway services.

| Endpoint set | Intended consumer | Where to implement |
|---|---|---|
| `POST /v1/webhooks/payment-gateway/{providerId}` | payment provider callback receiver | `services/backend_api/Modules/Checkout` and `services/backend_api/Modules/Payments` |
| `POST /v1/internal/catalog/restrictions/check` | server-side compliance check | `services/backend_api/Modules/Catalog` |
| `POST /internal/pricing/calculate` | server-side pricing orchestration | `services/backend_api/Modules/Pricing` |
| `POST /v1/internal/inventory/reservations`, `POST /v1/internal/inventory/reservations/{id}/convert`, `DELETE /v1/internal/inventory/reservations/{id}`, `POST /v1/internal/inventory/movements/return` | checkout/orders/returns orchestration | `services/backend_api/Modules/Inventory` |
| `POST /v1/internal/orders/{id}/advance-refund-state` | refund state machine bridge | `services/backend_api/Modules/Orders` |
| `POST /v1/internal/invoices/issue-on-capture`, `POST /v1/internal/credit-notes/issue` | payment/refund accounting bridge | `services/backend_api/Modules/TaxInvoices` |

## 6.1) Global Screen Flows

### Customer global flow (end-to-end)

```text
[Splash]
  -> refresh session -> me
  -> if unauthenticated: [Login/Register/OTP]
  -> if authenticated: [Home]

[Home] -> [Search] -> [Results] -> [Product Detail]
  -> availability + ratings -> [Cart]
  -> price-cart -> [Checkout Start]

[Checkout Address] -> [Shipping] -> [Payment] -> [Submit]
  -> if drift/conflict: accept-drift -> refresh summary
  -> [Confirmation]

[Orders List] -> [Order Detail]
  -> cancel/reorder/return eligibility
  -> [Return Wizard] -> [Returns List/Detail]
  -> [Invoice Preview/PDF]

[My Reviews] + [Verification Dashboard]
  -> submit/edit/report review
  -> submit/upload/resubmit/renew verification

[B2B Quotes/Company]
  -> quote from cart/product
  -> quote actions + document download
  -> company profile/branches/invitations/memberships
```

### Admin global flow (end-to-end)

```text
[Admin Login] -> [Admin MFA/Step-up/TOTP] -> [Admin Profile]
  -> [Orders Queue/Detail] -> fulfillment/payment operations
  -> [Returns Queue] -> decisions + refunds + policy
  -> [Invoice Center] -> jobs + regenerate/resend/export
  -> [Reviews Queue] -> decide/notes/policy
  -> [Verification Queue] -> approve/reject/request-info/revoke
  -> [Search Ops] -> health/jobs/reindex/stream

[Catalog Control]
  -> products/workflow/media/categories/brands/manufacturers/import

[Pricing Control]
  -> promotions/coupons/tax-rates/b2b-tiers/tier-pricing/assignments

[Inventory Control]
  -> batches + movements

[B2B Admin]
  -> quotes queue draft/publish
  -> company suspend
```

### Internal and webhook global flow (non-UI)

```text
[Checkout Submit]
  -> internal inventory reservation create/convert/delete
  -> payment webhook callback receiver
  -> internal pricing calculate
  -> internal invoice issue on capture

[Returns/Refund path]
  -> internal inventory return movement
  -> internal order advance-refund-state
  -> internal credit-note issue

[Catalog publish path]
  -> internal catalog restrictions check
```

## 6.2) Screen Blueprints And Single-Screen Flows

Legend:
- ASCII: visual component structure.
- Flow: API call order for one screen.

### Phase 1 - Auth and session foundation

1. Splash/session bootstrap
```text
+--------------------------------+
| Logo / app status              |
| Loading spinner                |
| Session state message          |
+--------------------------------+
```
Flow: `POST /v1/customer/identity/session/refresh` -> `GET /v1/customer/identity/me` -> route to auth/home.

2. Login
```text
+--------------------------------+
| Email/phone input              |
| Password input                 |
| Sign in CTA                    |
| Forgot password link           |
+--------------------------------+
```
Flow: `POST /v1/customer/identity/sign-in` -> success token/session -> optionally `GET /v1/customer/identity/me`.

3. Register
```text
+--------------------------------+
| Name/email/phone/password      |
| Terms checkbox                 |
| Create account CTA             |
+--------------------------------+
```
Flow: `POST /v1/customer/identity/register` -> navigate to OTP/email confirm.

4. OTP verify
```text
+--------------------------------+
| Destination hint               |
| OTP code fields                |
| Resend code CTA                |
| Verify CTA                     |
+--------------------------------+
```
Flow: `POST /v1/customer/identity/otp/request` -> `POST /v1/customer/identity/otp/verify`.

5. Password reset request
```text
+--------------------------------+
| Email/phone input              |
| Request reset CTA              |
+--------------------------------+
```
Flow: `POST /v1/customer/identity/password/reset-request`.

6. Password reset confirm
```text
+--------------------------------+
| Reset token/deep-link state    |
| New password + confirm         |
| Submit CTA                     |
+--------------------------------+
```
Flow: `POST /v1/customer/identity/password/reset-complete`.

7. Email confirmation deep link
```text
+--------------------------------+
| Email verification status       |
| Continue CTA                    |
+--------------------------------+
```
Flow: `POST /v1/customer/identity/email/confirm`.

8. Account security
```text
+--------------------------------+
| Change password section        |
| Active session summary         |
| Sign out CTA                   |
+--------------------------------+
```
Flow: `POST /v1/customer/identity/password/change` and `POST /v1/customer/identity/sign-out`.

9. Device/session management
```text
+--------------------------------+
| Session list (device/time)     |
| Revoke session action per row  |
+--------------------------------+
```
Flow: `GET /v1/customer/identity/sessions` -> `DELETE /v1/customer/identity/sessions/{sessionId}`.

10. Locale settings
```text
+--------------------------------+
| Language picker                |
| Market picker                  |
| Save CTA                       |
+--------------------------------+
```
Flow: `PATCH /v1/customer/identity/locale`.

### Phase 2 - Customer discovery experience

1. Home
```text
+--------------------------------+
| Search bar                     |
| Category carousel              |
| Brand strip                    |
+--------------------------------+
```
Flow: `GET /v1/customer/catalog/categories` + `GET /v1/customer/catalog/brands`.

2. Search landing
```text
+--------------------------------+
| Search input                   |
| Suggestion list                |
+--------------------------------+
```
Flow: `POST /v1/customer/search/autocomplete`.

3. Search result list
```text
+--------------------------------+
| Query + filter chips           |
| Product cards list             |
| Pagination/load-more           |
+--------------------------------+
```
Flow: `POST /v1/customer/search/products` -> optional barcode/SKU `POST /v1/customer/search/lookup`.

4. Category listing
```text
+--------------------------------+
| Category title + filters       |
| Sort dropdown                  |
| Product grid                   |
+--------------------------------+
```
Flow: `GET /v1/customer/catalog/categories/{slug}/products`.

5. Product detail
```text
+--------------------------------+
| Image gallery                  |
| Name/price/stock badge         |
| Description/specs              |
| Add to cart CTA                |
+--------------------------------+
```
Flow: `GET /v1/customer/catalog/products/{slug}` -> `GET /v1/customer/inventory/availability` -> `GET /v1/public/reviews/aggregates/{product_id}`.

6. Product rating summary/batch preload
```text
+--------------------------------+
| Rating stars summary           |
| Review count                   |
+--------------------------------+
```
Flow: list preload `GET /v1/public/reviews/aggregates` and detail `GET /v1/public/reviews/aggregates/{product_id}`.

7. Stock badge and availability
```text
+--------------------------------+
| Availability pill              |
| Delivery estimate text         |
+--------------------------------+
```
Flow: `GET /v1/customer/inventory/availability`.

### Phase 3 - Cart and checkout flow

1. Cart pricing panel
```text
+--------------------------------+
| Cart lines                     |
| Subtotal/tax/discount          |
| Total                          |
+--------------------------------+
```
Flow: `POST /customer/pricing/price-cart`.

2. Checkout start
```text
+--------------------------------+
| Checkout entry summary         |
| Start checkout CTA             |
+--------------------------------+
```
Flow: `POST /v1/customer/checkout/sessions`.

3. Checkout summary
```text
+--------------------------------+
| Session items                  |
| Address/shipping/payment state |
| Totals                         |
+--------------------------------+
```
Flow: `GET /v1/customer/checkout/sessions/{sessionId}/summary`.

4. Checkout shipping quotes
```text
+--------------------------------+
| Shipping methods list          |
| ETA and cost per option        |
+--------------------------------+
```
Flow: `GET /v1/customer/checkout/sessions/{sessionId}/shipping-quotes`.

5. Checkout address step
```text
+--------------------------------+
| Address form                   |
| Save and continue CTA          |
+--------------------------------+
```
Flow: `PATCH /v1/customer/checkout/sessions/{sessionId}/address` -> refresh summary.

6. Checkout shipping step
```text
+--------------------------------+
| Method selector                |
| Continue CTA                   |
+--------------------------------+
```
Flow: `PATCH /v1/customer/checkout/sessions/{sessionId}/shipping` -> refresh summary.

7. Checkout payment step
```text
+--------------------------------+
| Payment methods                |
| Card/bank details              |
| Continue CTA                   |
+--------------------------------+
```
Flow: `PATCH /v1/customer/checkout/sessions/{sessionId}/payment-method` -> refresh summary.

8. Checkout submit / drift
```text
+--------------------------------+
| Final review                   |
| Place order CTA                |
| Conflict banner (if drift)     |
+--------------------------------+
```
Flow: `POST /v1/customer/checkout/sessions/{sessionId}/submit` (with `Idempotency-Key`) -> if `409` then `POST /v1/customer/checkout/sessions/{sessionId}/accept-drift` -> submit again.

9. Checkout confirmation
```text
+--------------------------------+
| Success state                  |
| Order id + next actions        |
+--------------------------------+
```
Flow: render submit response; optionally navigate to `GET /v1/customer/orders/{id}`.

### Phase 4 - Post purchase journey

1. Orders list
```text
+--------------------------------+
| Filter bar                     |
| Order cards                    |
+--------------------------------+
```
Flow: `GET /v1/customer/orders`.

2. Order detail
```text
+--------------------------------+
| Order timeline                 |
| Items/payments/shipping        |
| Actions: cancel/return/reorder |
+--------------------------------+
```
Flow: `GET /v1/customer/orders/{id}` + `GET /v1/customer/orders/{id}/return-eligibility`.

3. Cancel order
```text
+--------------------------------+
| Reason selector                |
| Confirm cancel CTA             |
+--------------------------------+
```
Flow: `POST /v1/customer/orders/{id}/cancel`.

4. Reorder
```text
+--------------------------------+
| Eligible items                 |
| Add all to cart CTA            |
+--------------------------------+
```
Flow: `POST /v1/customer/orders/{id}/reorder`.

5. Return create wizard
```text
+--------------------------------+
| Item selector                  |
| Reason + note                  |
| Photo uploader                 |
| Submit return CTA              |
+--------------------------------+
```
Flow: `POST /v1/customer/returns/photos` (0..n) -> `POST /v1/customer/orders/{orderId}/returns`.

6. Returns list/detail
```text
+--------------------------------+
| Return list                    |
| Return status timeline         |
+--------------------------------+
```
Flow: `GET /v1/customer/returns` -> `GET /v1/customer/returns/{id}`.

7. Invoice preview/download
```text
+--------------------------------+
| Invoice metadata               |
| Preview pane                   |
| Download PDF CTA               |
+--------------------------------+
```
Flow: `GET /v1/customer/orders/{orderId}/invoice` -> `GET /v1/customer/orders/{orderId}/invoice.pdf`.

8. Legacy quotations
```text
+--------------------------------+
| Quotation list/detail          |
| Accept or reject actions       |
+--------------------------------+
```
Flow: `GET /v1/customer/quotations` -> `GET /v1/customer/quotations/{id}` -> `POST /v1/customer/quotations/{id}/accept|reject`.

### Phase 5 - Trust and compliance

1. My reviews list/detail
```text
+--------------------------------+
| Reviews list                   |
| Review detail card             |
+--------------------------------+
```
Flow: `GET /v1/customer/reviews/me` -> `GET /v1/customer/reviews/me/{id}`.

2. Review create/edit/report
```text
+--------------------------------+
| Rating selector                |
| Text/media input               |
| Submit / edit / report actions |
+--------------------------------+
```
Flow: `POST /v1/customer/reviews` -> `PATCH /v1/customer/reviews/{id}` -> `GET /v1/customer/reviews/report-reasons` -> `POST /v1/customer/reviews/{id}/report`.

3. Verification dashboard/detail
```text
+--------------------------------+
| Verification status cards      |
| Active case banner             |
| Start/resume CTA               |
+--------------------------------+
```
Flow: `GET /api/customer/verifications/schema` + `GET /api/customer/verifications/active` + `GET /api/customer/verifications` -> `GET /api/customer/verifications/{id}`.

4. Verification submit/documents/lifecycle
```text
+--------------------------------+
| Dynamic verification form      |
| Document uploads               |
| Resubmit/renew actions         |
+--------------------------------+
```
Flow: `POST /api/customer/verifications` -> `POST /api/customer/verifications/{id}/documents` -> `POST /api/customer/verifications/{id}/resubmit` -> `POST /api/customer/verifications/renew`.

### Phase 6 - B2B customer journey

1. Quote request from cart/product
```text
+--------------------------------+
| RFQ form                       |
| Quantity/terms fields          |
| Submit quote CTA               |
+--------------------------------+
```
Flow: `POST /api/customer/quotes/from-cart` or `POST /api/customer/quotes/from-product`.

2. My quotes and approvals
```text
+--------------------------------+
| Quotes list                    |
| Awaiting approval list         |
+--------------------------------+
```
Flow: `GET /api/customer/quotes` + `GET /api/customer/quotes/awaiting-my-approval`.

3. Quote detail/actions/documents
```text
+--------------------------------+
| Quote version timeline         |
| Action buttons                 |
| Download document CTA          |
+--------------------------------+
```
Flow: `GET /api/customer/quotes/{id}` -> actions (`withdraw`,`request-revision`,`submit-acceptance`,`finalize-acceptance`,`reject-acceptance`,`save-as-template`) -> `GET /api/customer/quotes/{quoteId}/versions/{versionId}/documents/{locale}`.

4. Company profile and governance
```text
+--------------------------------+
| Company profile form           |
| Branches table                 |
| Invitations table              |
| Membership roles               |
+--------------------------------+
```
Flow: `POST /api/customer/companies` -> `GET/PATCH /api/customer/companies/{id}` -> `POST /api/customer/companies/{id}/branches` + `DELETE /api/customer/companies/{id}/branches/{branchId}` -> invitation accept/decline/send endpoints -> membership `PATCH/DELETE /api/customer/companies/{id}/memberships/{membershipId}`.

### Phase 7 - Admin foundation and identity

1. Admin login/profile/debug
```text
+--------------------------------+
| Admin credentials              |
| Sign in CTA                    |
| Debug auth tests (non-prod)    |
+--------------------------------+
```
Flow: `POST /v1/admin/identity/sign-in` -> `GET /v1/admin/identity/me` -> optional debug `_test` endpoints.

2. MFA/step-up/TOTP
```text
+--------------------------------+
| Challenge status               |
| OTP/TOTP input                |
| Enroll/confirm/rotate actions  |
+--------------------------------+
```
Flow: `POST /v1/admin/identity/mfa/challenge` -> `POST /v1/admin/identity/mfa/step-up` -> `POST /v1/admin/identity/mfa/step-up/confirm` -> `POST /v1/admin/identity/mfa/totp/enroll|confirm|rotate`.

3. Invitations and accounts security
```text
+--------------------------------+
| Invite admin form              |
| Account sessions table         |
| MFA factors + role editor      |
+--------------------------------+
```
Flow: invitation create/delete/accept endpoints -> account sessions list/delete -> mfa factors/read + reset + role patch.

### Phase 8 - Admin commerce operations

1. Orders queue/detail/actions
```text
+--------------------------------+
| Orders filter + export         |
| Order detail + audit timeline  |
| Fulfillment/payment actions    |
+--------------------------------+
```
Flow: admin orders list/export -> detail/audit -> fulfillment endpoints -> payment endpoints.

2. Admin quotation panel + checkout monitor
```text
+--------------------------------+
| Quotation compose/detail       |
| Send/expire/convert actions    |
| Checkout sessions monitor      |
+--------------------------------+
```
Flow: quotations create/send/expire/convert + checkout sessions list/expire.

3. Returns and refunds operations
```text
+--------------------------------+
| Returns queue + export         |
| Return detail and decisions    |
| Refund retry/confirm           |
| Return policy editor           |
+--------------------------------+
```
Flow: returns list/detail/export -> decisions endpoints -> refund endpoints -> policy get/put.

4. Invoice center
```text
+--------------------------------+
| Invoice list/search/export     |
| Invoice detail/pdf             |
| Render queue/jobs actions      |
+--------------------------------+
```
Flow: invoices list/by-id/by-number/pdf -> render queue + retry + regenerate + resend + export.

5. Reviews moderation
```text
+--------------------------------+
| Queue filters                  |
| Review detail + customer view  |
| Decision + notes + delete      |
| Policy wordlists/markets       |
+--------------------------------+
```
Flow: queue/detail/by-customer -> decide/notes/delete -> wordlists get/put/delete + market patch.

6. Verification queue
```text
+--------------------------------+
| Verification queue             |
| Case detail + doc open         |
| Approve/reject/request-info    |
+--------------------------------+
```
Flow: `GET /api/admin/verifications` -> detail/doc open -> approve/reject/request-info/revoke.

7. Search operations
```text
+--------------------------------+
| Search health card             |
| Jobs list                      |
| Reindex action + stream log    |
+--------------------------------+
```
Flow: health/jobs -> reindex -> stream by job id.

### Phase 9 - Admin commercial controls

1. Catalog products/workflow/media/docs
```text
+--------------------------------+
| Product table + detail drawer  |
| Create/edit forms              |
| Workflow buttons               |
| Media/doc managers             |
+--------------------------------+
```
Flow: products list/detail/create/patch -> workflow actions -> media/doc actions.

2. Catalog categories/brands/manufacturers/import
```text
+--------------------------------+
| Categories tree                |
| Brand/manufacturer forms       |
| Bulk import upload             |
+--------------------------------+
```
Flow: categories create/patch/delete/reparent + brands/manufacturers create/patch + bulk-import.

3. Pricing controls
```text
+--------------------------------+
| Promotions panel               |
| Coupons panel                  |
| Tax rates panel                |
| B2B tiers panel                |
| Product/account assignment     |
+--------------------------------+
```
Flow: promotions CRUD+activate/deactivate -> coupons CRUD/deactivate/redemptions -> tax-rates CRUD -> b2b tiers CRUD -> product tier-prices set/delete -> account tier set + explanations read.

4. Inventory controls
```text
+--------------------------------+
| Batch list/detail              |
| Create/update batch form       |
| Adjust/transfer/writeoff forms |
+--------------------------------+
```
Flow: inventory batches list/detail/create/patch + movement adjust/transfer/writeoff.

5. B2B admin controls
```text
+--------------------------------+
| Quotes queue/detail            |
| Draft/publish controls         |
| Company suspend action         |
+--------------------------------+
```
Flow: admin quotes list/detail/draft/publish + company suspend.

### Phase 10 - Internal adapters and webhook handling (non-UI)

1. Payment webhook handler
```text
+--------------------------------+
| Provider callback ingress      |
| Signature validation           |
| Event dispatch                 |
+--------------------------------+
```
Flow: `POST /v1/webhooks/payment-gateway/{providerId}` -> order/payment/invoice internal orchestration.

2. Internal catalog/pricing/inventory/orders/invoices bridges
```text
+--------------------------------+
| Internal command bus           |
| Domain adapters                |
| Outbox/worker execution        |
+--------------------------------+
```
Flow:
- `POST /v1/internal/catalog/restrictions/check`
- `POST /internal/pricing/calculate`
- `POST /v1/internal/inventory/reservations`
- `POST /v1/internal/inventory/reservations/{id}/convert`
- `DELETE /v1/internal/inventory/reservations/{id}`
- `POST /v1/internal/inventory/movements/return`
- `POST /v1/internal/orders/{id}/advance-refund-state`
- `POST /v1/internal/invoices/issue-on-capture`
- `POST /v1/internal/credit-notes/issue`

## 6.3) Endpoint Usage Assurance

Rule: every endpoint listed in Sections 6 (phase tables) and 10 (internal/webhook) must appear at least once in one single-screen flow or one global flow above.

Verification checklist:
- Customer identity endpoints: used in Phase 1 blueprints and customer global flow.
- Customer discovery endpoints: used in Phase 2 blueprints and customer global flow.
- Checkout endpoints (including drift and idempotency): used in Phase 3 blueprints and internal global flow.
- Post-purchase endpoints (orders/returns/invoices/legacy quotations): used in Phase 4 blueprints.
- Reviews + verification endpoints: used in Phase 5 blueprints.
- B2B customer endpoints: used in Phase 6 blueprints.
- Admin identity endpoints (including `_test` non-prod): used in Phase 7 blueprints.
- Admin operations endpoints (orders/returns/refunds/invoices/reviews/verification/search): used in Phase 8 blueprints.
- Admin commercial endpoints (catalog/pricing/inventory/b2b): used in Phase 9 blueprints.
- Internal + webhook endpoints: used in Phase 10 blueprints and internal global flow.

Implementation guardrail:
- Add CI lint rule to fail when a newly added endpoint in any `openapi.*.json` file is not referenced in this document under either phase table rows or screen-flow sections.

## 6.4) Full Mobile-Size ASCII Wireframes (Concrete Screen Layouts)

Wireframe style rule used below:
- Every screen is a full mobile frame.
- Components are explicit (logo, inputs, buttons, links, lists, cards, actions).
- Main app shell screens include bottom nav: `Home | Categories | Cart | Orders | Settings`.
- Auth and one-time verification flows can hide bottom nav.

Bottom nav ASCII pattern:
```text
+--------------------------------------+
| Home | Categories | Cart | Orders | Settings |
+--------------------------------------+
```

Bottom nav routing map:
- `Home` -> Home screen (`GET /v1/customer/catalog/categories`, `GET /v1/customer/catalog/brands`)
- `Categories` -> Category listing entry (`GET /v1/customer/catalog/categories/{slug}/products`)
- `Cart` -> Cart pricing panel (`POST /customer/pricing/price-cart`)
- `Orders` -> Orders list (`GET /v1/customer/orders`)
- `Settings` -> Locale/account settings (`PATCH /v1/customer/identity/locale`, account security/session screens)

### Phase 1 - Auth and session foundation

1. Splash/session bootstrap
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|                                      |
|                 LOGO                 |
|                                      |
|            Loading session...        |
|                                      |
|        [ spinner animation ]         |
|                                      |
|      Checking token and profile      |
|                                      |
|                                      |
|                                      |
|                                      |
|                                      |
|                                      |
+--------------------------------------+
| Retry                               >|
+--------------------------------------+
```

2. Login
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|                                      |
|                 LOGO                 |
|                                      |
|               Welcome back           |
|                                      |
|  Email or Phone                      |
|  [_______________________________]   |
|                                      |
|  Password                            |
|  [_______________________________]   |
|                      [ Show / Hide ] |
|                                      |
|  [            Login Button         ] |
|                                      |
|  Register                             |
|  [          Create Account         ] |
|                                      |
|  Forgot password?                    |
|  (link) Reset Password               |
+--------------------------------------+
```

3. Register
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|                 LOGO                 |
|            Create Account            |
|                                      |
|  Full Name                           |
|  [_______________________________]   |
|  Email                               |
|  [_______________________________]   |
|  Phone                               |
|  [_______________________________]   |
|  Password                            |
|  [_______________________________]   |
|                                      |
|  [ ] I agree to Terms and Privacy    |
|                                      |
|  [          Register Button        ] |
|                                      |
|  Already have account? (link) Login  |
+--------------------------------------+
```

4. OTP verify
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|               Verify OTP             |
|  Code sent to +966******42           |
|                                      |
|        [__] [__] [__] [__] [__] [__] |
|                                      |
|  Resend in 00:28                     |
|  (link) Resend code                  |
|                                      |
|  [          Verify Button          ] |
|                                      |
|  (link) Change phone/email           |
+--------------------------------------+
```

5. Password reset request
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|             Reset Password           |
|                                      |
|  Email or Phone                      |
|  [_______________________________]   |
|                                      |
|  [       Send Reset Link/Button    ] |
|                                      |
|  (link) Back to Login                |
+--------------------------------------+
```

6. Password reset confirm
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|            Set New Password          |
|                                      |
|  New Password                        |
|  [_______________________________]   |
|  Confirm Password                    |
|  [_______________________________]   |
|                                      |
|  [         Save Password           ] |
|                                      |
|  Password rules checklist            |
+--------------------------------------+
```

7. Email confirmation deep link
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
|                                      |
|                 LOGO                 |
|                                      |
|        Email Verified Successfully   |
|                                      |
|  Your account is now confirmed.      |
|                                      |
|  [            Continue             ] |
+--------------------------------------+
```

8. Account security
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| < Account Security              Save |
+--------------------------------------+
| Change Password                       |
| Current [_________________________]   |
| New     [_________________________]   |
| Confirm [_________________________]   |
| [       Update Password           ]   |
|--------------------------------------|
| Sessions                               |
| iPhone 15 Pro        Active Now       |
| Mac Safari            Yesterday        |
|--------------------------------------|
| [             Sign Out             ]  |
+--------------------------------------+
```

9. Device/session management
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| < Active Sessions                    |
+--------------------------------------+
| This device                           |
| iPhone 15 Pro / Riyadh / Now         |
|--------------------------------------|
| Other devices                         |
| iPad / Riyadh / 2h ago       [Revoke]|
| Chrome / Jeddah / 1d ago     [Revoke]|
|--------------------------------------|
| [      Revoke All Other Sessions   ] |
+--------------------------------------+
```

10. Locale settings
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| < Language & Market                  |
+--------------------------------------+
| Language                              |
| [ English (EN)                    v ]|
|                                      |
| Market                                |
| [ SA (KSA)                        v ]|
|                                      |
| Currency                              |
| [ SAR                             v ]|
|                                      |
|  [             Save Changes        ] |
+--------------------------------------+
```

### Phase 2 - Customer discovery experience

1. Home
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| [ Search products, brands...      ]  |
|--------------------------------------|
| Categories                            |
| [Tile][Tile][Tile][Tile]             |
|--------------------------------------|
| Brands                                |
| [Brand][Brand][Brand][Brand]         |
|--------------------------------------|
| Featured                              |
| [Product card]                        |
| [Product card]                        |
+--------------------------------------+
```

2. Search landing
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| < Search                             |
| [ Search input                     ] |
|--------------------------------------|
| Suggestions                           |
| - cement board                        |
| - ceramic tile                        |
| - steel profile                       |
|--------------------------------------|
| Recent searches                        |
| - paint white matte                   |
+--------------------------------------+
```

3. Search result list
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| < Results for "tile"               |
| [Filter] [Sort] [Brand] [Price]      |
|--------------------------------------|
| [Product Card: image/title/price]    |
| [Product Card: image/title/price]    |
| [Product Card: image/title/price]    |
|--------------------------------------|
| [            Load More             ] |
+--------------------------------------+
```

4. Category listing
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| < Category: Bathroom Tiles           |
| [Filter chips.....................]  |
|--------------------------------------|
| [P1] [P2]                            |
| [P3] [P4]                            |
| [P5] [P6]                            |
+--------------------------------------+
```

5. Product detail
```text
+--------------------------------------+
| 9:41                           100%  |
+--------------------------------------+
| < Product Detail                     |
| [ image gallery .................. ] |
| Product Name                          |
| Price: 120 SAR                        |
| Stock: In Stock                       |
| Rating: 4.7 (125)                     |
|--------------------------------------|
| Description                            |
| lorem ipsum ...                        |
|--------------------------------------|
| Qty [-] [1] [+]                        |
| [           Add To Cart            ]  |
+--------------------------------------+
```

6. Product rating summary
```text
+--------------------------------------+
| Rating Summary                        |
| 4.7 ★★★★★                             |
| 125 reviews                           |
| 5★ ████████                           |
| 4★ █████                              |
| 3★ ██                                 |
+--------------------------------------+
```

7. Stock badge and availability
```text
+--------------------------------------+
| Availability                          |
| [ IN STOCK ]                          |
| Delivery by: Tomorrow                 |
+--------------------------------------+
```

### Phase 3 - Cart and checkout flow

1. Cart pricing panel
```text
+--------------------------------------+
| < Cart                               |
| [Cart line item]                     |
| [Cart line item]                     |
| Subtotal / Discount / Tax / Total    |
| [        Proceed to Checkout       ] |
+--------------------------------------+
```

2. Checkout start
```text
+--------------------------------------+
| < Checkout                           |
| Order summary snapshot               |
| Address: not set                     |
| Payment: not set                     |
| [          Start Checkout          ] |
+--------------------------------------+
```

3. Checkout summary
```text
+--------------------------------------+
| < Checkout Summary                   |
| Stepper: Address > Shipping > Pay    |
| Items list                            |
| Totals                                |
| [              Continue             ] |
+--------------------------------------+
```

4. Checkout shipping quotes
```text
+--------------------------------------+
| < Shipping Methods                   |
| ( ) Standard 2-3 days   15 SAR       |
| ( ) Express  1 day      30 SAR       |
| [              Select               ] |
+--------------------------------------+
```

5. Checkout address step
```text
+--------------------------------------+
| < Shipping Address                   |
| Name   [__________________________]  |
| Phone  [__________________________]  |
| City   [__________________________]  |
| Street [__________________________]  |
| [         Save and Continue        ] |
+--------------------------------------+
```

6. Checkout shipping step
```text
+--------------------------------------+
| < Delivery Option                    |
| ( ) Home delivery                    |
| ( ) Pickup point                     |
| [         Continue                 ] |
+--------------------------------------+
```

7. Checkout payment step
```text
+--------------------------------------+
| < Payment Method                     |
| ( ) Card                             |
| ( ) Bank transfer                    |
| Card No [_________________________]  |
| [         Continue                 ] |
+--------------------------------------+
```

8. Checkout submit / drift
```text
+--------------------------------------+
| < Review and Place Order             |
| Final totals + shipping + payment    |
| [            Place Order           ] |
| If conflict: [Accept Changes] dialog |
+--------------------------------------+
```

9. Checkout confirmation
```text
+--------------------------------------+
|              Success                  |
| Order #123456 created                 |
| [           View Order              ] |
| [        Continue Shopping          ] |
+--------------------------------------+
```

### Phase 4 - Post purchase journey

1. Orders list
```text
+--------------------------------------+
| < My Orders                          |
| [All][Pending][Delivered]            |
| Order card #1                        |
| Order card #2                        |
+--------------------------------------+
```

2. Order detail
```text
+--------------------------------------+
| < Order #123456                      |
| Timeline                              |
| Items / payment / shipping            |
| [Cancel] [Return] [Reorder]           |
+--------------------------------------+
```

3. Cancel order
```text
+--------------------------------------+
| < Cancel Order                       |
| Reason [v]                            |
| Note   [__________________________]   |
| [          Confirm Cancel          ]  |
+--------------------------------------+
```

4. Reorder
```text
+--------------------------------------+
| < Reorder                            |
| Previous items list                   |
| Qty controls                          |
| [            Add All to Cart        ] |
+--------------------------------------+
```

5. Return eligibility
```text
+--------------------------------------+
| < Return Eligibility                 |
| Eligible items checklist              |
| Policy notice                         |
| [          Continue Return          ] |
+--------------------------------------+
```

6. Return create wizard
```text
+--------------------------------------+
| < Create Return                      |
| Select item(s)                        |
| Reason [v]                            |
| Upload photo [ + ]                    |
| [            Submit Return          ] |
+--------------------------------------+
```

7. Returns list
```text
+--------------------------------------+
| < My Returns                         |
| Return #R1001  Pending               |
| Return #R1002  Approved              |
+--------------------------------------+
```

8. Return detail
```text
+--------------------------------------+
| < Return #R1001                      |
| Status timeline                       |
| Items + refund amount                 |
| Attachments                           |
+--------------------------------------+
```

9. Invoice preview
```text
+--------------------------------------+
| < Invoice Preview                    |
| Invoice metadata                      |
| [ PDF preview canvas ]                |
| [          Download PDF             ] |
+--------------------------------------+
```

10. Invoice PDF download
```text
+--------------------------------------+
| < Invoice PDF                        |
| File ready: invoice-123456.pdf        |
| [Open] [Share] [Download again]       |
+--------------------------------------+
```

11. Legacy quotation list
```text
+--------------------------------------+
| < Quotations                         |
| Quote #Q101  Pending                 |
| Quote #Q102  Expired                 |
+--------------------------------------+
```

12. Legacy quotation detail/actions
```text
+--------------------------------------+
| < Quote #Q101                        |
| Line items + totals                   |
| Terms and validity                    |
| [Accept] [Reject]                     |
+--------------------------------------+
```

### Phase 5 - Trust and compliance

1. My reviews list
```text
+--------------------------------------+
| < My Reviews                         |
| Review card #1                        |
| Review card #2                        |
+--------------------------------------+
```

2. My review detail
```text
+--------------------------------------+
| < Review Detail                      |
| Rating + text + media                 |
| Moderation status                     |
| [Edit] [Report]                       |
+--------------------------------------+
```

3. Submit review
```text
+--------------------------------------+
| < Write Review                       |
| Stars: ★★★★★                          |
| Comment [_________________________]   |
| Add media [ + ]                       |
| [             Submit Review         ] |
+--------------------------------------+
```

4. Edit review
```text
+--------------------------------------+
| < Edit Review                        |
| Stars + text editable                 |
| [              Save Changes         ] |
+--------------------------------------+
```

5. Report review
```text
+--------------------------------------+
| < Report Review                      |
| Reason ( ) Spam ( ) Abuse             |
| Note [___________________________]    |
| [               Report              ] |
+--------------------------------------+
```

6. Verification dashboard
```text
+--------------------------------------+
| < Verification                       |
| Active verification card              |
| Previous requests list                |
| [Start New] [Resume]                  |
+--------------------------------------+
```

7. Verification detail
```text
+--------------------------------------+
| < Verification Detail                |
| Case status + timeline                |
| Requested info/documents              |
| [Upload Docs] [Resubmit]              |
+--------------------------------------+
```

8. Submit verification
```text
+--------------------------------------+
| < Submit Verification                |
| Dynamic fields                        |
| Identity/business details             |
| [            Submit Request         ] |
+--------------------------------------+
```

9. Upload verification document
```text
+--------------------------------------+
| < Upload Document                    |
| Document type [v]                     |
| [Pick File] [Take Photo]              |
| Progress bar                           |
| [              Upload               ] |
+--------------------------------------+
```

10. Resubmit verification
```text
+--------------------------------------+
| < Resubmit                           |
| Requested fixes checklist             |
| Updated fields                         |
| [             Resubmit              ] |
+--------------------------------------+
```

11. Renew verification
```text
+--------------------------------------+
| < Renew Verification                 |
| Renewal reason/details                |
| [              Request Renewal      ] |
+--------------------------------------+
```

### Phase 6 - B2B customer journey

1. Quote request from cart
```text
+--------------------------------------+
| < Request Quote (Cart)               |
| Cart summary                           |
| Terms / expected qty                   |
| [            Submit RFQ             ] |
+--------------------------------------+
```

2. Quote request from product
```text
+--------------------------------------+
| < Request Quote (Product)            |
| Product snapshot                       |
| Qty / terms                            |
| [            Submit RFQ             ] |
+--------------------------------------+
```

3. My quotes list
```text
+--------------------------------------+
| < My Quotes                          |
| [All][Awaiting][Accepted]             |
| Quote cards                            |
+--------------------------------------+
```

4. Awaiting my approval list
```text
+--------------------------------------+
| < Awaiting My Approval               |
| Approval item #1                       |
| Approval item #2                       |
+--------------------------------------+
```

5. Quote detail
```text
+--------------------------------------+
| < Quote Detail                       |
| Version timeline                        |
| Pricing table                           |
| [Actions] toolbar                       |
+--------------------------------------+
```

6. Quote actions
```text
+--------------------------------------+
| < Quote Actions                       |
| [Withdraw] [Request Revision]         |
| [Submit Acceptance] [Finalize]        |
| [Reject Acceptance] [Save Template]   |
+--------------------------------------+
```

7. Quote document download
```text
+--------------------------------------+
| < Quote Document                      |
| Version [v]   Locale [v]              |
| [            Download PDF           ] |
+--------------------------------------+
```

8. Company registration
```text
+--------------------------------------+
| < Register Company                    |
| Name / VAT / Address fields           |
| [             Create Company         ] |
+--------------------------------------+
```

9. Company profile
```text
+--------------------------------------+
| < Company Profile                     |
| Profile details editable               |
| [               Save                ] |
+--------------------------------------+
```

10. Company branches
```text
+--------------------------------------+
| < Branches                            |
| Branch list                            |
| [Add Branch]                           |
| Per row: [Delete]                      |
+--------------------------------------+
```

11. Company invitations
```text
+--------------------------------------+
| < Invitations                          |
| Invite member form                     |
| Pending invitations list               |
| [Accept] [Decline] (token flow)        |
+--------------------------------------+
```

12. Company memberships
```text
+--------------------------------------+
| < Memberships                          |
| Member rows + role selector            |
| Per row: [Save Role] [Remove]          |
+--------------------------------------+
```

### Phase 7 - Admin foundation and identity

1. Admin login
```text
+--------------------------------------+
| < Admin Login                          |
| Email [___________________________]    |
| Password [________________________]    |
| [               Login               ]  |
+--------------------------------------+
```

2. Admin profile
```text
+--------------------------------------+
| < Admin Profile                        |
| Name / role / permissions summary      |
| [Manage Security]                      |
+--------------------------------------+
```

3. Auth guard debug screen
```text
+--------------------------------------+
| < Auth Debug (non-prod)                |
| [Call protected] [Call step-up]        |
| Response panel                          |
+--------------------------------------+
```

4. Admin MFA challenge
```text
+--------------------------------------+
| < MFA Challenge                        |
| OTP [__][__][__][__][__][__]           |
| [            Complete Challenge      ] |
+--------------------------------------+
```

5. Admin step-up
```text
+--------------------------------------+
| < Step-up Auth                         |
| [Request OTP]                          |
| OTP [______________________________]   |
| [              Confirm              ]  |
+--------------------------------------+
```

6. Admin TOTP setup/rotate
```text
+--------------------------------------+
| < TOTP Setup                           |
| [ QR / Secret ]                         |
| Code [____________________________]    |
| [Enroll] [Confirm] [Rotate]            |
+--------------------------------------+
```

7. Admin invitation management
```text
+--------------------------------------+
| < Admin Invitations                    |
| Invite form                             |
| Invitation list                          |
| Per row: [Revoke]                        |
+--------------------------------------+
```

8. Admin account sessions
```text
+--------------------------------------+
| < Account Sessions                     |
| Account selector [v]                    |
| Session rows + [Revoke]                 |
+--------------------------------------+
```

9. Admin account MFA and role
```text
+--------------------------------------+
| < Account Security                      |
| MFA factors list                        |
| [Reset MFA]                             |
| Role [v] [Save Role]                    |
+--------------------------------------+
```

### Phase 8 - Admin commerce operations

1. Orders queue
```text
+--------------------------------------+
| < Orders Queue                         |
| Filters + Export                        |
| Orders table/cards                       |
+--------------------------------------+
```

2. Order detail and audit
```text
+--------------------------------------+
| < Order Detail                          |
| Order data + audit timeline             |
+--------------------------------------+
```

3. Fulfillment actions
```text
+--------------------------------------+
| < Fulfillment                            |
| [Start Picking] [Mark Packed]           |
| [Handed to Carrier] [Create Shipment]   |
| [Mark Delivered]                         |
+--------------------------------------+
```

4. Payment operations
```text
+--------------------------------------+
| < Payment Ops                            |
| Bank transfer confirm form               |
| Force state selector + action            |
+--------------------------------------+
```

5. Admin quotation panel
```text
+--------------------------------------+
| < Quotations Admin                       |
| Create quote form                        |
| [Send] [Expire] [Convert]                |
+--------------------------------------+
```

6. Checkout sessions monitor
```text
+--------------------------------------+
| < Checkout Sessions                       |
| Session list + filters                    |
| Per row: [Expire Session]                 |
+--------------------------------------+
```

7. Returns queue
```text
+--------------------------------------+
| < Returns Queue                           |
| Filters + export                           |
| Return rows                                |
+--------------------------------------+
```

8. Return decisions
```text
+--------------------------------------+
| < Return Decision                          |
| [Approve] [Approve Partial] [Reject]       |
| [Inspect] [Mark Received] [Issue Refund]   |
| [Force Refund]                             |
+--------------------------------------+
```

9. Refund operations
```text
+--------------------------------------+
| < Refund Operations                        |
| Refund detail                               |
| [Retry] [Confirm Bank Transfer]             |
+--------------------------------------+
```

10. Return policy editor
```text
+--------------------------------------+
| < Return Policy                            |
| Market [v]                                  |
| Policy editor                               |
| [Save Policy]                               |
+--------------------------------------+
```

11. Invoice center
```text
+--------------------------------------+
| < Invoice Center                           |
| Search by id/number                         |
| Invoice list + [PDF]                        |
+--------------------------------------+
```

12. Invoice jobs and actions
```text
+--------------------------------------+
| < Invoice Jobs                              |
| Render queue list                            |
| [Retry Job] [Regenerate] [Resend] [Export]  |
+--------------------------------------+
```

13. Reviews moderation queue
```text
+--------------------------------------+
| < Reviews Queue                             |
| Filters                                     |
| Review rows                                 |
+--------------------------------------+
```

14. Reviews moderation actions
```text
+--------------------------------------+
| < Review Moderation                          |
| Decision form                                |
| Notes panel                                  |
| [Delete Review]                              |
+--------------------------------------+
```

15. Reviews policy
```text
+--------------------------------------+
| < Reviews Policy                              |
| Wordlists manager                              |
| Market policy controls                         |
+--------------------------------------+
```

16. Verification queue
```text
+--------------------------------------+
| < Verification Queue                           |
| Filters + case list                             |
| [Open Document]                                 |
+--------------------------------------+
```

17. Verification decisions
```text
+--------------------------------------+
| < Verification Decision                        |
| [Approve] [Reject] [Request Info] [Revoke]    |
+--------------------------------------+
```

18. Search operations
```text
+--------------------------------------+
| < Search Operations                            |
| Health status                                   |
| Jobs list                                       |
| [Reindex] [Open Stream]                         |
+--------------------------------------+
```

### Phase 9 - Admin commercial controls

1. Catalog products
```text
+--------------------------------------+
| < Catalog Products                             |
| Product list/table                               |
| [Create] [Edit]                                 |
+--------------------------------------+
```

2. Product workflow
```text
+--------------------------------------+
| < Product Workflow                              |
| [Submit] [Publish] [Archive] [Cancel Schedule] |
+--------------------------------------+
```

3. Product media and docs
```text
+--------------------------------------+
| < Product Media & Documents                     |
| Media gallery + upload                           |
| Document uploader                                |
+--------------------------------------+
```

4. Catalog categories
```text
+--------------------------------------+
| < Categories Tree                               |
| Tree + edit panel                                |
| [Create] [Reparent] [Delete]                     |
+--------------------------------------+
```

5. Catalog brands/manufacturers
```text
+--------------------------------------+
| < Brands & Manufacturers                        |
| List + create/edit forms                         |
+--------------------------------------+
```

6. Catalog bulk import
```text
+--------------------------------------+
| < Bulk Import                                   |
| File picker                                      |
| Import options                                   |
| [Run Import]                                     |
+--------------------------------------+
```

7. Pricing promotions
```text
+--------------------------------------+
| < Promotions                                   |
| Promotions list                                  |
| [Create] [Edit] [Activate] [Deactivate]          |
+--------------------------------------+
```

8. Pricing coupons
```text
+--------------------------------------+
| < Coupons                                      |
| Coupons list                                     |
| [Create] [Edit] [Deactivate] [Redemptions]       |
+--------------------------------------+
```

9. Pricing tax rates
```text
+--------------------------------------+
| < Tax Rates                                     |
| Tax rates list + forms                           |
+--------------------------------------+
```

10. B2B pricing tiers
```text
+--------------------------------------+
| < B2B Tiers                                    |
| Tier list + create/edit/delete                   |
+--------------------------------------+
```

11. Product tier pricing
```text
+--------------------------------------+
| < Product Tier Prices                            |
| Product selector                                  |
| Tier prices grid                                  |
| [Save] [Remove]                                   |
+--------------------------------------+
```

12. Account tier assignment
```text
+--------------------------------------+
| < Account Tier Assignment                         |
| Account selector + tier selector                   |
| [Assign Tier]                                      |
| Explanation panel                                  |
+--------------------------------------+
```

13. Inventory batches
```text
+--------------------------------------+
| < Inventory Batches                               |
| Batch list/detail                                   |
| [Create] [Update]                                   |
+--------------------------------------+
```

14. Inventory movements
```text
+--------------------------------------+
| < Inventory Movements                              |
| [Adjust] [Transfer] [Writeoff] forms                |
| [Submit]                                             |
+--------------------------------------+
```

15. Admin quote queue
```text
+--------------------------------------+
| < Admin Quote Queue                               |
| Quote rows + detail                                 |
| [Draft] [Publish]                                   |
+--------------------------------------+
```

16. Admin company control
```text
+--------------------------------------+
| < Company Control                                |
| Company details                                    |
| [Suspend Company]                                  |
+--------------------------------------+
```

### Phase 10 - Internal adapters and webhook handling (non-UI)

1. Payment webhook handler monitor
```text
+--------------------------------------+
| < Webhook Monitor                                |
| Callback logs                                      |
| Signature status                                   |
| [Replay Event]                                     |
+--------------------------------------+
```

2. Internal orchestration monitor
```text
+--------------------------------------+
| < Internal Orchestration                          |
| Reservation lifecycle                               |
| Pricing calc traces                                 |
| Refund/invoice bridge events                        |
+--------------------------------------+
```

## 6.5) Endpoint Usage Assurance

Rule: every endpoint listed in Sections 6 (phase tables) and 10 (internal/webhook) must appear at least once in one single-screen flow or one global flow above.

Verification checklist:
- Customer identity endpoints: used in Phase 1 blueprints and customer global flow.
- Customer discovery endpoints: used in Phase 2 blueprints and customer global flow.
- Checkout endpoints (including drift and idempotency): used in Phase 3 blueprints and internal global flow.
- Post-purchase endpoints (orders/returns/invoices/legacy quotations): used in Phase 4 blueprints.
- Reviews + verification endpoints: used in Phase 5 blueprints.
- B2B customer endpoints: used in Phase 6 blueprints.
- Admin identity endpoints (including `_test` non-prod): used in Phase 7 blueprints.
- Admin operations endpoints (orders/returns/refunds/invoices/reviews/verification/search): used in Phase 8 blueprints.
- Admin commercial endpoints (catalog/pricing/inventory/b2b): used in Phase 9 blueprints.
- Internal + webhook endpoints: used in Phase 10 blueprints and internal global flow.

## 7) Coverage Checklist

Covered endpoint groups in this plan:
- Identity (customer + admin)
- Catalog (customer + admin)
- Search (customer + admin)
- Checkout (customer + admin + webhook)
- Orders (customer + admin + internal)
- Invoices (customer + admin + internal)
- Returns (customer + admin)
- Reviews (customer + public + admin)
- Verification (customer + admin)
- B2B quotes and companies (customer + admin)
- Pricing (customer + admin + internal)
- Inventory (customer + admin + internal)

Not directly callable from mobile UI:
- all `/v1/internal/*` endpoints
- `/internal/pricing/calculate`
- `/v1/webhooks/payment-gateway/{providerId}`

## 8) Implementation Risks And Mitigations

Risk 1: mixed route prefixes (`/v1`, `/api`, `/admin`) increase client complexity.
- Mitigation: create a domain gateway layer per alias (`IDN`, `CAT`, etc.) and never call raw paths from UI widgets.

Risk 2: some OpenAPI surfaces are path-only with light schema detail (notably parts of B2B).
- Mitigation: bind DTOs from contract files and backend slice tests before UI implementation.

Risk 3: missing explicit cart OpenAPI file in current exported artifacts.
- Mitigation: treat cart as dependency on spec 009 contract and generate/commit `openapi.cart.json` before full cart API wiring.

Risk 4: state transition endpoints can produce conflict responses (`409`).
- Mitigation: standardize conflict UI with one reusable conflict dialog and forced refresh path.

## 9) Suggested Ticket Breakdown Template

Use this template for each screen ticket:
- Screen name:
- Phase:
- Persona:
- Required endpoints:
- Required request models:
- Required query/path/header params:
- Success states:
- Empty states:
- Error states by status code:
- Telemetry events:
- Definition of done:
