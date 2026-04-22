# Research — Orders v1 (Spec 011)

**Date**: 2026-04-22

## R1 — Four separate state machines
**Decision**: `order_state`, `payment_state`, `fulfillment_state`, `refund_state` on the order row; each has its own enum + transition table.
**Rationale**: Principle 17 non-negotiable. A single status field cannot express "payment captured but awaiting stock".
**Alternative**: Single status — explicitly rejected by constitution.

## R2 — Order-number format
**Decision**: `ORD-{MARKET}-{YYYYMM}-{SEQ6}`; Postgres sequences `orders.seq_{market}_{yyyymm}` created on demand.
**Rationale**: Human-readable; collision-free; supports finance ops (group by month+market).
**Alternative**: UUID-only — unfriendly for call-center support.

## R3 — Quotations as sibling aggregate
**Decision**: `quotations`, `quotation_lines` tables, convertible to orders via `CreateFromQuotation`.
**Rationale**: B2B quotes have their own lifecycle (send → accept/reject → expire) distinct from orders.

## R4 — Shipments
**Decision**: One order → N shipments; `shipments`, `shipment_lines`.
**Rationale**: Supports backorders, split warehouse fulfillment (Phase 1.5 enabler), partial shipping.

## R5 — Cancellation policy
**Decision**: Per-market `cancellation_policy` config: cancel allowed if (no shipment) AND ((payment=authorized) OR (payment=captured AND hours_since_placed ≤ `captured_cancel_hours`)).
**Rationale**: Aligns with market-typical consumer expectations.

## R6 — Line-level snapshot
**Decision**: `order_lines` snapshot `sku`, `name_ar`, `name_en`, `unit_price_minor`, `tax_minor`, `discount_minor`, `qty`, `restricted`, `attributes_json` at order time.
**Rationale**: Catalog can change; order must be reproducible for invoice/refund.

## R7 — Outbox pattern
**Decision**: `orders.orders_outbox` transactional; dispatcher publishes to analytics/notifications.
**Rationale**: Guarantees at-least-once delivery without requiring a message broker at launch.

## R8 — Tax preservation
**Decision**: Store the pricing explanation id (spec 007-a) + tax amounts on each order line; refunds use these stored amounts (tax rate may have changed since).
**Rationale**: Principle 18 invoice correctness + Principle 25 audit.

## R9 — High-level status projection
**Decision**: Derived at read time by `HighLevelStatusProjector(orderState, paymentState, fulfillmentState, refundState)`; value examples: `pending_payment`, `processing`, `shipped`, `delivered`, `cancelled`, `partially_refunded`, `refunded`.
**Rationale**: Customers want a single word; internals retain the four truths.

## R10 — COD delivery → capture
**Decision**: Delivery confirmation (admin or carrier webhook) advances `payment.state = captured`.
**Rationale**: COD is pay-on-delivery by definition.

## R11 — Return eligibility
**Decision**: Evaluator reads `return_window_days` from spec 013 `returns.return_policies` (single source of truth; KSA launch 14 d, EG launch 7 d) and returns `eligible + reasonCode + daysRemaining`.
**Rationale**: Common market norms; admin-configurable; no duplicated policy column.

## R12 — Audit trail storage
**Decision**: `order_state_transitions` + admin-mutation rows via spec 003 `audit_log_entries`. The local table captures state-machine trace; the shared audit table captures admin actions with actor + before/after snapshots.
**Rationale**: Two consumers (UX timeline vs compliance audit) have different needs.
