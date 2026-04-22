# HTTP Contract — Returns & Refunds v1 (Spec 013)

**Base**: `/v1/`. Errors: RFC 7807 + `reasonCode`.

## Customer
### POST /v1/customer/orders/{orderId}/returns
Body:
```json
{
  "lines": [{ "orderLineId": "...", "qty": 1, "lineReasonCode": "defective" }],
  "reasonCode": "defective",
  "customerNotes": "optional",
  "photoIds": ["..."]
}
```
Returns `{ returnNumber, id, state: "pending_review" }`.
- `400 return.window.expired`, `400 return.line.qty_exceeds_delivered`, `400 return.line.restricted_zero_window`, `409 return.order.not_delivered`.

### GET /v1/customer/returns?status=&page=&pageSize=
Paginated list.

### GET /v1/customer/returns/{id}
Full detail: lines, current state, refund (if any), photos, timeline.

### POST /v1/customer/returns/photos
Multipart upload (single photo). Returns `{ photoId }`. Max 5 MB, JPEG/PNG/HEIC.
- `413 photo.size.exceeded`, `415 photo.mime.unsupported`.

## Admin
Permission `returns.read` unless otherwise noted.

### GET /v1/admin/returns?market=&state=&from=&to=&page=&pageSize=
Filtered list.

### GET /v1/admin/returns/{id}
Full detail + audit trail.

### Approval (permission `returns.review.write`)
- `POST /v1/admin/returns/{id}/approve` — body `{ adminNotes? }`.
- `POST /v1/admin/returns/{id}/reject` — body `{ reasonCode, adminNotes? }`.
- `POST /v1/admin/returns/{id}/approve-partial` — body `{ lines: [{ returnLineId, approvedQty }], adminNotes? }`.

### Fulfillment (permission `returns.warehouse.write`)
- `POST /v1/admin/returns/{id}/mark-received` — body `{ lines: [{ returnLineId, receivedQty }] }`.
- `POST /v1/admin/returns/{id}/inspect` — body `{ lines: [{ returnLineId, sellableQty, defectiveQty, photos? }] }` → triggers spec 008 movement.

### Refund (permission `returns.refund.write`)
- `POST /v1/admin/returns/{id}/issue-refund` — body `{ restockingFeeMinor? }`. Calls `IPaymentGateway.Refund`. Idempotent on `(returnId)`.
- `POST /v1/admin/returns/{id}/force-refund` — body `{ reasonCode }`. Skip physical path. High-privilege.
- `POST /v1/admin/refunds/{refundId}/retry` — after gateway failure.
- `POST /v1/admin/refunds/{refundId}/confirm-bank-transfer` — body `{ iban, beneficiaryName, bankName, reference }`. For COD / manual path.

### Export
- `GET /v1/admin/returns/export?market=&from=&to=&format=csv`

### Policies
- `GET /v1/admin/return-policies`
- `PUT /v1/admin/return-policies/{market}` — body `{ returnWindowDays, autoApproveUnderDays?, restockingFeeBp, shippingRefundOnFullOnly }`.

## Internal
### POST /v1/internal/returns/refund-completed
Emits `refund.completed` → spec 012 credit note + spec 011 `refund_state` advance. Normally fires via outbox; exposed for replay.

## Reason codes
`return.window.expired`, `return.line.qty_exceeds_delivered`, `return.line.restricted_zero_window`, `return.order.not_delivered`, `return.state.illegal_transition`, `return.not_found`, `refund.over_refund_blocked`, `refund.gateway_failure`, `refund.already_issued`, `refund.manual_iban.required`, `photo.size.exceeded`, `photo.mime.unsupported`, `inspection.qty_mismatch`.

## Events (outbox → published)
`return.submitted`, `return.approved`, `return.approved_partial`, `return.rejected`, `return.received`, `return.inspected`, `refund.initiated`, `refund.completed`, `refund.failed`, `refund.manual_confirmed`.
