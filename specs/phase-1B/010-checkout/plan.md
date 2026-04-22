# Implementation Plan — Checkout v1 (Spec 010)

**Branch**: `phase-1B-specs` · **Date**: 2026-04-22

## Technical Context
- **Runtime**: .NET 9 / C# 12.
- **DB**: PostgreSQL 16; schema `checkout`.
- **Module**: `services/backend_api/Modules/Checkout/`.
- **Deps**: EF Core, FluentValidation, `Polly` (retry/circuit for provider calls), `Microsoft.AspNetCore.Http.Connections` (webhooks).

## Constitution Check
| Principle | Gate | Note |
|---|---|---|
| 3 — Browse w/o auth | PASS | Guest may fill session; only `submit` forces auth. |
| 5 — Market-configurable | PASS | Payment methods + COD eligibility keyed by market. |
| 8 — Restricted visibility | PASS | Restriction gate re-checked at submit (FR-009); never hides product. |
| 9 — B2B | PASS | Bank transfer + PO path. |
| 10 — Pricing central | PASS | Issue mode authoritative at submit. |
| 11 — Inventory | PASS | Reservations locked at submit; converted on confirm. |
| 13 — Payment abstraction | PASS | `IPaymentGateway` interface; no coupling to one provider. |
| 14 — Shipping abstraction | PASS | `IShippingProvider` interface. |
| 17 — Order separation | PASS | Order/payment/shipping/refund states owned by spec 011/012/013. |
| 22/23 — Stack + architecture | PASS | .NET + Postgres; modular monolith. |
| 24 — State machines | PASS | Session state machine + payment-attempt state machine. |
| 25 — Audit | PASS | Every transition writes audit row. |
| 27 — UX quality | PASS | Explicit session steps, expiry warning, drift confirmation. |
| 28 — AI-build standard | PASS | Idempotency key + two-phase submit flow explicit. |

**Gate**: PASS.

## Phase A — Primitives
- `Primitives/Payment/IPaymentGateway.cs` + `StubPaymentGateway` (Dev only).
- `Primitives/Shipping/IShippingProvider.cs` + `StubShippingProvider` (Dev only).
- `Primitives/PaymentMethodCatalog.cs` — market-indexed list.
- `Primitives/CheckoutSessionStateMachine.cs` — enumerated transitions.
- `Primitives/IdempotencyStore.cs` — keyed cache for submit outcomes (DB-backed).
- `Primitives/DriftDetector.cs` — compares Preview vs Issue hash.

## Phase B — Persistence
- 5 tables: `checkout_sessions`, `payment_attempts`, `shipping_quotes`, `payment_webhook_events`, `idempotency_results`.
- Migration `Checkout_Initial`.

## Phase C — Customer slices
- `Customer/StartSession/*`
- `Customer/SetAddress/*`
- `Customer/GetShippingQuotes/*`
- `Customer/SelectShipping/*`
- `Customer/SelectPaymentMethod/*`
- `Customer/Summary/*`
- `Customer/Submit/*` (idempotent; two-phase)
- `Customer/ConfirmDrift/*` (accepts the pricing drift diff)

## Phase D — Webhook + admin
- `Webhooks/PaymentGatewayWebhook/*.cs` (single endpoint; multiplexed by provider id).
- `Admin/ListSessions/*`, `Admin/ForceExpire/*`.

## Phase E — Workers
- `CheckoutExpiryWorker` — 1 min tick.
- `PaymentReconciliationWorker` — bank transfer reconciliation handoff to admin.

## Phase F — Integration
- Hooks into spec 004 login (auth requirement), spec 005 restriction, spec 007-a Issue, spec 008 convert, spec 011 order-create, spec 012 invoice-issue.

## Phase G — Testing
- Unit: state machine, drift detector, idempotency store.
- Integration: 1000 concurrent submits (SC-003); duplicate webhooks (SC-007); COD cap matrix (SC-008).
- Contract: per-FR.
- Fault-injection: payment-success-then-order-create-failure saga compensation.

## Phase H — Polish
- AR editorial pass on `checkout.ar.icu`.
- OpenAPI regen; fingerprint; DoD.

## Complexity tracking
| Item | Why | Mitigation |
|---|---|---|
| Two-phase submit (submit → confirm) | Reliability under payment + order-create failure modes. | Saga with audit rows + idempotency key. |
| Idempotency store (5 min) | Network retries on client. | Small table, indexed; cleanup job. |
| Webhook dedup | Providers retry aggressively. | `payment_webhook_events` unique key on `(provider_id, event_id)`. |
| Stub providers at launch | ADR-007/008 TBD. | Interface contract locks the shape; real providers slot in. |

**Post-design re-check**: PASS.
