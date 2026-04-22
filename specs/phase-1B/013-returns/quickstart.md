# Quickstart — Returns & Refunds v1 (Spec 013)

**Date**: 2026-04-22 · **Target**: backend developer / AI agent, 30 minutes.

## Prerequisites
- Specs 008, 010, 011, 012 at DoD; A1 compose up.
- Migration `Returns_Initial` applied.
- Seed `return_policies` for KSA (14 d) + EG (7 d).

## 30-minute walk-through

1. **Seed a delivered KSA order** via spec 011 seed (2 lines, `payment.state=captured`, `fulfillment.state=delivered`, `delivered_at=NOW - 3 days`).
2. **Customer upload photo**: `POST /v1/customer/returns/photos` (multipart, 100 KB JPEG) → `{ photoId }`.
3. **Customer submit**: `POST /v1/customer/orders/{orderId}/returns` with 1 line × qty 1, reason `defective`, photoId → `{ returnNumber: RET-KSA-202604-000001, state: pending_review }`. `return.submitted` event emitted.
4. **Admin approve**: `POST /v1/admin/returns/{id}/approve` → state `approved`; `return.approved` event.
5. **Admin mark received**: received qty 1 → state `received`.
6. **Inspection**: `inspect` with `sellableQty=1, defectiveQty=0` → state `inspected`; spec 008 shows a +1 movement on the item's batch.
7. **Issue refund**: `POST /v1/admin/returns/{id}/issue-refund` → `IPaymentGateway.Refund` called with original captured txn + amount; state `refunded`; `refund.completed` event.
8. **Verify cascades**: spec 012 has a new credit note `CN-KSA-202604-000001` referencing the original invoice; spec 011's order `refund_state=partial`, `high_level_status=partially_refunded`.
9. **Over-refund guard**: Attempt `POST issue-refund` again (duplicate click) → 200 idempotent same result. Submit a SECOND RMA for the same line → `400 refund.over_refund_blocked` when amount would exceed captured.
10. **COD refund path**: On a separate COD order, repeat the flow. Refund transitions `pending → pending_manual_transfer`; admin submits `POST /v1/admin/refunds/{id}/confirm-bank-transfer` with IBAN → `completed`.

## Definition of Done
- ≥ 1 contract test per FR-001..FR-024.
- All 9 SCs wired to automated tests.
- State-machine fuzz (SC-004) in CI nightly.
- Over-refund guard (SC-006) tested with 100 crafted amounts.
- AR editorial pass on reason codes + status labels.
- OpenAPI regen + fingerprint + `docs/dod.md` green.
