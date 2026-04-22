# HTTP Contract — Pricing & Tax Engine v1 (Spec 007-a)

**Base**: `/v1/`. Errors: RFC 7807 + `reasonCode`.

---

## Customer

### POST /v1/customer/pricing/price-cart
Preview-mode pricing. No side effects.

Request:
```json
{
  "marketCode": "ksa",
  "locale": "ar",
  "lines": [
    { "productId": "…", "qty": 2 }
  ],
  "couponCode": "WELCOME10"
}
```

Response:
```json
{
  "lines": [ /* see explanation JSON in data-model.md */ ],
  "totals": {
    "subtotalMinor": 16200,
    "discountMinor": 3800,
    "taxMinor": 2430,
    "grandTotalMinor": 18630
  },
  "currency": "SAR",
  "explanationHash": "sha256-base64url-…"
}
```

Reason codes: `pricing.coupon.invalid`, `pricing.coupon.expired`, `pricing.coupon.limit_reached`, `pricing.coupon.excludes_restricted`, `pricing.product.not_found`, `pricing.currency_mismatch`, `pricing.tax_rate_missing`.

---

## Admin

All require admin JWT (spec 004); permissions below.

### Tax rates
- `GET /v1/admin/pricing/tax-rates?market=` — `pricing.tax.read`
- `POST /v1/admin/pricing/tax-rates` — `pricing.tax.write`. Body: `{ marketCode, kind, rateBps, effectiveFrom, effectiveTo? }`.
- `PATCH /v1/admin/pricing/tax-rates/{id}` — closes `effective_to`; does not mutate historical row (audit preserves chain).

### Promotions
- CRUD under `/v1/admin/pricing/promotions` — `pricing.promotion.write`.
- `POST /v1/admin/pricing/promotions/{id}/activate`, `/deactivate`.

### Coupons
- CRUD under `/v1/admin/pricing/coupons` — `pricing.coupon.write`.
- `GET /v1/admin/pricing/coupons/{id}/redemptions` — list redemptions with pagination.

### B2B tiers
- CRUD under `/v1/admin/pricing/b2b-tiers` — `pricing.tier.write`.
- `POST /v1/admin/pricing/accounts/{accountId}/tier` — assign tier.
- `POST /v1/admin/pricing/products/{productId}/tier-prices` — upsert product tier price.

### Inspection
- `GET /v1/admin/pricing/explanations/{ownerKind}/{ownerId}` — fetch immutable explanation for a quote/order.

---

## Internal (service-to-service)

### POST /v1/internal/pricing/calculate
Called by cart/checkout/orders/quotations (same VPC, service-to-service JWT).
Request mirrors the customer endpoint plus `mode: "preview" | "issue"`, `quotationId?`, `orderId?`.
Response same as customer + a persisted `explanationId` when `mode=issue`.

Reason codes: as above.

---

## Events emitted
- `pricing.tax_rate.changed`
- `pricing.promotion.activated|deactivated|updated`
- `pricing.coupon.created|updated|deactivated|exhausted`
- `pricing.explanation.recorded` (on issue mode)

Each event published via spec 003 event bus; consumed by analytics + notification campaign specs later.
