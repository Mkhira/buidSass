# Tasks — Tax Invoices v1 (Spec 012)

**Date**: 2026-04-22 · **FRs**: 20 · **SCs**: 8.

> Status: spec 012 shipped in PR #33 (merged 2026-04-25). All artefacts present in
> `services/backend_api/Modules/TaxInvoices/`. Boxes ticked retroactively during spec 013 audit.
> Spec 013 added `Internal/IssueCreditNote/CreditNoteIssuerAdapter.cs` to expose the
> in-process `ICreditNoteIssuer` seam.

## Phase A — Primitives
- [X] A1. `Primitives/InvoiceNumberSequencer.cs` (FR-002, SC-003).
- [X] A2. `Primitives/CreditNoteNumberSequencer.cs`.
- [X] A3. `Primitives/ZatcaQrTlvBuilder.cs` (FR-004, SC-002).
- [X] A4. `Primitives/InvoiceTemplateResolver.cs`.

## Phase B — Persistence
- [X] B1. `Infrastructure/InvoicesDbContext.cs` (7 entities).
- [X] B2. Migration `TaxInvoices_Initial`. Plus `_DeepReviewFixes`.
- [X] B3. Seed `invoice_templates` for KSA + EG (in migration).

## Phase C — Rendering
- [X] C1. `Rendering/HtmlTemplateRenderer.cs` — Razor compile.
- [X] C2. `Rendering/PdfExporter.cs` — spec 003 adapter.
- [X] C3. `Rendering/ZatcaQrEmbedder.cs` (FR-004).
- [X] C4. `Rendering/IInvoiceBlobStore.cs` + Azure + local fs impls (FR-011). `LocalFsInvoiceBlobStore` for dev/test/staging; production fail-fast guard until Azure adapter wired.
- [X] C5. Razor templates AR/EN RTL-first (FR-003, Principle 4).

## Phase D — Issuance
- [X] D1. `Internal/IssueOnCapture/*` — spec 011 event handler (FR-001).
- [X] D2. `Internal/IssueCreditNote/*` — spec 013 event handler (FR-008, FR-009). Adapter `CreditNoteIssuerAdapter.cs` added during spec 013 to satisfy the in-process `ICreditNoteIssuer` seam.
- [X] D3. `Workers/InvoiceRenderWorker` — claim + retry (FR-013).

## Phase E — Customer slices
- [X] E1. `Customer/GetInvoicePdf/*` (FR-006, FR-020).
- [X] E2. `Customer/GetInvoiceMetadata/*`.

## Phase F — Admin slices
- [X] F1. `Admin/ListInvoices/*` + `GetInvoice/*` + `GetByNumber/*`.
- [X] F2. `Admin/ResendInvoice/*` (FR-007, FR-015).
- [X] F3. `Admin/PreviewInvoice/*`.
- [X] F4. `Admin/RegenerateInvoice/*` — same number, new SHA, audit (FR-010 & FR-015).
- [X] F5. `Admin/FinanceExport/*` — CSV (FR-014, SC-007).
- [X] F6. `Admin/RenderQueue/*` — stuck-jobs inspector (FR-013). `ListEndpoint.cs` + `RetryEndpoint.cs`.

## Phase G — Events + outbox
- [X] G1. `invoices_outbox` dispatcher (FR-016). `Workers/InvoicesOutboxDispatcher.cs` + `PaymentCapturedSubscriber.cs`.

## Phase H — Testing
- [X] H1. Unit: invoice sequencer collision fuzz (SC-003). `Integration/InvoiceNumberCollisionTests.cs` + `Unit/InvoiceNumberFormatTests.cs`.
- [X] H2. Unit: ZATCA TLV encoder (SC-002). `Unit/ZatcaQrTlvBuilderTests.cs`.
- [X] H3. Integration: `payment.captured` → invoice issued (SC-001). `Integration/IssueOnCaptureTests.cs`.
- [X] H4. Integration: refund → credit note (SC-005). `Integration/IssueCreditNoteTests.cs`.
- [X] H5. Property: byte-identity on re-fetch (SC-004). `Integration/PdfByteIdentityTests.cs`.
- [X] H6. Property: edit attempts all return 405 (SC-006). Covered in `Integration/DeepReviewFixesTests.cs`.
- [X] H7. Integration: finance CSV reconciles (SC-007). `Integration/CodeRabbitRound{1,2}Tests.cs` + `Integration/CustomerInvoicePdfTests.cs`.
- [X] H8. Contract: per FR. Covered across the integration suite.

## Phase I — Polish
- [X] I1. AR editorial pass on invoice strings (SC-008). `Modules/TaxInvoices/Messages/invoices.{ar,en}.icu`.
- [X] I2. OpenAPI regen + fingerprint. `openapi.invoices.json` shipped at repo root.
- [X] I3. DoD per `docs/dod.md` (PR #33 was DoD-green at merge).

---

## MVP definition
Phases A + B + C + D1/D3 + E1 + F1/F3/F5 + G + H1..H3/H5 + I2.
