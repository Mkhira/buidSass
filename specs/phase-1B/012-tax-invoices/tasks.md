# Tasks — Tax Invoices v1 (Spec 012)

**Date**: 2026-04-22 · **FRs**: 20 · **SCs**: 8.

## Phase A — Primitives
- [ ] A1. `Primitives/InvoiceNumberSequencer.cs` (FR-002, SC-003).
- [ ] A2. `Primitives/CreditNoteNumberSequencer.cs`.
- [ ] A3. `Primitives/ZatcaQrTlvBuilder.cs` (FR-004, SC-002).
- [ ] A4. `Primitives/InvoiceTemplateResolver.cs`.

## Phase B — Persistence
- [ ] B1. `Infrastructure/InvoicesDbContext.cs` (7 entities).
- [ ] B2. Migration `TaxInvoices_Initial`.
- [ ] B3. Seed `invoice_templates` for KSA + EG.

## Phase C — Rendering
- [ ] C1. `Rendering/HtmlTemplateRenderer.cs` — Razor compile.
- [ ] C2. `Rendering/PdfExporter.cs` — spec 003 adapter.
- [ ] C3. `Rendering/ZatcaQrEmbedder.cs` (FR-004).
- [ ] C4. `Rendering/IInvoiceBlobStore.cs` + Azure + local fs impls (FR-011).
- [ ] C5. Razor templates AR/EN RTL-first (FR-003, Principle 4).

## Phase D — Issuance
- [ ] D1. `Internal/IssueOnCapture/*` — spec 011 event handler (FR-001).
- [ ] D2. `Internal/IssueCreditNote/*` — spec 013 event handler (FR-008, FR-009).
- [ ] D3. `Workers/InvoiceRenderWorker` — claim + retry (FR-013).

## Phase E — Customer slices
- [ ] E1. `Customer/GetInvoicePdf/*` (FR-006, FR-020).
- [ ] E2. `Customer/GetInvoiceMetadata/*`.

## Phase F — Admin slices
- [ ] F1. `Admin/ListInvoices/*` + `GetInvoice/*`.
- [ ] F2. `Admin/ResendInvoice/*` (FR-007, FR-015).
- [ ] F3. `Admin/PreviewInvoice/*`.
- [ ] F4. `Admin/RegenerateInvoice/*` — same number, new SHA, audit (FR-010 & FR-015).
- [ ] F5. `Admin/FinanceExport/*` — CSV (FR-014, SC-007).
- [ ] F6. `Admin/RenderQueue/*` — stuck-jobs inspector (FR-013).

## Phase G — Events + outbox
- [ ] G1. `invoices_outbox` dispatcher (FR-016).

## Phase H — Testing
- [ ] H1. Unit: invoice sequencer collision fuzz (SC-003).
- [ ] H2. Unit: ZATCA TLV encoder (SC-002).
- [ ] H3. Integration: `payment.captured` → invoice issued (SC-001).
- [ ] H4. Integration: refund → credit note (SC-005).
- [ ] H5. Property: byte-identity on re-fetch (SC-004).
- [ ] H6. Property: edit attempts all return 405 (SC-006).
- [ ] H7. Integration: finance CSV reconciles (SC-007).
- [ ] H8. Contract: per FR.

## Phase I — Polish
- [ ] I1. AR editorial pass on invoice strings (SC-008).
- [ ] I2. OpenAPI regen + fingerprint.
- [ ] I3. DoD per `docs/dod.md`.

---

## MVP definition
Phases A + B + C + D1/D3 + E1 + F1/F3/F5 + G + H1..H3/H5 + I2.
