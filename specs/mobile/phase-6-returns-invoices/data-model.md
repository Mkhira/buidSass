# Data Model — Phase 6: Returns & Invoices

> Sources: `openapi.returns.json`, `openapi.invoices.json`, plus `openapi.orders.json` for return-eligibility (see Phase 5 data-model).

## Returns

### POST `/v1/customer/returns/photos`
Headers: `Idempotency-Key: <clientPhotoKey UUID v4>` REQUIRED — one UUID per tile, reused across retries for the same tile, fresh UUID for each new tile.
Request: multipart `file` (image) + form field `clientPhotoKey` (same UUID echoed for server-side dedupe).
Response:
```json
{ "photoId": "uuid", "url": "https://...", "checksum": "sha256:..." }
```
**Dedupe contract:** server dedupes by `(clientPhotoKey, checksum)`. A retry with the same key + same file returns the same `photoId`. A new file with the same key returns a 422 (key conflict). A new key with the same file may return a new `photoId` (this is fine — the wizard tracks by `photoId`, not by file).

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
