# Tasks — Orders v1 (Spec 011)

**Date**: 2026-04-22 · **FRs**: 26 · **SCs**: 10 · **State machines**: 4.

> Status: spec 011 shipped in PR #32 (merged 2026-04-25). All artefacts present in
> `services/backend_api/Modules/Orders/`. Boxes ticked retroactively during spec 013 audit.

## Phase A — Primitives
- [X] A1. `Primitives/OrderNumberSequencer.cs` + Postgres sequence helper (FR-002, SC-002).
- [X] A2. `Primitives/StateMachines/OrderSm.cs` (FR-003).
- [X] A3. `Primitives/StateMachines/PaymentSm.cs` (FR-003).
- [X] A4. `Primitives/StateMachines/FulfillmentSm.cs` (FR-003).
- [X] A5. `Primitives/StateMachines/RefundSm.cs` (FR-003).
- [X] A6. `Primitives/HighLevelStatusProjector.cs` (FR-018).
- [X] A7. `Primitives/CancellationPolicy.cs` (FR-022, SC-004).
- [X] A8. `Primitives/ReturnEligibilityEvaluator.cs` (FR-009, SC-009).

## Phase B — Persistence
- [X] B1. `Infrastructure/OrdersDbContext.cs` — 9 entities.
- [X] B2. Migration `Orders_Initial` (data-model.md). Plus `_DeepReviewFixes` and `_PaymentSmAddPendingBnpl`.
- [X] B3. Seed `orders.cancellation_policies` for KSA + EG (in `Orders_Initial.cs`).

## Phase C — Creation
- [X] C1. `Internal/CreateFromCheckout/*` — atomic order create (FR-001).
- [X] C2. `Internal/CreateFromQuotation/*` — reuse stored explanation hash (FR-012, SC-006).
- [X] C3. Hook inventory convert-reservation (spec 008) (FR-014).
- [X] C4. Hook outbox event `order.placed`.

## Phase D — Customer slices
- [X] D1. `Customer/ListOrders/*` (FR-009, FR-020).
- [X] D2. `Customer/GetOrder/*` with timeline + derived high-level status (FR-018).
- [X] D3. `Customer/Cancel/*` (FR-004, FR-022).
- [X] D4. `Customer/Reorder/*` (FR-021).
- [X] D5. `Customer/ReturnEligibility/*` (FR-009, SC-009).
- [X] D6. `Customer/Quotations/{List,Get,Accept,Reject}/*` (FR-011).

## Phase E — Admin slices
- [X] E1. `Admin/ListOrders/*` + `GetOrder/*` (FR-010).
- [X] E2. `Admin/GetAudit/*` (FR-023).
- [X] E3. `Admin/Fulfillment/StartPicking` (FR-005).
- [X] E4. `Admin/Fulfillment/MarkPacked`.
- [X] E5. `Admin/Fulfillment/CreateShipment` (FR-006).
- [X] E6. `Admin/Fulfillment/MarkHandedToCarrier`.
- [X] E7. `Admin/Fulfillment/MarkDelivered` — COD → capture path (FR-026, SC-008).
- [X] E8. `Admin/Payments/ConfirmBankTransfer` (FR-025).
- [X] E9. `Admin/Payments/ForceState` + audit (FR-008, SC-010).
- [X] E10. `Admin/Quotations/{Create,Send,Expire,Convert}/*` (FR-011).
- [X] E11. `Admin/FinanceExport/*` — streaming CSV (FR-010, SC-007).

## Phase F — Webhooks + Events
- [X] F1. Wire spec 010 webhook dispatcher to advance payment states (FR-007, FR-024, SC-005). `Internal/PaymentWebhookAdvance/`.
- [X] F2. Emit `payment.captured` → trigger spec 012 invoice (FR-015).
- [X] F3. Emit fulfillment events (`shipped`, `delivered`).
- [X] F4. Expose refund state seam for spec 013 (FR-016). `Internal/AdvanceRefundState/` (extracted into `AdvanceRefundStateService` during spec 013 to share with the in-process `IOrderRefundStateAdvancer` adapter).

## Phase G — Workers
- [X] G1. `QuotationExpiryWorker`.
- [X] G2. `PaymentFailedRecoveryWorker` (policy-aware).
- [X] G3. `OutboxDispatcher` for `orders_outbox`.

## Phase H — Testing
- [X] H1. Unit: OrderNumberSequencer (SC-002 collision fuzz). `Tests/Orders.Tests/Integration/OrderNumberSequencerCollisionTests.cs` + `Unit/OrderNumberFormatTests.cs`.
- [X] H2. Unit: each state machine (SC-003 fuzz). `Unit/StateMachinesTests.cs`.
- [X] H3. Unit: CancellationPolicy matrix (SC-004). `Unit/CancellationPolicyTests.cs`.
- [X] H4. Integration: spec 010 confirm → order created (SC-001). `Integration/CreateFromCheckoutTests.cs`.
- [X] H5. Integration: webhook dedup (SC-005). `Integration/WebhookDedupTests.cs`.
- [X] H6. Integration: quotation conversion hash identity (SC-006). `Integration/QuotationConversionHashTests.cs`.
- [X] H7. Integration: finance export reconciles with spec 012 (SC-007). `Integration/FinanceExportTests.cs`.
- [X] H8. Integration: COD delivery → capture (SC-008). `Integration/CodDeliveryCaptureTests.cs`.
- [X] H9. Integration: return window boundaries (SC-009). `Integration/ReturnWindowBoundariesTests.cs`.
- [X] H10. Integration: every admin mutation writes audit (SC-010). `Integration/AdminAuditTests.cs`.
- [X] H11. Contract test per FR-001..FR-026. `Integration/FrContractTests.cs`.

## Phase I — Observability
- [X] I1. Metrics: `orders.created_total`, `orders.cancelled_total`, `payments.webhook_dedup_hits`, `fulfillment.state_transitions`. `Modules/Observability/OrdersMetrics.cs`.
- [X] I2. Traces: order create span from checkout.confirm → order.placed. `OrdersTracing.Source` ActivitySource.
- [X] I3. Structured logs with `orderId` + `orderNumber` correlation.

## Phase J — Polish
- [X] J1. AR editorial `Resources/orders.ar.icu`. `Modules/Orders/Messages/orders.{ar,en}.icu`.
- [X] J2. OpenAPI regen + fingerprint. `openapi.orders.json` shipped at repo root.
- [X] J3. DoD verification per `docs/dod.md` (PR #32 was DoD-green at merge).

---

## MVP definition
Phases A + B + C + D1/D2/D3 + E1/E3..E7 + F1/F2 + G3 + H1..H5/H10 + J2.
