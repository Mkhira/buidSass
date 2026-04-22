# Tasks — Returns & Refunds v1 (Spec 013)

**Date**: 2026-04-22 · **FRs**: 24 · **SCs**: 9 · **State machines**: 3.

## Phase A — Primitives
- [ ] A1. `Primitives/ReturnNumberSequencer.cs` (FR-003).
- [ ] A2. `Primitives/ReturnStateMachine.cs` (FR-004, SC-004).
- [ ] A3. `Primitives/RefundStateMachine.cs`.
- [ ] A4. `Primitives/InspectionStateMachine.cs`.
- [ ] A5. `Primitives/RefundAmountCalculator.cs` (FR-014, SC-003).
- [ ] A6. `Primitives/ReturnPolicyEvaluator.cs` — window + per-product override (FR-001, FR-002).

## Phase B — Persistence
- [ ] B1. `Infrastructure/ReturnsDbContext.cs` (9 entities).
- [ ] B2. Migration `Returns_Initial`.
- [ ] B3. Seed `return_policies` for KSA + EG.

## Phase C — Customer slices
- [ ] C1. `Customer/UploadReturnPhoto/*` (FR-020).
- [ ] C2. `Customer/SubmitReturn/*` (FR-001, FR-005, FR-024).
- [ ] C3. `Customer/ListReturns/*` (FR-017).
- [ ] C4. `Customer/GetReturn/*` + timeline (FR-013).

## Phase D — Admin review
- [ ] D1. `Admin/ListReturns/*`, `GetReturn/*`.
- [ ] D2. `Admin/Approve/*`, `Reject/*`, `ApprovePartial/*` (FR-006, FR-012, FR-019).

## Phase E — Admin warehouse
- [ ] E1. `Admin/MarkReceived/*`.
- [ ] E2. `Admin/RecordInspection/*` → emit inventory movement (FR-009, SC-007).

## Phase F — Admin refund
- [ ] F1. `Admin/IssueRefund/*` — calls `IPaymentGateway.Refund` (FR-007).
- [ ] F2. Over-refund guard (FR-022, SC-006).
- [ ] F3. `Admin/ForceRefund/*` — skip-physical.
- [ ] F4. `Admin/Refunds/ConfirmBankTransfer/*` (FR-011, FR-023).
- [ ] F5. `Admin/Refunds/Retry/*`.
- [ ] F6. `Admin/ReturnsExport/*` — CSV (FR-016).

## Phase G — Policy admin
- [ ] G1. `Admin/ReturnPolicies/{Get,Put}/*` (FR-015, audit).

## Phase H — Integrations & events
- [ ] H1. `returns_outbox` dispatcher.
- [ ] H2. Event wiring → spec 012 credit note (FR-008, SC-009).
- [ ] H3. Event wiring → spec 011 `refund_state` advance (FR-010).
- [ ] H4. Event wiring → spec 008 inventory return movement (FR-009, SC-007).

## Phase I — Workers
- [ ] I1. `RefundRetryWorker` (FR-021).

## Phase J — Testing
- [ ] J1. Unit: state-machine fuzz (SC-004).
- [ ] J2. Unit: RefundAmountCalculator 1000-case parameterized (SC-003).
- [ ] J3. Integration: full happy path E2E (SC-001).
- [ ] J4. Integration: COD manual bank transfer path.
- [ ] J5. Integration: over-refund guard (SC-006).
- [ ] J6. Integration: idempotency — approve/issue-refund replay (SC-005).
- [ ] J7. Integration: inspection idempotency (SC-007).
- [ ] J8. Integration: credit-note reconciliation (SC-009).
- [ ] J9. Contract per FR.

## Phase K — Polish
- [ ] K1. AR editorial `Resources/returns.ar.icu` (SC-008).
- [ ] K2. OpenAPI regen + fingerprint.
- [ ] K3. DoD verification per `docs/dod.md`.

---

## MVP definition
Phases A + B + C + D + E + F1/F2/F4 + G + H + J1..J5 + K2.
