# Implementation Plan — Returns & Refunds v1 (Spec 013)

**Branch**: `phase-1B-specs` · **Date**: 2026-04-22

## Technical Context
- **Runtime**: .NET 9 / C# 12.
- **DB**: PostgreSQL 16; schema `returns`.
- **Module**: `services/backend_api/Modules/Returns/`.
- **Deps**: EF Core, FluentValidation, Azure.Storage.Blobs (photos), spec 010's `IPaymentGateway`.

## Constitution Check
| Principle | Gate | Note |
|---|---|---|
| 5 — Market-configurable | PASS | Return window + restocking per market. |
| 8 — Restricted | PASS | Zero-window override supported. |
| 13 — Payment abstraction | PASS | Refund via `IPaymentGateway.Refund`. |
| 17 — Refund as independent state | PASS | Own SM; advances spec 011's `refund_state`. |
| 18 — Invoices | PASS | Credit note via spec 012. |
| 21 — Operational readiness | PASS | Admin inspection + approval flows. |
| 22/23 | PASS | .NET + Postgres modular monolith. |
| 24 — State machines | PASS | `Return`, `Refund`, `Inspection`. |
| 25 — Audit | PASS | Every admin action logged. |
| 27 — UX quality | PASS | Timeline + states + notifications. |

**Gate**: PASS.

## Phase A — Primitives
- `Primitives/ReturnNumberSequencer.cs` — per `(market, yyyymm)`.
- `Primitives/ReturnStateMachine.cs` — 8-state machine (see data-model).
- `Primitives/RefundStateMachine.cs`.
- `Primitives/RefundAmountCalculator.cs` — pro-rata on original tax rate.
- `Primitives/ReturnPolicyEvaluator.cs` — market + per-product zero-window.

## Phase B — Persistence
- Tables: `return_requests`, `return_lines`, `inspections`, `inspection_lines`, `refunds`, `refund_lines`, `return_photos`, `return_policies`, `returns_outbox`.
- Migration `Returns_Initial`.
- Seed `return_policies` for KSA/EG.

## Phase C — Customer slices
- `Customer/SubmitReturn/*` (FR-001, FR-005, FR-024).
- `Customer/ListReturns/*`, `Customer/GetReturn/*` (FR-017).
- `Customer/UploadReturnPhoto/*` (FR-020).

## Phase D — Admin slices
- `Admin/ListReturns/*`, `GetReturn/*`.
- `Admin/Approve/*`, `Reject/*`, `ApprovePartial/*` (FR-006).
- `Admin/MarkReceived/*`, `RecordInspection/*`.
- `Admin/IssueRefund/*` — calls `IPaymentGateway.Refund` (FR-007, FR-022).
- `Admin/ForceRefund/*` — skip-physical path (FR-006).
- `Admin/ConfirmBankTransfer/*` — manual COD/fallback (FR-011, FR-023).
- `Admin/Export/*` — CSV (FR-016).

## Phase E — Integrations
- Event handler: on inspection → emit `inventory.return_movement` to spec 008 (FR-009).
- Event handler: on refund → emit `refund.completed` → spec 012 credit note + spec 011 `refund_state` advance (FR-008, FR-010).
- Notifications: event-only for now (spec 019 Phase 1D).

## Phase F — Workers
- `RefundRetryWorker` — retries `refund.gateway_failed` with backoff (FR-021).

## Phase G — Events + outbox
- `returns_outbox` with `return.submitted`, `return.approved`, `return.rejected`, `return.received`, `return.inspected`, `refund.completed`, `refund.failed`.

## Phase H — Testing
- Unit: ReturnStateMachine fuzz (SC-004).
- Unit: RefundAmountCalculator parameterized (SC-003).
- Unit: PolicyEvaluator (market + override).
- Integration: full happy-path E2E (submit → approve → receive → inspect → refund → credit note).
- Integration: COD manual bank transfer.
- Integration: over-refund guard (SC-006).
- Integration: idempotency (SC-005, SC-007).
- Contract per FR.

## Phase I — Polish
- AR editorial `returns.ar.icu` (SC-008).
- OpenAPI regen + fingerprint + DoD.

## Complexity tracking
| Item | Why | Mitigation |
|---|---|---|
| 3 state machines | Principle 24. | Each in its own file + fuzz test. |
| Restocking idempotency | Double-posting would skew inventory. | Dedup key `(return_id, line_id)`. |
| COD manual path | Real-world friction. | Clear admin UI + audit; separate sub-state. |

**Post-design re-check**: PASS.
