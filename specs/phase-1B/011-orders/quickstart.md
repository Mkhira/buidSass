# Quickstart — Orders v1 (Spec 011)

**Date**: 2026-04-22 · **Target**: backend developer / AI agent, 30 minutes.

## Prerequisites
- A1 Docker compose (Postgres 16 + Meilisearch) up.
- Specs 004, 005, 007-a, 008, 010 migrated.
- EF migrations `Orders_Initial` applied.

## 30-minute walk-through

1. **Seed a confirmed checkout session** via spec 010 seed command (market=KSA, 2 lines).
2. **Call** `POST /v1/internal/orders/from-checkout` with the session id. Response returns `{ orderId, orderNumber }` matching `ORD-KSA-202604-000001`.
3. **Verify DB state**: `orders.order_state='placed'`, `payment_state='authorized'`, `fulfillment_state='not_started'`, `refund_state='none'`; `order_lines` snapshot matches cart; `orders_outbox` has one `order.placed` row.
4. **Advance fulfillment** via admin endpoints: `start-picking` → `mark-packed` → `create-shipment` (provider=aramex, method=express, tracking=`TEST123`) → `mark-handed-to-carrier`. After each call, GET detail shows the new state + a new `order_state_transitions` row.
5. **Simulate payment webhook**: POST to spec 010 webhook endpoint with a `captured` event referencing the order's payment id. Order `payment_state=captured`; spec 012 invoice trigger event queued.
6. **Cancel attempt while shipment exists** → `409 order.cancel.shipment_exists`.
7. **Issue a customer cancel** on a SECOND fresh order (pre-shipment) → order `cancelled`, payment `voided`, inventory reservation released (spec 008 movement visible).
8. **Reorder**: `POST /v1/customer/orders/{id}/reorder` on the first (fulfilled) order → a new cart id returned; open the cart; both lines present.
9. **Admin audit trail**: `GET /v1/admin/orders/{id}/audit` shows the complete transition list plus admin mutations.
10. **Finance export**: `GET /v1/admin/orders/export?market=KSA&from=...&to=...&format=csv` streams a CSV; open in a spreadsheet and confirm line-level tax + discount columns.

## Definition of Done
- ≥ 1 contract test per FR-001..FR-026.
- All 10 SCs wired to automated tests (see tasks.md Phase H).
- State-machine fuzz (SC-003) seeded into CI as a nightly job.
- OpenAPI regen + fingerprint + constitution principle footer updated.
- AR strings in `orders.ar.icu` reviewed by native speaker.
- `docs/dod.md` gates green.
