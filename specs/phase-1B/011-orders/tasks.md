# Tasks — Orders v1 (Spec 011)

**Date**: 2026-04-22 · **FRs**: 26 · **SCs**: 10 · **State machines**: 4.

## Phase A — Primitives
- [ ] A1. `Primitives/OrderNumberSequencer.cs` + Postgres sequence helper (FR-002, SC-002).
- [ ] A2. `Primitives/StateMachines/OrderSm.cs` (FR-003).
- [ ] A3. `Primitives/StateMachines/PaymentSm.cs` (FR-003).
- [ ] A4. `Primitives/StateMachines/FulfillmentSm.cs` (FR-003).
- [ ] A5. `Primitives/StateMachines/RefundSm.cs` (FR-003).
- [ ] A6. `Primitives/HighLevelStatusProjector.cs` (FR-018).
- [ ] A7. `Primitives/CancellationPolicy.cs` (FR-022, SC-004).
- [ ] A8. `Primitives/ReturnEligibilityEvaluator.cs` (FR-009, SC-009).

## Phase B — Persistence
- [ ] B1. `Infrastructure/OrdersDbContext.cs` — 9 entities.
- [ ] B2. Migration `Orders_Initial` (data-model.md).
- [ ] B3. Seed `orders.cancellation_policies` for KSA + EG.

## Phase C — Creation
- [ ] C1. `Internal/CreateFromCheckout/*` — atomic order create (FR-001).
- [ ] C2. `Internal/CreateFromQuotation/*` — reuse stored explanation hash (FR-012, SC-006).
- [ ] C3. Hook inventory convert-reservation (spec 008) (FR-014).
- [ ] C4. Hook outbox event `order.placed`.

## Phase D — Customer slices
- [ ] D1. `Customer/ListOrders/*` (FR-009, FR-020).
- [ ] D2. `Customer/GetOrder/*` with timeline + derived high-level status (FR-018).
- [ ] D3. `Customer/Cancel/*` (FR-004, FR-022).
- [ ] D4. `Customer/Reorder/*` (FR-021).
- [ ] D5. `Customer/ReturnEligibility/*` (FR-009, SC-009).
- [ ] D6. `Customer/Quotations/{List,Get,Accept,Reject}/*` (FR-011).

## Phase E — Admin slices
- [ ] E1. `Admin/ListOrders/*` + `GetOrder/*` (FR-010).
- [ ] E2. `Admin/GetAudit/*` (FR-023).
- [ ] E3. `Admin/Fulfillment/StartPicking` (FR-005).
- [ ] E4. `Admin/Fulfillment/MarkPacked`.
- [ ] E5. `Admin/Fulfillment/CreateShipment` (FR-006).
- [ ] E6. `Admin/Fulfillment/MarkHandedToCarrier`.
- [ ] E7. `Admin/Fulfillment/MarkDelivered` — COD → capture path (FR-026, SC-008).
- [ ] E8. `Admin/Payments/ConfirmBankTransfer` (FR-025).
- [ ] E9. `Admin/Payments/ForceState` + audit (FR-008, SC-010).
- [ ] E10. `Admin/Quotations/{Create,Send,Expire,Convert}/*` (FR-011).
- [ ] E11. `Admin/FinanceExport/*` — streaming CSV (FR-010, SC-007).

## Phase F — Webhooks + Events
- [ ] F1. Wire spec 010 webhook dispatcher to advance payment states (FR-007, FR-024, SC-005).
- [ ] F2. Emit `payment.captured` → trigger spec 012 invoice (FR-015).
- [ ] F3. Emit fulfillment events (`shipped`, `delivered`).
- [ ] F4. Expose refund state seam for spec 013 (FR-016).

## Phase G — Workers
- [ ] G1. `QuotationExpiryWorker`.
- [ ] G2. `PaymentFailedRecoveryWorker` (policy-aware).
- [ ] G3. `OutboxDispatcher` for `orders_outbox`.

## Phase H — Testing
- [ ] H1. Unit: OrderNumberSequencer (SC-002 collision fuzz).
- [ ] H2. Unit: each state machine (SC-003 fuzz).
- [ ] H3. Unit: CancellationPolicy matrix (SC-004).
- [ ] H4. Integration: spec 010 confirm → order created (SC-001).
- [ ] H5. Integration: webhook dedup (SC-005).
- [ ] H6. Integration: quotation conversion hash identity (SC-006).
- [ ] H7. Integration: finance export reconciles with spec 012 (SC-007).
- [ ] H8. Integration: COD delivery → capture (SC-008).
- [ ] H9. Integration: return window boundaries (SC-009).
- [ ] H10. Integration: every admin mutation writes audit (SC-010).
- [ ] H11. Contract test per FR-001..FR-026.

## Phase I — Observability
- [ ] I1. Metrics: `orders.created_total`, `orders.cancelled_total`, `payments.webhook_dedup_hits`, `fulfillment.state_transitions`.
- [ ] I2. Traces: order create span from checkout.confirm → order.placed.
- [ ] I3. Structured logs with `orderId` + `orderNumber` correlation.

## Phase J — Polish
- [ ] J1. AR editorial `Resources/orders.ar.icu`.
- [ ] J2. OpenAPI regen + fingerprint.
- [ ] J3. DoD verification per `docs/dod.md`.

---

## MVP definition
Phases A + B + C + D1/D2/D3 + E1/E3..E7 + F1/F2 + G3 + H1..H5/H10 + J2.
