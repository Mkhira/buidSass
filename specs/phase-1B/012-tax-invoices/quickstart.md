# Quickstart — Tax Invoices v1 (Spec 012)

**Date**: 2026-04-22 · **Target**: backend developer / AI agent, 30 minutes.

## Prerequisites
- Specs 003, 007-a, 011 at DoD; A1 compose up (Postgres, Blob Storage emulator / Azurite).
- Migration `TaxInvoices_Initial` applied.
- Seed `invoices.invoice_templates` for KSA + EG.

## 30-minute walk-through

1. **Seed a captured KSA order** via spec 011 seed: B2C, 2 lines, VAT 15 %.
2. **Trigger issuance**: the `payment.captured` event handler inserts an `invoices` row with `state=pending` and enqueues a render job.
3. **Wait** ≤ 5 s; `InvoiceRenderWorker` picks the job, renders HTML → PDF, embeds ZATCA QR, uploads to blob storage, sets `state=rendered`.
4. **Fetch** `GET /v1/customer/orders/{orderId}/invoice.pdf` — 200 OK with `application/pdf` stream. Save locally.
5. **Decode QR** (using any TLV base64 decoder) → verify seller name (AR), VAT number, timestamp, totals.
6. **Re-fetch** the same endpoint → SHA-256 of bytes matches first fetch (SC-004).
7. **Seed a B2B KSA order** (company account with VAT number + PO). Issue flow runs. PDF header shows company name / VAT / PO.
8. **Trigger refund** via spec 013: partial refund of line #1 qty 1. Credit note issues; `GET /v1/admin/credit-notes/{id}/pdf` shows a negative line referencing the original invoice.
9. **Admin resend**: `POST /v1/admin/invoices/{id}/resend` → spec 019 notification (mocked in dev) fires; same PDF bytes attached.
10. **Finance export**: `GET /v1/admin/invoices/export?market=KSA&from=2026-04-01&to=2026-04-30&format=csv`; totals reconcile to `invoices - credit_notes`.

## Definition of Done
- ≥ 1 contract test per FR-001..FR-020.
- All 8 SCs wired to tests.
- ZATCA QR verified against official validator (SC-002).
- PDF byte-identity test (SC-004).
- AR editorial pass on invoice template.
- OpenAPI regen + fingerprint + `docs/dod.md` green.
