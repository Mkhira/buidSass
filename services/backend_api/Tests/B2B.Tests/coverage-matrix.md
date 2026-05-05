# Spec 021 — DoD coverage matrix

Spec 021 task T150. Maps every functional requirement (FR-) and every Success
Criterion (SC-) from `spec.md` to the test that proves it. Used during the
DoD walkthrough.

## Functional requirements

| FR | Description | Test |
|---|---|---|
| FR-011 | Cross-market quote requests rejected with `quote.market_mismatch`. | `Contract/RequestQuoteFromCartContractTests` |
| FR-019 | Per-company PO uniqueness when `unique_po_required=true`. | `Contract/SubmitAcceptanceContractTests`, `Integration/PoSoftWarningFlowTests` |
| FR-024 | Last admin removal forbidden. | `Integration/CompanyAdministrationInvariantsTests.RemoveLastAdmin_*` |
| FR-025 | Last approver removal under `approver_required=true` forbidden. | `Integration/CompanyAdministrationInvariantsTests.RemoveLastApprover_*` |
| FR-026 | Suspended companies rejected on customer-facing actions. | `Contract/RequestQuoteFromCartContractTests`, `RejectAcceptanceHandler` (CodeRabbit Round 1) |
| FR-027 | Cross-market individual-customer flow uses `invoice_billing=false`. | `Integration/IndividualAcceptanceTests` |
| FR-030 | Last approver leaves → pending-approver quotes return to revised. | `MemberHandler.ApplyMembershipChange` (covered by FR-025 path) |
| FR-031 | `approver_required=true → false` while pending-approver quotes exist transitions them back to revised. | `Integration/CompanyAdministrationInvariantsTests.UpdateCompanyConfig_*` |
| FR-036 | Eligibility re-check at acceptance. | `Integration/AdminDetailVerificationWarningsTests`, `QuoteToOrderConverter` |
| FR-038 | `account_inactive` carry-over. | `Contract/RequestQuoteFromCartContractTests`, `Hooks/AccountLifecycleHandler` |
| FR-040 | Below-baseline reason required + audited. | `Integration/BelowBaselineAuditTests` |
| FR-041 / FR-042 | EN + AR error envelopes (token-stable). | `Integration/CustomerQuoteLocaleTests` |
| FR-043 | Subscriber failures don't roll back state writes. | `Hooks/AccountLifecycleHandler`, `QuoteExpiryWorker`, `InvitationExpiryWorker` |
| FR-045 | Per-customer + per-company hourly rate-limit. | `Integration/RateLimitEnforcementTests` |

## Success criteria

| SC | Description | Test |
|---|---|---|
| SC-001 | 5-day buyer round trip. | UI surface (Phase 1C); backend latency budgets in `Benchmarks/baselines.md` |
| SC-002 | 3-day individual round trip. | UI surface (Phase 1C); covered partially by `Integration/IndividualAcceptanceTests` |
| SC-004 | Below-baseline overrides audited. | `Integration/BelowBaselineAuditTests` |
| SC-005 | Audit replay reflects every state-touching action. | `scripts/audit-spot-check-b2b.sh` |
| SC-007 | Conversion rollback on order-system failure. | `Integration/ApproverFlowAndConversionTests.ConverterFailure_keeps_quote_in_pending_approver_state` |
| SC-009 | Multi-approver finalize race resolved by xmin. | `Integration/ApproverFlowAndConversionTests.FinalizeAcceptance_two_approvers_first_wins_second_sees_already_decided` |
| SC-010 | Rate-limit enforcement under burst. | `Integration/RateLimitEnforcementTests` |

## Phase 10 coverage

| Phase 10 task | Test |
|---|---|
| Workers — quote expiry | `Integration/QuoteExpiryWorkerTests` (4 tests) |
| Workers — invitation expiry | `Integration/InvitationExpiryWorkerTests` (5 tests) |
| Account-lifecycle hook | `Integration/AccountLifecycleHandlerTests` (4 tests) |
| Product-archived hook | `Integration/ProductArchivedHandlerTests` (4 tests) |
| Dev seeder | `Integration/B2BDevDataSeederTests` (3 tests) |

## Open follow-ups (intentional)

* **T097 TaxPreviewDrift test** — the converter reserves the drift-detection
  hook; production-binding tests follow when spec 011 surfaces the drift
  signal.
* **T147 AR editorial sweep** — Principle 4 hard-gate; awaits a native
  Arabic-speaking reviewer (Phase 1F). Current entries in
  `Modules/B2B/Messages/AR_EDITORIAL_REVIEW.md` remain `draft`.
* **T148 OpenAPI full schema** — the path surface map is canonical;
  per-endpoint schemas emit once spec 005 + spec 007-a producers are
  registered in production DI (`scripts/generate-openapi-b2b.sh`).
* **T151 latency baselines** — first-pass measurements pending observability
  hookup (spec 026); locked envelope documented in `Benchmarks/baselines.md`.
