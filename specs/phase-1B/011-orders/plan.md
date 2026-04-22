# Implementation Plan — Orders v1 (Spec 011)

**Branch**: `phase-1B-specs` · **Date**: 2026-04-22

## Technical Context
- **Runtime**: .NET 9 / C# 12.
- **DB**: PostgreSQL 16; schema `orders`.
- **Module**: `services/backend_api/Modules/Orders/`.
- **Deps**: EF Core, FluentValidation, `CsvHelper` (finance export), `System.Text.Json`.

## Constitution Check
| Principle | Gate | Note |
|---|---|---|
| 5 — Market-configurable | PASS | Order-number sequence + cancellation policy per market. |
| 6 — Multi-vendor-ready | PASS | `owner_id/vendor_id` on order + lines. |
| 9 — B2B | PASS | Quotations + bank transfer + PO preserved from cart. |
| 14 — Shipping abstraction | PASS | Shipment creation goes through spec 010's `IShippingProvider`. |
| 17 — Order separation | PASS | Four independent state machines + `high_level_status` view. |
| 21 — Operational readiness | PASS | Admin fulfillment + finance export + audit endpoints. |
| 22/23 — Stack + architecture | PASS | .NET + Postgres; modular monolith. |
| 24 — State machines | PASS | Each of the 4 machines enumerated. |
| 25 — Audit | PASS | Every transition + admin mutation audited. |
| 27 — UX quality | PASS | Timeline, tracking link, actions gated by policy. |
| 28 — AI-build standard | PASS | Explicit transition tables + reason codes. |

**Gate**: PASS.

## Phase A — Primitives
- `Primitives/OrderNumberSequencer.cs` — per (market, yyyymm) sequence via Postgres `nextval`.
- `Primitives/StateMachines/{OrderSm,PaymentSm,FulfillmentSm,RefundSm}.cs` — enumerated transitions.
- `Primitives/HighLevelStatusProjector.cs` — derives UX status.
- `Primitives/ReturnEligibilityEvaluator.cs` — per-market window.
- `Primitives/CancellationPolicy.cs` — per-market.

## Phase B — Persistence
- 9 tables (data-model.md). Migration `Orders_Initial`.
- Per-market Postgres sequences `orders.seq_{market}_{yyyymm}` created on demand.

## Phase C — Creation
- `Internal/CreateFromCheckout/*.cs` — called by spec 010 on confirm.
- `Internal/CreateFromQuotation/*.cs` — called on quote acceptance.
- Hooks spec 008 convert + spec 012 invoice issuance + events.

## Phase D — Customer slices
- `Customer/ListOrders/*` — paginated, filtered.
- `Customer/GetOrder/*` — detail with timeline.
- `Customer/Cancel/*` — policy-enforced.
- `Customer/Reorder/*` — seeds new cart.
- `Customer/Quotations/{List,Get,Accept,Reject}/*`.

## Phase E — Admin slices
- `Admin/ListOrders/*`, `GetOrder/*`, `GetAudit/*`.
- `Admin/Fulfillment/{StartPicking,MarkPacked,CreateShipment,MarkHandedToCarrier,MarkDelivered}/*`.
- `Admin/Payments/{ConfirmBankTransfer,ForceState}/*` (audit).
- `Admin/Quotations/{Create,Reject,Expire,ConvertToOrder}/*`.
- `Admin/FinanceExport/*` — CSV.

## Phase F — Workers
- `QuotationExpiryWorker` — daily tick; sets `status=expired` on passed `expires_at`.
- `PaymentFailedRecoveryWorker` — periodic retry per policy (spec 017-friendly).

## Phase G — Events + outbox
- Transactional outbox `orders.orders_outbox` with `event_type` + `aggregate_id`; dispatcher publishes to downstream consumers (analytics, notifications, search `order.placed`-derived signals if any).

## Phase H — Testing
- Unit: state machines (fuzz SC-003), sequencer (SC-002), cancellation policy matrix (SC-004).
- Integration: spec 010 confirm → order, webhook dedup (SC-005), COD delivery (SC-008), quotation conversion (SC-006).
- Contract: per FR.

## Phase I — Polish
- AR editorial `orders.ar.icu`.
- OpenAPI regen; fingerprint; DoD.

## Complexity tracking
| Item | Why | Mitigation |
|---|---|---|
| Four state machines | Principle 17 non-negotiable. | Each lives in its own file + test file. |
| Per-market order-number sequences | Human-readable + collision-free at scale. | DB-native sequence creation is trivial. |
| Quotation sibling aggregate | B2B buying flow needs quote-first semantics. | Re-uses pricing explanation — no duplicate math. |

**Post-design re-check**: PASS.
