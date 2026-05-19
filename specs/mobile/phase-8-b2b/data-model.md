# Data Model — Phase 8: B2B

> Sources: `openapi.b2b.json` (20 customer ops) + `openapi.orders.json` (4 legacy quotation ops).

## Quotes

### POST `/api/customer/quotes/from-cart`
Headers: `Idempotency-Key` REQUIRED.
Request:
```json
{
  "cartLines": [{ "productId": "uuid", "qty": 5 }],
  "terms": "string",
  "expectedDeliveryDate": "iso8601?",
  "note": "string?"
}
```
Response:
```json
{ "id": "uuid", "quoteNumber": "Q-...", "state": "draft", "createdAt": "iso8601" }
```

### POST `/api/customer/quotes/from-product`
Headers: `Idempotency-Key` REQUIRED.
Request:
```json
{ "productId": "uuid", "qty": 100, "terms": "string", "expectedDeliveryDate": "iso8601?", "note": "string?" }
```
Response: same as above.

### GET `/api/customer/quotes`
Response: see §S-8.1 in `spec.md`.

### GET `/api/customer/quotes/awaiting-my-approval`
Response: list with `submittedBy` + `submittedAt`.

### GET `/api/customer/quotes/{id}`
Response: see §S-8.5 — includes `versions[]`, `actions{}`, and optional `submittedBy`.

### POST `/api/customer/quotes/{id}/withdraw`
Request: `{ "note": "string?" }`.
Response: refreshed quote.

### POST `/api/customer/quotes/{id}/request-revision`
Request: `{ "note": "string" }`.
Response: refreshed quote.

### POST `/api/customer/quotes/{id}/submit-acceptance`
Request: `{ "note": "string?" }`.
Response: refreshed quote (now state=`awaiting_finalization` or similar).

### POST `/api/customer/quotes/{id}/finalize-acceptance`
Request: `{ "note": "string?" }`.
Response: refreshed quote (state=`accepted`).

### POST `/api/customer/quotes/{id}/reject-acceptance`
Request: `{ "note": "string" }`.
Response: refreshed quote.

### POST `/api/customer/quotes/{id}/save-as-template`
Request: `{ "templateName": "string" }`.
Response: `{ "templateId": "uuid" }`.

### GET `/api/customer/quotes/{quoteId}/versions/{versionId}/documents/{locale}`
Response: binary PDF.

## Companies

### POST `/api/customer/companies`
Headers: `Idempotency-Key` REQUIRED.
Request:
```json
{
  "name": "string",
  "vatNumber": "string",
  "address": "string",
  "commercialRegistration": "string?",
  "marketCode": "SA | EG"
}
```
Response: `{ "id": "uuid", "name": "string", "createdAt": "iso8601" }`.

### GET `/api/customer/companies/{id}`
Response: see §S-8.8.

### PATCH `/api/customer/companies/{id}`
Request: subset of fields from POST.
Response: refreshed company.

### POST `/api/customer/companies/{id}/branches`
Request:
```json
{ "name": "string", "address": "string" }
```
Response: `{ "branchId": "uuid", "name": "string", "address": "string" }`.

### DELETE `/api/customer/companies/{id}/branches/{branchId}`
Response: 204.

### POST `/api/customer/companies/{id}/invitations`
Request:
```json
{ "email": "string", "role": "buyer | approver | admin" }
```
Response: `{ "invitationId": "uuid", "email": "string", "role": "string", "sentAt": "iso8601" }`.

### POST `/api/customer/companies/invitations/{token}/accept`
Request: no body.
Response: `{ "companyId": "uuid", "role": "string" }`.

### POST `/api/customer/companies/invitations/{token}/decline`
Request: no body.
Response: 204.

### PATCH `/api/customer/companies/{id}/memberships/{membershipId}`
Request: `{ "role": "buyer | approver | admin" }`.
Response: refreshed membership.

### DELETE `/api/customer/companies/{id}/memberships/{membershipId}`
Response: 204.

## Legacy quotations

### GET `/v1/customer/quotations`
Response: list of legacy quote summaries.

### GET `/v1/customer/quotations/{id}`
Response: legacy quote detail with line items, totals, terms, validity.

### POST `/v1/customer/quotations/{id}/accept`
Request: `{ "note": "string?" }`.
Response: refreshed legacy quote.

### POST `/v1/customer/quotations/{id}/reject`
Request: `{ "note": "string" }`.
Response: refreshed legacy quote.

## Quote document cache

Same pattern as Phase 6 invoice cache:

| Path | TTL |
|---|---|
| `${tempDir}/quote-docs/{quoteId}-{versionId}-{locale}.pdf` | 30 days |
