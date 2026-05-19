# Data Model — Phase 6: Returns & Invoices

> Sources: `openapi.returns.json`, `openapi.invoices.json`, plus `openapi.orders.json` for return-eligibility (see Phase 5 data-model).

## Returns

### POST `/v1/customer/returns/photos`
Request: multipart `file` (image) + form field `clientPhotoKey` (UUID).
Response:
```json
{ "photoId": "uuid", "url": "https://...", "checksum": "sha256:..." }
```

### POST `/v1/customer/orders/{orderId}/returns`
Headers: `Idempotency-Key` REQUIRED.
Request:
```json
{
  "lines": [
    { "productId": "uuid", "qty": 1, "reason": "wrong_item | damaged | not_as_described | other", "note": "string?", "photoIds": ["uuid"] }
  ],
  "preferredRefundMethod": "original_payment | bank_transfer | wallet"
}
```
Response: see §S-6.2 in `spec.md`.

### GET `/v1/customer/returns`
Query: `status`, `page`, `pageSize`.
Response: see §S-6.1.

### GET `/v1/customer/returns/{id}`
Response: see §S-6.3.

## Invoices

### GET `/v1/customer/orders/{orderId}/invoice`
Response: see §S-6.4.

### GET `/v1/customer/orders/{orderId}/invoice.pdf`
Response: `Content-Type: application/pdf` binary body.

## Local cache

| Path | TTL |
|---|---|
| `${tempDir}/invoices/{orderId}-{invoiceNumber}.pdf` | 30 days, cleared on app start sweeper |
