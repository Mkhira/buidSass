# Data Model — Phase 4: Cart & Checkout

> Sources: `openapi.checkout.json`, `openapi.pricing.json`, `openapi.inventory.json`. See Phase 2 data-model for pricing + inventory shapes.

## POST `/v1/customer/checkout/sessions`
Request:
```json
{
  "lines": [{ "productId": "uuid", "qty": 1 }],
  "couponCode": "string?",
  "buyerKind": "consumer | business",
  "marketCode": "SA | EG"
}
```
Response: see §S-4.3 in `spec.md`.

## GET `/v1/customer/checkout/sessions/{sessionId}/summary`
Response:
```json
{
  "sessionId": "uuid",
  "expiresAt": "iso8601",
  "lines": [{ "productId": "uuid", "name": "string", "qty": 1, "unitPrice": "120.00", "lineTotal": "120.00" }],
  "address": {
    "addressId": "uuid?",
    "name": "string",
    "phone": "string",
    "city": "string",
    "region": "string",
    "street": "string",
    "postalCode": "string?"
  },
  "shipping": { "method": "string?", "cost": { "amount": "15.00", "currency": "SAR" }, "etaDays": "2-3" },
  "payment": { "method": "card | apple_pay | mada | stc_pay | tabby | tamara | valu | meeza | bank_transfer | cod" },
  "totals": { "subtotal": "...", "discount": "...", "tax": "...", "shipping": "...", "grandTotal": "..." },
  "availableMethods": ["card", "apple_pay", "..."],
  "stepStatus": { "address": "complete | pending", "shipping": "...", "payment": "...", "review": "..." }
}
```

## GET `/v1/customer/checkout/sessions/{sessionId}/shipping-quotes`
Response:
```json
[
  { "method": "string", "label": "string", "price": { "amount": "string", "currency": "string" }, "etaDays": "string" }
]
```

## PATCH `/v1/customer/checkout/sessions/{sessionId}/address`
Request:
```json
{ "addressId": "uuid?", "name": "string", "phone": "string", "city": "string", "region": "string", "street": "string", "postalCode": "string?" }
```
Response: refreshed summary.

## PATCH `/v1/customer/checkout/sessions/{sessionId}/shipping`
Request: `{ "method": "string" }`
Response: refreshed summary.

## PATCH `/v1/customer/checkout/sessions/{sessionId}/payment-method`
Request:
```json
{
  "method": "card | apple_pay | mada | stc_pay | tabby | tamara | valu | meeza | bank_transfer | cod",
  "providerToken": "string?",      // present for tokenized methods
  "bankTransferReference": "string?"
}
```
Response: refreshed summary.

## POST `/v1/customer/checkout/sessions/{sessionId}/submit`
Headers: `Idempotency-Key` REQUIRED.
Request: no body OR optional client metadata (deviceId, locale snapshot).
Response: see §S-4.8 in `spec.md`.

## POST `/v1/customer/checkout/sessions/{sessionId}/accept-drift`
Request: no body.
Response: refreshed summary.

## Drift error body (409)
```json
{
  "error": {
    "code": "checkout.drift",
    "message": "Prices or availability changed",
    "correlationId": "uuid",
    "details": {
      "deltas": [
        { "kind": "price | qty | unavailable", "productId": "uuid", "before": "...", "after": "..." }
      ],
      "newTotals": { /* totals subset */ }
    }
  }
}
```

## Local cart schema (`CartStore`)

```json
{
  "lines": [
    { "productId": "uuid", "slug": "string", "name": "string", "imageUrl": "string", "qty": 1, "priceHint": { "amount": "string", "currency": "string" } }
  ],
  "couponCode": "string?",
  "updatedAt": "iso8601"
}
```

Persisted to `shared_preferences` under `cart.v1`. Clearing behavior follows BR-1 in [`./spec.md`](./spec.md): configurable via a single setting whose default clears the cart on submit-success and on sign-out. The setting itself is not user-exposed at launch; the default is the implementation.
