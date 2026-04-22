# Feature Specification: Tax Invoices (v1)

**Feature Number**: `012-tax-invoices`
**Phase Assignment**: Phase 1B · Milestone 4 · Lane A (backend)
**Created**: 2026-04-22
**Input**: constitution Principles 4, 5, 9, 18, 22, 23, 25, 28, 29.

---

## Clarifications

### Session 2026-04-22

- Q1: Invoice issuance trigger? → **A: On `payment.captured`** from spec 011; for COD orders that means on delivery confirmation. B2B bank transfer → on `AdminConfirmBankTransfer`.
- Q2: Numbering scheme? → **A: Per-market, monthly sequence**: `INV-{MARKET}-{YYYYMM}-{SEQ6}` (e.g. `INV-KSA-202604-000187`). Credit notes use `CN-{MARKET}-{YYYYMM}-{SEQ6}`.
- Q3: PDF rendering? → **A: Server-rendered bilingual AR/EN PDF** using the HTML → PDF abstraction from spec 003. Layout is RTL-first; English block appears on the right side of each row.
- Q4: KSA ZATCA integration? → **A: Phase 1B produces ZATCA-compliant Phase 1 (e-invoicing) XML + QR code** (TLV base64 with seller name, VAT number, timestamp, total, VAT total). Phase 2 integration (clearance API) is Phase 1.5.
- Q5: EG ETA (Egyptian Tax Authority)? → **A: Out of scope for Phase 1B**; EG invoices are internal documents only. Hook left for Phase 1.5.
- Q6: Credit notes? → **A: Required.** Refunds (spec 013) issue a credit note that references the original invoice; partial refunds produce a credit note covering only the refunded lines.
- Q7: Stored vs re-rendered? → **A: Stored.** Once issued, the invoice PDF + XML are immutable and stored in object storage; re-fetch returns the same bytes.

---

## User Scenarios & Testing

### User Story 1 — Customer downloads their invoice (P1)
After payment capture, customer sees an "Invoice" link in the order detail and downloads the bilingual PDF.

**Acceptance Scenarios**:
1. *Given* an order with `payment.state=captured`, *when* customer opens order detail, *then* `invoiceUrl` is populated and clicking downloads a PDF.
2. *Given* an order with `payment.state=authorized` (not captured), *then* `invoiceUrl` is null; UI shows "Invoice available after payment capture".
3. *Given* the PDF was already issued, *then* the same bytes (SHA-256 match) are returned every time.

---

### User Story 2 — B2B invoice with PO + company details (P1)
B2B buyer's invoice shows company legal name, VAT number, PO number, and payment terms.

**Acceptance Scenarios**:
1. *Given* a B2B order with `accountType=company`, *then* invoice `billTo` block uses `companyLegalName`, `companyVatNumber`, `companyAddress`.
2. *Given* `b2b_po_number` set on the order, *then* invoice header shows the PO.
3. *Given* payment method is bank transfer, *then* footer shows bank details + IBAN (pulled from market config).

---

### User Story 3 — KSA ZATCA Phase 1 QR code (P1)
KSA invoices embed a ZATCA-compliant TLV QR code.

**Acceptance Scenarios**:
1. *Given* a KSA invoice issued, *when* decoded, *then* the QR TLV carries seller name, VAT number, ISO-8601 timestamp, grand total (VAT-inclusive), VAT total — all in order.
2. *Given* a B2B KSA invoice, *then* the buyer VAT number is also included (Phase 1 additional fields).
3. *Given* an EG invoice, *then* no QR required; the slot is empty.

---

### User Story 4 — Admin re-issues a lost invoice (P2)
Admin force-reissues an invoice (e.g. buyer lost the email).

**Acceptance Scenarios**:
1. *Given* an already-issued invoice, *when* admin triggers `Resend`, *then* a new notification is sent with the SAME invoice number/PDF (never a new number).
2. *Given* admin clicks `Preview`, *then* the PDF opens inline.

---

### User Story 5 — Credit note on refund (P1)
A refund from spec 013 produces a credit note referencing the original invoice.

**Acceptance Scenarios**:
1. *Given* a partial refund of order line #2, *when* the credit note is issued, *then* it shows only line #2 with the refunded quantity + original tax rate and references `INV-...-000187`.
2. *Given* a full refund, *then* the credit note totals equal the invoice totals (negative).
3. *Given* a credit note is issued, *then* the original invoice is NOT altered (immutable per Q7).

---

### User Story 6 — Finance export with tax breakdown (P2)
Finance downloads a CSV with per-market VAT totals for a date range.

**Acceptance Scenarios**:
1. *Given* 1000 invoices in the range, *when* CSV export is run, *then* per-line VAT + per-invoice totals + credit-note adjustments are included.
2. *Given* EG + KSA mixed, *then* rows are grouped by market.

