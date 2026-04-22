# HTTP Contract — Cart v1 (Spec 009)

**Base**: `/v1/`. Errors: RFC 7807 + `reasonCode`. Anonymous surface uses `X-Cart-Token` (header) or `cart_token` cookie.

## Customer
### GET /v1/customer/cart?market=ksa
Returns the current cart for the (account, market) or (cart_token, market). Recomputes pricing + availability each call.
Response:
```json
{
  "id": "…",
  "marketCode": "ksa",
  "lines": [{
    "id": "…",
    "productId": "…",
    "qty": 2,
    "restricted": true,
    "restrictionReasonCode": "catalog.restricted.verification_required",
    "unavailable": false,
    "stockChanged": false,
    "priceBreakdown": { /* spec 007-a line */ }
  }],
  "savedItems": [{ "productId": "…" }],
  "couponCode": null,
  "pricing": { /* spec 007-a totals */ },
  "checkoutEligibility": { "allowed": false, "reasonCode": "catalog.restricted.verification_required" },
  "b2b": { "poNumber": null, "reference": null, "notes": null, "requestedDeliveryFrom": null, "requestedDeliveryTo": null }
}
```

### POST /v1/customer/cart/lines
Request: `{ productId, qty }`. Creates cart if needed. Returns full cart.
- `409 cart.inventory_insufficient` with shortfall details.
- `400 cart.below_min_qty`, `400 cart.above_max_qty`, `400 cart.product_market_mismatch`.
- `413 cart.too_many_lines`.

### PATCH /v1/customer/cart/lines/{lineId}
Request: `{ qty }`. qty=0 removes line.

### DELETE /v1/customer/cart/lines/{lineId}

### POST /v1/customer/cart/saved-items
Request: `{ productId }` — moves to saved. Releases reservation.

### POST /v1/customer/cart/saved-items/{productId}/restore
Attempts to move back to active cart with fresh reservation.

### POST /v1/customer/cart/coupon
Request: `{ code }`. Applies single coupon.

### DELETE /v1/customer/cart/coupon

### POST /v1/customer/cart/b2b
Request: `{ poNumber?, reference?, notes?, requestedDeliveryFrom?, requestedDeliveryTo? }`. 403 if non-B2B.

### POST /v1/customer/cart/switch-market
Request: `{ toMarket }`. Archives current active cart; 302-style response with new active cart id (or empty cart).

### POST /v1/customer/cart/restore/{archivedCartId}
Restores an archived cart if within 7 days and same account.

### POST /v1/customer/cart/merge
Called internally by spec 004 login handler; also callable by client with token + JWT. Idempotent.

## Admin
### GET /v1/admin/cart/carts/{cartId}
Support read — audit-logged. Permission `cart.read`.

### GET /v1/admin/cart/abandoned?market=&from=&to=&page=&pageSize=
For follow-up campaigns.

## Reason codes
`cart.inventory_insufficient`, `cart.below_min_qty`, `cart.above_max_qty`, `cart.product_market_mismatch`, `cart.too_many_lines`, `cart.coupon.invalid`, `cart.coupon.expired`, `cart.coupon.limit_reached`, `cart.coupon.excludes_restricted`, `cart.b2b_fields_forbidden`, `cart.market_mismatch`, `cart.merge.qty_capped`, `cart.restore.expired`, `cart.restore.not_found`.

## Events
`cart.line_added|updated|removed`, `cart.coupon_applied|removed`, `cart.merged`, `cart.abandoned`, `cart.archived|restored|purged`.
