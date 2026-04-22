# HTTP Contract — Checkout v1 (Spec 010)

**Base**: `/v1/`. Errors: RFC 7807 + `reasonCode`.

## Customer
### POST /v1/customer/checkout/sessions
Request: `{ cartId, marketCode }`. Creates a fresh session bound to the cart.
Response: `{ sessionId, state, expiresAt }`.

### PATCH /v1/customer/checkout/sessions/{id}/address
Request: `{ shipping: AddressDTO, billing?: AddressDTO }`. State → `addressed`.

### GET /v1/customer/checkout/sessions/{id}/shipping-quotes
Response: `{ quotes: [{ providerId, methodCode, etaMinDays, etaMaxDays, feeMinor, expiresAt }] }`.

### PATCH /v1/customer/checkout/sessions/{id}/shipping
Request: `{ providerId, methodCode }`. State → `shipping_selected`.

### PATCH /v1/customer/checkout/sessions/{id}/payment-method
Request: `{ method }`. State → `payment_selected`. Validates market eligibility + COD cap + restriction rules.

### GET /v1/customer/checkout/sessions/{id}/summary
Response: priced totals in Preview mode + shipping fee + payment method. No side effects.

### POST /v1/customer/checkout/sessions/{id}/submit
Required header: `Idempotency-Key`. Requires JWT. Request may include card tokenization fields (provider-specific) or empty for COD/bank transfer.
Response 200:
```json
{
  "orderId": "…",
  "invoiceNumber": "…",
  "paymentState": "captured"|"pending"|"pending_cod",
  "pricing": { /* Issue explanation */ },
  "shipping": { /* selected method */ }
}
```
- `401 checkout.requires_auth`, `403 checkout.restricted_not_allowed`, `409 checkout.inventory_lost`, `409 checkout.pricing_drift`, `400 checkout.b2b.po_required`, `400 checkout.cod_cap_exceeded`, `409 checkout.already_submitted`.

### POST /v1/customer/checkout/sessions/{id}/accept-drift
Request: `{ acceptedTotalMinor, newExplanationHash }`. Resumes submit.

## Admin
### GET /v1/admin/checkout/sessions?accountId=&state=&page=&pageSize=
Permission `checkout.read`.

### POST /v1/admin/checkout/sessions/{id}/expire
Force-expire. Permission `checkout.write`. Audit-logged.

## Webhook
### POST /v1/webhooks/payment-gateway/{providerId}
Provider-signed payload. Always returns 2xx (unless signature invalid → 401 silent).
Deduped via `(provider_id, provider_event_id)`.

## Reason codes
`checkout.requires_auth`, `checkout.restricted_not_allowed`, `checkout.inventory_lost`, `checkout.pricing_drift`, `checkout.b2b.po_required`, `checkout.cod_cap_exceeded`, `checkout.cod_restricted_product`, `checkout.address_unserviceable`, `checkout.already_submitted`, `checkout.market_mismatch`, `checkout.session.expired`, `checkout.payment.declined`, `checkout.payment.gateway_timeout`, `checkout.webhook.signature_invalid`.

## Events
Session + payment-attempt + webhook events (see data-model.md).
