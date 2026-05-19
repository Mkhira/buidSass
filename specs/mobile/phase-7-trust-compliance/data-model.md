# Data Model — Phase 7: Trust & Compliance

> Sources: `openapi.verification.json`, `openapi.reviews.json`.

## Verification

### GET `/api/customer/verifications/schema`
Response: see §S-7.2 in `spec.md`.

### POST `/api/customer/verifications`
Headers: `Idempotency-Key` REQUIRED.
Request:
```json
{
  "kind": "string",
  "marketCode": "SA | EG",
  "fields": { "businessLicense": "AB123", "vat": "300..." }
}
```
Response:
```json
{ "id": "uuid", "state": "submitted", "createdAt": "iso8601" }
```

### GET `/api/customer/verifications`
Response: list per §S-7.1.

### GET `/api/customer/verifications/active`
Response: active per §S-7.1.

### GET `/api/customer/verifications/{id}`
Response: detail per §S-7.3.

### POST `/api/customer/verifications/{id}/documents`
Request: multipart `file` + form `slotKey`.
Response:
```json
{ "slotKey": "string", "url": "https://...", "uploadedAt": "iso8601" }
```

### POST `/api/customer/verifications/{id}/resubmit`
Headers: `Idempotency-Key` REQUIRED (BR-4a in `spec.md`).
Request:
```json
{ "fields": { "businessLicense": "new value" }, "noteToAdmin": "string?" }
```
Response: refreshed detail.

### POST `/api/customer/verifications/renew`
Headers: `Idempotency-Key` REQUIRED (BR-5a in `spec.md`).
Request:
```json
{ "priorVerificationId": "uuid", "marketCode": "SA | EG" }
```
Response:
```json
{ "id": "uuid", "state": "submitted", "createdAt": "iso8601", "priorVerificationId": "uuid" }
```

## Reviews — customer

### POST `/v1/customer/reviews`
Headers: `Idempotency-Key` REQUIRED.
Request:
```json
{
  "productId": "uuid",
  "orderId": "uuid",
  "rating": 5,
  "comment": "string",
  "locale": "ar | en",
  "mediaIds": ["uuid"]
}
```
Response: see §S-7.5.

### GET `/v1/customer/reviews/me`
Query: `state`, `page`, `pageSize`.
Response: see §S-7.6.

### GET `/v1/customer/reviews/me/{id}`
Response: see §S-7.7.

### PATCH `/v1/customer/reviews/{id}`
Request:
```json
{ "rating": 4, "comment": "updated", "mediaIds": ["uuid"] }
```
Response: refreshed detail.

### GET `/v1/customer/reviews/report-reasons`
Response: see §S-7.8.

### POST `/v1/customer/reviews/{id}/report`
Request:
```json
{ "reasonKey": "spam | abuse | fake | other", "note": "string?" }
```
Response:
```json
{ "id": "uuid", "state": "submitted" }
```
