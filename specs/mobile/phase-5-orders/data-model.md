# Data Model — Phase 5: Orders

> Source: `openapi.orders.json`.

## GET `/v1/customer/orders`
Query: `status`, `market`, `from`, `to`, `page`, `pageSize`.
Response: see §S-5.1 in `spec.md`.

## GET `/v1/customer/orders/{id}`
Response: see §S-5.2 in `spec.md`. All four state machines are present at `states.*`. CTA gates at `actions.*`.

## POST `/v1/customer/orders/{id}/cancel`
Request:
```json
{ "reason": "string", "note": "string?" }
```
Response: refreshed order detail.

## POST `/v1/customer/orders/{id}/reorder`
Request: no body.
Response:
```json
{
  "available": [{ "productId": "uuid", "qty": 2, "name": "string", "priceHint": { "amount": "string", "currency": "string" } }],
  "unavailable": [{ "productId": "uuid", "name": "string", "reason": "out_of_stock | discontinued | market_blocked" }]
}
```

## GET `/v1/customer/orders/{id}/return-eligibility`
Response:
```json
{
  "lines": [
    { "productId": "uuid", "name": "string", "qty": 1, "eligible": true, "reason": "string?", "windowEndsAt": "iso8601" }
  ],
  "anyEligible": true,
  "policyMarket": "SA | EG"
}
```

## State machines (client mirrors server)

### orderState
`placed → confirmed → completed`
`placed | confirmed → cancelled (terminal)`

### paymentState
`pending → captured → refunded`
`pending → failed → captured (after retry)`

### fulfillmentState
`pending → picking → packed → shipped → delivered`

### refundState
`none → requested → approved → issued`
`none → requested → rejected (terminal)`

Each pill is rendered independently. Combinations the UI must handle gracefully:
- `paymentState=pending` + `fulfillmentState=pending` → "Awaiting payment confirmation" hint above CTAs.
- `orderState=cancelled` + `paymentState=captured` + `refundState=requested` → "Refund in progress" hint.
