# HTTP Contract — Orders v1 (Spec 011)

**Base**: `/v1/`. Errors: RFC 7807 + `reasonCode`.

## Customer
### GET /v1/customer/orders?status=&market=&from=&to=&page=&pageSize=
Response: `{ orders: [{ orderNumber, placedAt, grandTotalMinor, currency, highLevelStatus }], total, page, pageSize }`.

### GET /v1/customer/orders/{id}
Full detail: lines, shipments, payments, refunds (summary), timeline, invoiceUrl, returnEligibility.

### POST /v1/customer/orders/{id}/cancel
Request: `{ reason? }`. Policy-enforced. Returns updated order.
- `409 order.cancel.shipment_exists`, `409 order.cancel.policy_denied`, `400 order.cancel.window_expired`.

### POST /v1/customer/orders/{id}/reorder
Response: `{ cartId, addedLineCount, skippedLines: [{ productId, reason }] }`.

### GET /v1/customer/orders/{id}/return-eligibility
Response: `{ eligible, daysRemaining?, reasonCode? }`.

### Quotations
- `GET /v1/customer/quotations?status=`
- `GET /v1/customer/quotations/{id}`
- `POST /v1/customer/quotations/{id}/accept` — creates order; returns `{ orderId, orderNumber }`.
- `POST /v1/customer/quotations/{id}/reject`

## Admin
### GET /v1/admin/orders?filters…
Permission `orders.read`.

### GET /v1/admin/orders/{id}
### GET /v1/admin/orders/{id}/audit — full transition trail + admin mutations.

### Fulfillment (permission `orders.fulfillment.write`)
- `POST /v1/admin/orders/{id}/fulfillment/start-picking`
- `POST /v1/admin/orders/{id}/fulfillment/mark-packed`
- `POST /v1/admin/orders/{id}/fulfillment/create-shipment` — body `{ providerId, methodCode, trackingNumber?, carrier_label_url?, eta_from?, eta_to? }`.
- `POST /v1/admin/orders/{id}/fulfillment/mark-handed-to-carrier`
- `POST /v1/admin/orders/{id}/fulfillment/mark-delivered`

### Payments (permission `orders.payment.write`)
- `POST /v1/admin/orders/{id}/payments/confirm-bank-transfer` — body `{ reference, receivedAt }`.
- `POST /v1/admin/orders/{id}/payments/force-state` — body `{ toState, reason }` (high-privilege).

### Quotations admin
- `POST /v1/admin/quotations` — create draft.
- `POST /v1/admin/quotations/{id}/send` — activate + deliver.
- `POST /v1/admin/quotations/{id}/expire` — manual.
- `POST /v1/admin/quotations/{id}/convert` — creates order on behalf of buyer.

### Finance
- `GET /v1/admin/orders/export?market=&from=&to=&format=csv` — streams CSV with line-level tax + discount.

## Internal
### POST /v1/internal/orders/from-checkout
Called by spec 010 confirm. Body: `{ checkoutSessionId }`.

### POST /v1/internal/orders/from-quotation
Body: `{ quotationId }`.

## Reason codes
`order.not_found`, `order.cancel.shipment_exists`, `order.cancel.policy_denied`, `order.cancel.window_expired`, `order.quote.integrity_fail`, `order.quote.expired`, `order.state.illegal_transition`, `order.number.collision` (should never happen; fuzz-tested), `order.payment.not_in_pending_bank_transfer`, `order.fulfillment.not_ready`, `order.reorder.no_eligible_lines`.

## Events (outbox → published)
`order.placed`, `order.cancelled`, `order.cancellation_pending`, `payment.authorized`, `payment.captured`, `payment.failed`, `payment.voided`, `payment.refunded`, `payment.partially_refunded`, `fulfillment.picking_started`, `fulfillment.packed`, `fulfillment.shipped`, `fulfillment.delivered`, `fulfillment.awaiting_stock`, `quote.created`, `quote.sent`, `quote.accepted`, `quote.rejected`, `quote.expired`, `quote.converted`.
