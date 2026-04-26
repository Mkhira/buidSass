# Tasks — Returns & Refunds v1 (Spec 013)

**Date**: 2026-04-22 · **FRs**: 24 · **SCs**: 9 · **State machines**: 3.

## Phase A — Primitives
- [X] A1. `Primitives/ReturnNumberSequencer.cs` (FR-003).
- [X] A2. `Primitives/ReturnStateMachine.cs` (FR-004, SC-004).
- [X] A3. `Primitives/RefundStateMachine.cs`.
- [X] A4. `Primitives/InspectionStateMachine.cs`.
- [X] A5. `Primitives/RefundAmountCalculator.cs` (FR-014, SC-003).
- [X] A6. `Primitives/ReturnPolicyEvaluator.cs` — window + per-product override (FR-001, FR-002).

## Phase B — Persistence
- [X] B1. `Infrastructure/ReturnsDbContext.cs` (9 entities, plus `ReturnStateTransition` for trail).
- [X] B2. Migration `Returns_Initial`.
- [X] B3. Seed `return_policies` for KSA + EG (inside migration, idempotent).

## Phase C — Customer slices
- [X] C1. `Customer/UploadReturnPhoto/*` (FR-020).
- [X] C2. `Customer/SubmitReturn/*` (FR-001, FR-005, FR-024).
- [X] C3. `Customer/ListReturns/*` (FR-017).
- [X] C4. `Customer/GetReturn/*` + timeline (FR-013).

## Phase D — Admin review
- [X] D1. `Admin/ListReturns/*`, `GetReturn/*`.
- [X] D2. `Admin/Approve/*`, `Reject/*`, `ApprovePartial/*` (FR-006, FR-012, FR-019).

## Phase E — Admin warehouse
- [X] E1. `Admin/MarkReceived/*`.
- [X] E2. `Admin/RecordInspection/*` → emit inventory movement (FR-009, SC-007).

## Phase F — Admin refund
- [X] F1. `Admin/IssueRefund/*` — calls `IPaymentGateway.Refund` (FR-007).
- [X] F2. Over-refund guard (FR-022, SC-006).
- [X] F3. `Admin/ForceRefund/*` — skip-physical.
- [X] F4. `Admin/Refunds/ConfirmBankTransfer/*` (FR-011, FR-023).
- [X] F5. `Admin/Refunds/Retry/*`.
- [X] F6. `Admin/ReturnsExport/*` — CSV (FR-016).

## Phase G — Policy admin
- [X] G1. `Admin/ReturnPolicies/{Get,Put}/*` (FR-015, audit).

## Phase H — Integrations & events
- [X] H1. `returns_outbox` dispatcher. `Workers/ReturnsOutboxDispatcher.cs` + `ReturnsOutboxDispatchService.cs` (extracted to be testable).
- [X] H2. Event wiring → spec 012 credit note (FR-008, SC-009). `ICreditNoteIssuer` Shared seam + `CreditNoteIssuerAdapter` in spec 012.
- [X] H3. Event wiring → spec 011 `refund_state` advance (FR-010). `IOrderRefundStateAdvancer` Shared seam + `OrderRefundStateAdvancerAdapter` (and extracted `AdvanceRefundStateService` in spec 011) so the in-process call shares logic with the public HTTP endpoint.
- [X] H4. Event wiring → spec 008 inventory return movement (FR-009, SC-007). Reuses existing `IReservationConverter.PostReturnAsync`.

## Phase I — Workers
- [X] I1. `RefundRetryWorker` (FR-021).

## Phase J — Testing
- [X] J1. Unit: state-machine fuzz (SC-004). `Unit/ReturnStateMachineTests.cs` (10 k transitions, 0 illegal accepted).
- [X] J2. Unit: RefundAmountCalculator 1000-case parameterized (SC-003). Switched to pro-rate `OriginalTaxMinor` (matches spec 012's credit-note math) so SC-009 reconciles to 0.
- [X] J3. Integration: full happy path E2E (SC-001). `Integration/HappyPathTests.cs`.
- [X] J4. Integration: COD manual bank transfer path. `Integration/CodBankTransferPathTests.cs` (2 tests).
- [X] J5. Integration: over-refund guard (SC-006). `Integration/OverRefundGuardTests.cs`.
- [X] J6. Integration: idempotency — approve/issue-refund replay (SC-005). `Integration/IdempotencyTests.cs`.
- [X] J7. Integration: inspection idempotency (SC-007). `Integration/InspectionIdempotencyTests.cs` (5 calls = 1 inventory movement, 1 inspection row).
- [X] J8. Integration: credit-note reconciliation (SC-009). `Integration/CreditNoteReconciliationTests.cs` (drives the dispatcher; verifies refund amount == credit-note grand total).
- [X] J9. Contract per FR. `Integration/FrContractTests.cs` (8 tests covering FR-001/002/003/005/013/016/017/024 + permission gates).

## Phase K — Polish
- [X] K1. AR editorial `Resources/returns.ar.icu` (SC-008). 33 keys; matching `returns.en.icu`.
- [X] K2. OpenAPI regen + fingerprint. `openapi.returns.json` shipped at repo root; constitution fingerprint `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62` recorded in `dod-verification.md`.
- [X] K3. DoD verification per `docs/dod.md`. See `dod-verification.md` for the per-trigger checklist; CI-gated items (UC-2/3/7) noted as pending PR.

---

## MVP definition
Phases A + B + C + D + E + F1/F2/F4 + G + H + J1..J5 + K2.

## Test count
**61 / 61 passing** in `Tests/Returns.Tests/`:
- 45 unit (state machines, RefundAmountCalculator including SC-003 1000-case sweep, ReturnPolicyEvaluator, RefundStateMachine, InspectionStateMachine, ResolveTaxRateBp).
- 16 integration (happy path, COD bank transfer + IBAN guard, over-refund guard, idempotency, inspection idempotency, credit-note reconciliation, 8× FR contract sweep).

## Deferred / non-blocking notes
- The Azure Blob `IStorageService` adapter is shared with spec 003; production uses the real adapter; tests use `LocalDiskStorageService`.
- The pre-existing umbrella `Tests/backend_api.Tests.csproj` was updated to also exclude the new `Returns.Tests/` folder (matches the existing pattern for sibling test projects).
