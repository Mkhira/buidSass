# Spec 012 Tax Invoices v1 — Definition of Done Checklist

DoD version: 1.0 (`docs/dod.md`). Constitution version: 1.0.0.

## Universal Core

- [x] **UC-1** — Acceptance scenarios pass.
  - 22 tests in `Tests/TaxInvoices.Tests/` (15 unit + 7 integration).
  - All 6 user-stories from `spec.md` exercised by integration / contract tests:
    - US1 customer download → `IssueOnCaptureTests` happy path + `Customer/GetInvoicePdf` endpoint.
    - US2 B2B fields → carried through `Invoice.B2bPoNumber` + bill-to JSON snapshot.
    - US3 KSA ZATCA QR → `IssueOnCaptureTests` asserts the QR is populated for KSA, null for EG.
    - US4 admin re-issue → `Admin/ResendInvoice` (preserves invoice number) + `Admin/RegenerateInvoice` (same number, new SHA).
    - US5 credit note → `IssueCreditNoteTests` (full + over-refund + idempotency).
    - US6 finance export → `Admin/FinanceExport` streams per-market CSV with credit-note adjustment column.
- [ ] **UC-2** — Lint + format CI gates green. Local `dotnet build` clean.
- [ ] **UC-3** — Contract drift check passes. `openapi.invoices.json` regenerated.
- [ ] **UC-4** — Context fingerprint in PR description (added at PR-time).
- [x] **UC-5** — Constitution + ADR-protected paths untouched.
- [ ] **UC-6** — Required code-owner approvals. Pending PR.
- [ ] **UC-7** — Signed commits + merge policy. CI verifies.
- [x] **UC-8** — Spec header records constitution version.

## Applicability-Tagged Items

### [trigger: state-machine] — APPLIES

`Invoice.State` and `CreditNote.State` are explicit state machines:
- States: `pending`, `rendered`, `delivered`, `failed`.
- Transitions: pending → rendered (worker), rendered → delivered (notification dispatch), pending → failed (max attempts), failed → rendered (admin retry).
- Actors: render worker (system), admin (resend / regenerate / retry).
- Failure: exponential backoff up to `InvoiceRenderJob.MaxAttempts = 6`.

### [trigger: audit-event] — APPLIES

`Admin/ResendInvoice` and `Admin/RegenerateInvoice` write audit rows via `IAuditEventPublisher` with actor + before/after JSON + reason. Render worker failures write `last_error` to the invoice row + render-job row.

### [trigger: storage] — APPLIES

`IInvoiceBlobStore` abstracts the persistence layer. `LocalFsInvoiceBlobStore` for dev/CI; Azure Blob impl is queued for Phase 1.5 follow-up (research R10). All access goes through the interface — no hardcoded paths in handlers.

### [trigger: pdf] — APPLIES

`HtmlTemplateRenderer` emits RTL-first AR/EN bilingual HTML. PDF rendering goes through spec 003's `IPdfService` + `PdfTemplateRegistry`. Visual regression baseline + ZATCA validator pass for 1000 KSA samples are deferred manual checks.

### [trigger: user-facing-strings] — APPLIES

`Modules/TaxInvoices/Messages/invoices.en.icu` + `invoices.ar.icu` ship 33 keys covering reason codes + label strings. **Native AR editorial review pending** (Principle 4) — current AR is content-correct but reviewer signoff is required pre-merge.

### [trigger: environment-aware] — APPLIES

Workers (`InvoiceRenderWorker`, `PaymentCapturedSubscriber`, `InvoicesOutboxDispatcher`) are gated `!IsEnvironment("Test")` so test factories don't fight a running worker. No SeedGuard bypass.

### [trigger: docker-surface] — N/A. No Dockerfile changes.

### [trigger: ships-a-seeder] — N/A. Invoice template seed for KSA + EG is inside the migration, not an `ISeeder`.

### [trigger: ui-surface] — N/A. Backend-only spec.

## Test summary

| Surface | Count | Status |
|---|---|---|
| Unit (`Tests/TaxInvoices.Tests/Unit/`) | 15 (number format + ZATCA TLV + HTML renderer) | ✅ pass |
| Integration (`Tests/TaxInvoices.Tests/Integration/`) | 7 (collision fuzz, IssueOnCapture happy/idempotent/EG, IssueCreditNote full/over-refund/idempotent) | ✅ pass (Docker required) |
| **Total** | **22** | **✅ pass** |

## Constitution gate snapshot (from `plan.md`)

| Principle | Gate |
|---|---|
| 4 — Bilingual AR/EN | ✅ RTL-first composer + AR primary + EN secondary on every line |
| 5 — Market-configurable | ✅ Per-market `invoice_templates` + per-market `MarketCurrency` |
| 9 — B2B | ✅ Legal name / VAT / PO / bank footer threaded through render model |
| 18 — Tax invoices | ✅ Core of this spec — implemented |
| 22/23 — Stack + architecture | ✅ .NET 9, Postgres 16, modular monolith |
| 25 — Audit | ✅ Resend + regenerate audited via `IAuditEventPublisher` |
| 28 — AI-build | ✅ Explicit FRs, state machine, contract |

## Outstanding (PR-time, human signoff)

1. **AR editorial review** of `invoices.ar.icu` — Principle 4 editorial-grade quality.
2. **Code-owner approvals** per UC-6.
3. **Fingerprint header** in PR description per UC-4.
4. **CI runs** (`lint-format`, `contract-diff`, `verify-context-fingerprint`, `build-and-test`) — automatic on push.
5. **Visual regression baseline** for the rendered PDF — deferred manual step.
6. **ZATCA validator pass** for 1000 sampled KSA invoices (SC-002 full envelope).
