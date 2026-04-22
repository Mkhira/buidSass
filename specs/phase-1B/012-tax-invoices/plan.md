# Implementation Plan — Tax Invoices v1 (Spec 012)

**Branch**: `phase-1B-specs` · **Date**: 2026-04-22

## Technical Context
- **Runtime**: .NET 9 / C# 12.
- **DB**: PostgreSQL 16; schema `invoices`.
- **Module**: `services/backend_api/Modules/TaxInvoices/`.
- **Deps**: EF Core, QuestPDF (HTML→PDF via spec 003 abstraction), System.Security.Cryptography (ZATCA TLV), Azure.Storage.Blobs.

## Constitution Check
| Principle | Gate | Note |
|---|---|---|
| 4 — Bilingual AR/EN | PASS | RTL-first PDF; AR editorial required. |
| 5 — Market-configurable | PASS | Templates + bank details + tax label per market. |
| 9 — B2B | PASS | Legal name / VAT / PO / bank footer. |
| 18 — Tax invoices | PASS | Core of this spec. |
| 22/23 | PASS | .NET + Postgres; modular monolith. |
| 25 — Audit | PASS | Admin reissue/regenerate audited. |
| 28 — AI-build | PASS | Explicit fields + FRs. |

**Gate**: PASS.

## Phase A — Primitives
- `Primitives/InvoiceNumberSequencer.cs` — `(market, yyyymm)` sequence.
- `Primitives/ZatcaQrTlvBuilder.cs` — TLV base64 encoder.
- `Primitives/InvoiceTemplateResolver.cs` — per-market + locale.
- `Primitives/CreditNoteNumberSequencer.cs`.

## Phase B — Persistence
- Tables: `invoices`, `invoice_lines`, `credit_notes`, `credit_note_lines`, `invoice_render_jobs`, `invoice_templates`, `invoices_outbox`.
- Migration `TaxInvoices_Initial`.

## Phase C — Rendering
- `Rendering/HtmlTemplateRenderer.cs` — Razor-based HTML composer.
- `Rendering/PdfExporter.cs` — wraps spec 003's PDF abstraction.
- `Rendering/ZatcaQrEmbedder.cs` — stamps QR onto KSA PDFs.
- `Rendering/StorageUploader.cs` — blob put with idempotent key `(invoiceId, kind)`.

## Phase D — Issuance
- `Internal/IssueOnCapture/*` — event handler for `payment.captured` (spec 011).
- `Internal/IssueCreditNote/*` — event handler for spec 013 refund.
- `Workers/InvoiceRenderWorker` — claims `invoice_render_jobs`, retries with backoff.

## Phase E — Customer slices
- `Customer/GetInvoicePdf/*` — streams stored blob.

## Phase F — Admin slices
- `Admin/ListInvoices/*`, `GetInvoice/*`.
- `Admin/ResendInvoice/*` — triggers spec 019 notification.
- `Admin/PreviewInvoice/*`.
- `Admin/FinanceExport/*` — CSV.
- `Admin/StuckJobs/*` — render job queue inspector.

## Phase G — Events + outbox
- `invoices_outbox` with `invoice.issued`, `invoice.regenerated`, `credit_note.issued`.

## Phase H — Testing
- Unit: number sequencer, ZATCA TLV (SC-002).
- Integration: payment.captured → invoice issued (SC-001).
- Integration: refund → credit note (SC-005).
- Contract: per FR.
- Property: byte-identity on re-fetch (SC-004).

## Phase I — Polish
- AR editorial invoice strings.
- OpenAPI regen + fingerprint + DoD.

## Complexity tracking
| Item | Why | Mitigation |
|---|---|---|
| ZATCA Phase 1 QR | KSA regulatory. | Small TLV builder; isolate in one file; CI test against validator sample. |
| PDF rendering queue | Rendering can be slow. | Async job w/ retry; customers see "available in a moment". |
| Bilingual AR/EN layout | Principle 4. | Template designed RTL-first; English columns always right-aligned. |

**Post-design re-check**: PASS.
