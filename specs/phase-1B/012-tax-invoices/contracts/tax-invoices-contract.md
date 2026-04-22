# HTTP Contract — Tax Invoices v1 (Spec 012)

**Base**: `/v1/`. Errors: RFC 7807 + `reasonCode`.

## Customer
### GET /v1/customer/orders/{orderId}/invoice.pdf
Returns `application/pdf` stream (stored bytes).
- `404 invoice.not_found` if payment not yet captured.
- `409 invoice.render_pending` if queued but not yet rendered (with `Retry-After`).

### GET /v1/customer/orders/{orderId}/invoice
Metadata only: `{ invoiceNumber, issuedAt, currency, grandTotalMinor, pdfAvailable: bool }`.

## Admin
Permission `invoices.read` unless otherwise noted.

### GET /v1/admin/invoices?market=&from=&to=&status=&page=&pageSize=
List.

### GET /v1/admin/invoices/{id}
Full invoice + lines + credit notes (if any).

### GET /v1/admin/invoices/by-number/{invoiceNumber}
Shortcut search.

### POST /v1/admin/invoices/{id}/resend
Permission `invoices.resend`. Body `{ channel?: "email"|"whatsapp" }`. Fires spec 019.
- `404 invoice.not_found`, `409 invoice.not_rendered`.

### POST /v1/admin/invoices/{id}/regenerate
Permission `invoices.regenerate`. Re-renders PDF (same number, new SHA). Requires reason.
- Audit row required.

### GET /v1/admin/invoices/{id}/pdf
Returns the stored PDF stream (admin preview).

### GET /v1/admin/invoices/export?market=&from=&to=&format=csv
Streams CSV. Columns: `invoiceNumber, orderNumber, market, issuedAt, currency, subtotal, discount, tax, shipping, grandTotal, creditNoteNumbers, netAfterRefunds`.

### GET /v1/admin/invoices/render-queue
Stuck jobs inspector. Returns `[ { jobId, invoiceId, state, attempts, lastError, nextAttemptAt } ]`.

### POST /v1/admin/invoices/render-queue/{jobId}/retry
Force re-enqueue.

### Credit notes
- `GET /v1/admin/credit-notes?market=&from=&to=`
- `GET /v1/admin/credit-notes/{id}`
- `GET /v1/admin/credit-notes/{id}/pdf`

## Internal

Primary ingestion path for both endpoints below is the **event subscription** (spec 011 / spec 013 outbox). The HTTP endpoints exist for replay / admin recovery / integration test seeding. Either path hits the same handler.

### POST /v1/internal/invoices/issue-on-capture
Idempotent replay endpoint for the `payment.captured` subscriber (spec 011 outbox). Body: `{ orderId }`. Idempotent on `(orderId)`. COD orders emit `payment.captured` on delivery confirmation; bank-transfer orders emit it on `AdminConfirmBankTransfer` — one code path.

### POST /v1/internal/credit-notes/issue
Idempotent replay endpoint for the `refund.completed` / `refund.manual_confirmed` subscriber (spec 013 outbox). Body: `{ invoiceId, refundId, lines: [{ invoiceLineId, qty }], reasonCode }`. Idempotent on `(refundId)`.

## Reason codes
`invoice.not_found`, `invoice.not_rendered`, `invoice.render_pending`, `invoice.immutable`, `invoice.regenerate.denied`, `invoice.template.missing`, `invoice.zatca.qr_failed`, `credit_note.not_found`, `credit_note.line_exceeds_invoice`.

## Events (outbox → published)
`invoice.issued`, `invoice.regenerated`, `invoice.failed`, `credit_note.issued`.