---

### Edge Cases
1. Payment captured but invoice rendering fails (PDF library error) → issuance job retries with exponential backoff; error surfaced to admin via ops dashboard.
2. Credit note on an order with a tax rate that changed since invoice → credit note uses ORIGINAL rate from stored explanation.
3. Multi-shipment order → still ONE invoice per order (shipments don't issue separate invoices in Phase 1B).
4. Customer invoice language preference differs from order-time market? → invoice is bilingual anyway; email copy uses current preference.
5. Admin attempts to edit an issued invoice → 405 `invoice.immutable`; must issue a credit note + re-issue as new invoice in rare cases (admin-only, audited).
6. Storage outage → PDF fetch returns 503 with retry-after; re-upload job backlogged.
7. ZATCA QR generation fails for KSA → issuance blocked; ops alert; invoice not released.
8. Year rollover crosses midnight mid-issuance → sequence reset cleanly because scope is `(market, yyyymm)`.

---

## Requirements (FR-)
- **FR-001**: System MUST issue an invoice automatically on `payment.captured` for every market.
- **FR-002**: Invoice number MUST follow `INV-{MARKET}-{YYYYMM}-{SEQ6}`; credit note number `CN-{MARKET}-{YYYYMM}-{SEQ6}`.
- **FR-003**: System MUST render a bilingual AR/EN PDF (RTL-first) stored in object storage.
- **FR-004**: KSA invoices MUST embed a ZATCA Phase 1 QR code (TLV, base64).
- **FR-005**: Invoices MUST pull pricing explanation data (spec 007-a) — NOT recompute taxes.
- **FR-006**: System MUST expose `GET /v1/customer/orders/{id}/invoice.pdf` returning stored bytes.
- **FR-007**: System MUST expose admin endpoints: list, detail, resend, preview, reissue-after-credit-note.
- **FR-008**: System MUST support credit notes triggered by spec 013 refund.
- **FR-009**: Credit notes MUST reference the original invoice by number + id.
- **FR-010**: Invoices MUST be immutable once issued (no edit endpoint).
- **FR-011**: System MUST store PDF + XML blobs in object storage with `(invoice_id, kind)` key.
- **FR-012**: System MUST support B2B fields: company legal name, VAT number, PO number.
- **FR-013**: System MUST handle rendering failures via a retry queue; surface stuck jobs in admin.
- **FR-014**: System MUST expose a finance CSV export (`GET /v1/admin/invoices/export?…`).
- **FR-015**: System MUST audit admin mutations (reissue, force-regenerate) per Principle 25.
- **FR-016**: System MUST emit `invoice.issued`, `invoice.regenerated`, `credit_note.issued` events.
- **FR-017**: Invoice templates MUST be market-configurable (legal footer, tax labels, bank details).
- **FR-018**: System MUST allow admin to download invoice by `invoiceNumber` (search shortcut).
- **FR-019**: PDF rendering MUST complete in < 3 s p95 (SC-001).
- **FR-020**: Customer MUST only fetch invoices for their own orders (FR-020 of spec 011 applies).

### Key Entities
- **Invoice** / **InvoiceLine** — the doc.
- **CreditNote** / **CreditNoteLine**.
- **InvoiceRenderJob** — async renderer.
- **InvoiceTemplate** — per-market + locale overrides.

---

## Success Criteria (SC-)
- **SC-001**: PDF render p95 < 3 s; p99 < 8 s.
- **SC-002**: ZATCA QR TLV validates against the official validator for 1000 sampled KSA invoices.
- **SC-003**: Invoice number uniqueness: 10 000 concurrent captures × 2 markets → 0 collisions.
- **SC-004**: PDF byte-identity on re-fetch (SHA-256 match 100/100).
- **SC-005**: Credit note reconciles to 0 net VAT for full refunds.
- **SC-006**: Immutable invariant: all admin edit attempts return 405 (pen test).
- **SC-007**: Finance CSV export numerically matches sum of invoices – credit notes for a period.
- **SC-008**: AR editorial pass by native speaker.

---

## Dependencies
- Spec 003 (audit + PDF abstraction + storage).
- Spec 007-a (pricing explanation as the source of truth for tax numbers).
- Spec 011 (trigger on `payment.captured`).
- Spec 013 (credit note trigger on refund).
- Spec 019 (notifications — email with invoice attached; Phase 1D).

## Assumptions
- Object storage is provided by infra A1 (Azure Blob Storage).
- ZATCA Phase 2 clearance is Phase 1.5 work.
- Invoice templates are compiled at build time; runtime takes locale + market overrides only.

## Out of Scope
- ZATCA Phase 2 clearance API — Phase 1.5.
- EG ETA integration — Phase 1.5.
- Recurring invoices — Phase 2.
- Invoice financing / early payment discounts — Phase 2.
