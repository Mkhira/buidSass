# Research — Tax Invoices v1 (Spec 012)

**Date**: 2026-04-22

## R1 — Issuance trigger
**Decision**: Event-driven — subscribe to `payment.captured` from spec 011's outbox. Additional trigger for bank-transfer confirm + COD delivery capture (both already emit `payment.captured`).
**Rationale**: One code path; no polling.
**Alternative**: Synchronous call from order flow — rejected because PDF rendering can be slow and should not block order finalization.

## R2 — Numbering scheme
**Decision**: `INV-{MARKET}-{YYYYMM}-{SEQ6}` per-market monthly sequence; credit notes `CN-{MARKET}-{YYYYMM}-{SEQ6}`.
**Rationale**: Mirrors order number (spec 011) — finance ops friendly.
**Alternative**: Global counter — rejected; per-market is the KSA/EG convention.

## R3 — PDF engine
**Decision**: Render HTML via Razor → PDF via spec 003's PDF abstraction (QuestPDF wrapper).
**Rationale**: Keeps spec 003 as the single owner of the PDF library choice; AR/RTL support is baked in.
**Alternative**: WeasyPrint (Python) — would require a sidecar; rejected.

## R4 — ZATCA Phase 1 QR
**Decision**: Hand-rolled TLV encoder + base64. Fields (in order): seller name (AR), VAT number, timestamp (ISO 8601), invoice total (VAT-inclusive), VAT total. B2B additional: buyer VAT number.
**Rationale**: Small scope; no SDK needed for Phase 1. Phase 2 (clearance) will use ZATCA SDK.
**Alternative**: SDK from day one — too much dependency overhead for Phase 1B.

## R5 — Immutable PDFs
**Decision**: Store rendered PDF in Azure Blob Storage under path `invoices/{market}/{yyyymm}/{invoice_number}.pdf`. Object SHA-256 recorded in DB for byte-identity verification.
**Rationale**: Compliance requires reproducible artifacts for 7 years.
**Alternative**: Re-render on demand — rejected; violates immutability + slower.

## R6 — Template strategy
**Decision**: Razor templates compiled at build time; per-market strings in resource bundles; bank details in `invoice_templates` DB row.
**Rationale**: Fast compile + per-market config without code deploy.

## R7 — Credit notes
**Decision**: Separate tables `credit_notes` + `credit_note_lines`; reference original invoice by number + id. Tax rate is always the original invoice's rate.
**Rationale**: Audit clarity; original invoice is never mutated.

## R8 — Bilingual layout
**Decision**: RTL-first page with AR primary + EN secondary on each line; totals block shows both languages; numbers use ASCII digits for tax authority parseability.
**Rationale**: Principle 4 + common practice in KSA/EG accounting.

## R9 — Rendering queue
**Decision**: `invoice_render_jobs` table with states `queued|rendering|done|failed`; worker claims with `FOR UPDATE SKIP LOCKED`; exponential backoff up to 6 attempts.
**Rationale**: PDF rendering failures must not stall the order pipeline.

## R10 — Storage abstraction
**Decision**: Interface `IInvoiceBlobStore` with an Azure Blob implementation; local-dev uses filesystem implementation.
**Rationale**: Keeps tests hermetic; Principle 22 (fixed tech stack) respected (Azure is our cloud per ADR-010).

## R11 — Customer fetch
**Decision**: `GET /v1/customer/orders/{id}/invoice.pdf` proxies through the API (not a signed direct URL at launch) so that access control is enforced at handler level.
**Rationale**: Simpler auth; direct-URL optimization deferred to Phase 1.5.

## R12 — EG ETA
**Decision**: Out of scope. EG invoices are internal only for Phase 1B; schema reserves columns for future integration.
**Rationale**: ETA integration has a long approval cycle; does not gate launch.
